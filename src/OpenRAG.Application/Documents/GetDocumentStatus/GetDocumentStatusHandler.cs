using Mediator;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;
using OpenRAG.Domain.Documents;

namespace OpenRAG.Application.Documents.GetDocumentStatus;

public sealed class GetDocumentStatusHandler : IRequestHandler<GetDocumentStatusQuery, GetDocumentStatusResponse>
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentEmbeddingRepository _embeddingRepository;
    private readonly ICurrentTenant _currentTenant;

    public GetDocumentStatusHandler(
        IDocumentRepository documentRepository,
        IDocumentChunkRepository chunkRepository,
        IDocumentEmbeddingRepository embeddingRepository,
        ICurrentTenant currentTenant)
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _embeddingRepository = embeddingRepository;
        _currentTenant = currentTenant;
    }

    public async ValueTask<GetDocumentStatusResponse> Handle(
        GetDocumentStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.DocumentId == Guid.Empty)
        {
            throw new AppException("DocumentId cannot be empty.");
        }

        var tenantId = _currentTenant.TenantId;

        var document = await _documentRepository.GetByIdWithVersionsAsync(
            tenantId, query.DocumentId, cancellationToken);

        if (document is null)
        {
            throw new AppException($"Document '{query.DocumentId}' not found.");
        }

        var versions = new List<DocumentVersionStatusDto>();

        foreach (var version in document.Versions)
        {
            var chunkCount = await _chunkRepository.CountByVersionAsync(
                tenantId, document.Id, version.Id, cancellationToken);

            var embeddingCount = await _embeddingRepository.CountByVersionAsync(
                tenantId, document.Id, version.Id, cancellationToken);

            var embeddingMeta = await _embeddingRepository.GetMetadataByVersionAsync(
                tenantId, document.Id, version.Id, cancellationToken);

            var steps = new List<ProcessingStepStatusDto>
            {
                new("Preprocess", DeriveStepStatus(version.Status, version.DoclingMarkdownObjectKey),
                    1, version.CreatedAt, null, null),
                new("Chunk", chunkCount > 0 ? "Completed" : "Pending",
                    1, null, null, null),
                new("GenerateEmbeddings", embeddingCount > 0 ? "Completed" : "Pending",
                    1, null, null, null)
            };

            var versionStatus = DeriveVersionStatus(version.Status, chunkCount, embeddingCount, steps);

            versions.Add(new DocumentVersionStatusDto(
                VersionId: version.Id,
                VersionNumber: version.VersionNumber,
                Status: versionStatus,
                OriginalObjectKey: version.OriginalObjectKey,
                MarkdownObjectKey: version.DoclingMarkdownObjectKey,
                JsonObjectKey: version.DoclingJsonObjectKey,
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

        return new GetDocumentStatusResponse(
            DocumentId: document.Id,
            Status: docStatus,
            CurrentVersionId: document.CurrentVersionId,
            OriginalFileName: document.OriginalFileName,
            CreatedAt: document.CreatedAt,
            UpdatedAt: document.UpdatedAt,
            Versions: versions);
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
