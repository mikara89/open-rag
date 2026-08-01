using Mediator;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;
using OpenRAG.Application.Common.Results;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

namespace OpenRAG.Application.Documents.GetDocumentStatus;

public sealed class GetDocumentStatusHandler
    : IRequestHandler<GetDocumentStatusQuery, Result<GetDocumentStatusResponse>>
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentEmbeddingRepository _embeddingRepository;
    private readonly IProcessingRunRepository _processingRunRepository;
    private readonly ICurrentTenant _currentTenant;

    public GetDocumentStatusHandler(
        IDocumentRepository documentRepository,
        IDocumentChunkRepository chunkRepository,
        IDocumentEmbeddingRepository embeddingRepository,
        IProcessingRunRepository processingRunRepository,
        ICurrentTenant currentTenant)
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _embeddingRepository = embeddingRepository;
        _processingRunRepository = processingRunRepository;
        _currentTenant = currentTenant;
    }

    public async ValueTask<Result<GetDocumentStatusResponse>> Handle(
        GetDocumentStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.DocumentId == Guid.Empty)
        {
            return Result<GetDocumentStatusResponse>.Failure(
                ApplicationErrors.InvalidRequest(
                    "request.document_id_required",
                    "DocumentId cannot be empty.",
                    "documentId"));
        }

        var tenantId = _currentTenant.TenantId;

        var document = await _documentRepository.GetByIdWithVersionsAsync(
            tenantId, query.DocumentId, cancellationToken);

        if (document is null)
            return Result<GetDocumentStatusResponse>.Failure(ApplicationErrors.ResourceNotFound());

        IsolationGuard.Equal(document.TenantId, tenantId, nameof(document.TenantId));
        IsolationGuard.Equal(document.Id, query.DocumentId, nameof(document.Id));

        var versions = new List<DocumentVersionStatusDto>();
        var allRuns = new List<ProcessingRunHistoryDto>();

        foreach (var version in document.Versions)
        {
            IsolationGuard.Equal(version.TenantId, tenantId, nameof(version.TenantId));
            IsolationGuard.Equal(version.DocumentId, document.Id, nameof(version.DocumentId));
            IsolationGuard.NonEmpty(version.Id, nameof(version.Id));

            var chunkCount = await _chunkRepository.CountByVersionAsync(
                tenantId, document.Id, version.Id, cancellationToken);

            var embeddingCount = await _embeddingRepository.CountByVersionAsync(
                tenantId, document.Id, version.Id, cancellationToken);

            var embeddingMeta = await _embeddingRepository.GetMetadataByVersionAsync(
                tenantId, document.Id, version.Id, cancellationToken);

            // Load real processing runs and steps
            var runs = await _processingRunRepository.GetRunsByDocumentAsync(
                tenantId, document.Id, version.Id, cancellationToken);

            var runDtos = new List<ProcessingRunHistoryDto>();
            foreach (var run in runs)
            {
                IsolationGuard.Equal(run.TenantId, tenantId, nameof(run.TenantId));
                IsolationGuard.Equal(run.DocumentId, document.Id, nameof(run.DocumentId));
                IsolationGuard.Equal(run.VersionId, version.Id, nameof(run.VersionId));

                var runSteps = await _processingRunRepository.GetStepsByRunAsync(
                    tenantId, run.Id, cancellationToken);

                foreach (var step in runSteps)
                {
                    IsolationGuard.Equal(step.TenantId, tenantId, nameof(step.TenantId));
                    IsolationGuard.Equal(step.DocumentId, document.Id, nameof(step.DocumentId));
                    IsolationGuard.Equal(step.VersionId, version.Id, nameof(step.VersionId));
                    IsolationGuard.Equal(step.ProcessingRunId, run.Id, nameof(step.ProcessingRunId));
                }

                var stepDtos = runSteps.Select(s => new ProcessingStepHistoryDto(
                    Name: s.StepName.ToString(),
                    Status: s.Status.ToString(),
                    AttemptCount: s.AttemptCount,
                    StartedAt: s.StartedAt,
                    CompletedAt: s.CompletedAt,
                    HasError: s.Status == DocumentProcessingStepStatus.Failed
                )).ToList();

                runDtos.Add(new ProcessingRunHistoryDto(
                    RunId: run.Id,
                    Reason: run.RunReason.ToString(),
                    Status: run.Status.ToString(),
                    StartedAt: run.StartedAt,
                    CompletedAt: run.CompletedAt,
                    CorrelationId: run.CorrelationId,
                    Steps: stepDtos));
            }

            allRuns.AddRange(runDtos);

            // Derive step status from real data when available, fall back to version-based
            var steps = new List<ProcessingStepStatusDto>
            {
                new("Preprocess", DeriveStepStatus(version.Status, version.DoclingMarkdownObjectKey),
                    1, version.CreatedAt, null, version.Status == DocumentVersionStatus.Failed),
                new("Chunk", chunkCount > 0 ? "Completed" : "Pending",
                    1, null, null, false),
                new("GenerateEmbeddings", embeddingCount > 0 ? "Completed" : "Pending",
                    1, null, null, false)
            };

            var versionStatus = DeriveVersionStatus(version.Status, chunkCount, embeddingCount, steps);

            versions.Add(new DocumentVersionStatusDto(
                VersionId: version.Id,
                VersionNumber: version.VersionNumber,
                Status: versionStatus,
                HasSourceFile: !string.IsNullOrWhiteSpace(version.OriginalObjectKey),
                HasMarkdownArtifact: !string.IsNullOrWhiteSpace(version.DoclingMarkdownObjectKey)
                    && version.DoclingMarkdownObjectKey != "pending",
                HasJsonArtifact: !string.IsNullOrWhiteSpace(version.DoclingJsonObjectKey)
                    && version.DoclingJsonObjectKey != "pending",
                ChunkCount: chunkCount,
                EmbeddingCount: embeddingCount,
                EmbeddingProvider: embeddingMeta?.Provider,
                EmbeddingModel: embeddingMeta?.Model,
                EmbeddingDimensions: embeddingMeta?.Dimensions,
                Steps: steps));
        }

        // Derive document-level status from version progression
        var docStatus = versions.Count > 0
            ? versions[^1].Status
            : document.Status.ToString();

        return Result<GetDocumentStatusResponse>.Success(new GetDocumentStatusResponse(
            DocumentId: document.Id,
            Status: docStatus,
            CurrentVersionId: document.CurrentVersionId,
            OriginalFileName: document.OriginalFileName,
            CreatedAt: document.CreatedAt,
            UpdatedAt: document.UpdatedAt,
            Versions: versions,
            ProcessingRuns: allRuns));
    }

    private static string DeriveVersionStatus(
        DocumentVersionStatus versionStatus,
        int chunkCount,
        int embeddingCount,
        List<ProcessingStepStatusDto> steps)
    {
        if (steps.Any(s => s.Status == "Failed")) return "Failed";
        if (embeddingCount > 0) return "Ready";
        if (chunkCount > 0) return "Chunked";
        if (versionStatus == DocumentVersionStatus.Preprocessed) return "Preprocessed";
        if (versionStatus == DocumentVersionStatus.Preprocessing) return "Preprocessing";
        return "Uploaded";
    }

    private static string DeriveStepStatus(DocumentVersionStatus vs, string? mdKey)
    {
        return vs switch
        {
            DocumentVersionStatus.Preprocessed => "Completed",
            DocumentVersionStatus.Preprocessing => "Running",
            DocumentVersionStatus.Failed => "Failed",
            _ => !string.IsNullOrWhiteSpace(mdKey) && mdKey != "pending" ? "Completed" : "Pending"
        };
    }
}
