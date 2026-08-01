using Mediator;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;
using OpenRAG.Domain.Documents;

namespace OpenRAG.Application.Documents.GetDocumentDetail;

public sealed class GetDocumentDetailHandler : IRequestHandler<GetDocumentDetailQuery, GetDocumentDetailResponse>
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentEmbeddingRepository _embeddingRepository;
    private readonly IDocumentIntelligenceRepository _intelligenceRepository;
    private readonly ICurrentTenant _currentTenant;

    public GetDocumentDetailHandler(
        IDocumentRepository documentRepository,
        IDocumentChunkRepository chunkRepository,
        IDocumentEmbeddingRepository embeddingRepository,
        IDocumentIntelligenceRepository intelligenceRepository,
        ICurrentTenant currentTenant)
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _embeddingRepository = embeddingRepository;
        _intelligenceRepository = intelligenceRepository;
        _currentTenant = currentTenant;
    }

    public async ValueTask<GetDocumentDetailResponse> Handle(
        GetDocumentDetailQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.DocumentId == Guid.Empty)
            throw new RequestValidationException("DocumentId cannot be empty.");

        var tenantId = _currentTenant.TenantId;

        var document = await _documentRepository.GetByIdWithVersionsAsync(
            tenantId, query.DocumentId, cancellationToken);

        if (document is null)
            throw new ResourceNotFoundException();

        IsolationGuard.Equal(document.TenantId, tenantId, nameof(document.TenantId));
        IsolationGuard.Equal(document.Id, query.DocumentId, nameof(document.Id));

        // Build latest version detail
        DocumentDetailVersionDto? latestVersion = null;
        DocumentDetailIntelligenceDto? intelligence = null;
        if (document.CurrentVersionId is not null)
        {
            var version = document.Versions.FirstOrDefault(v => v.Id == document.CurrentVersionId.Value);
            if (version is null)
                throw new IsolationViolationException("The current document version relationship is invalid.");

            IsolationGuard.Equal(version.TenantId, tenantId, nameof(version.TenantId));
            IsolationGuard.Equal(version.DocumentId, document.Id, nameof(version.DocumentId));
            IsolationGuard.Equal(version.Id, document.CurrentVersionId.Value, nameof(version.Id));

            var chunkCount = await _chunkRepository.CountByVersionAsync(
                tenantId, document.Id, version.Id, cancellationToken);
            var embeddingCount = await _embeddingRepository.CountByVersionAsync(
                tenantId, document.Id, version.Id, cancellationToken);
            var embeddingMeta = await _embeddingRepository.GetMetadataByVersionAsync(
                tenantId, document.Id, version.Id, cancellationToken);

            latestVersion = new DocumentDetailVersionDto(
                    VersionId: version.Id,
                    VersionNumber: version.VersionNumber,
                    HasSourceFile: !string.IsNullOrWhiteSpace(version.OriginalObjectKey),
                    HasMarkdownArtifact: !string.IsNullOrWhiteSpace(version.DoclingMarkdownObjectKey)
                        && !string.Equals(version.DoclingMarkdownObjectKey, "pending", StringComparison.Ordinal),
                    HasJsonArtifact: !string.IsNullOrWhiteSpace(version.DoclingJsonObjectKey)
                        && !string.Equals(version.DoclingJsonObjectKey, "pending", StringComparison.Ordinal),
                    ChunkCount: chunkCount,
                    EmbeddingCount: embeddingCount,
                    EmbeddingProvider: embeddingMeta?.Provider,
                    EmbeddingModel: embeddingMeta?.Model,
                    EmbeddingDimensions: embeddingMeta?.Dimensions);

            // Load intelligence if available
            var intel = await _intelligenceRepository.GetByVersionAsync(
                tenantId, document.Id, version.Id, cancellationToken);

            if (intel is not null)
            {
                IsolationGuard.Equal(intel.TenantId, tenantId, nameof(intel.TenantId));
                IsolationGuard.Equal(intel.DocumentId, document.Id, nameof(intel.DocumentId));
                IsolationGuard.Equal(intel.VersionId, version.Id, nameof(intel.VersionId));

                intelligence = new DocumentDetailIntelligenceDto(
                        Classification: intel.Classification,
                        Summary: intel.Summary,
                        IntelligenceProvider: intel.Provider,
                        IntelligenceModel: intel.Model,
                        IntelligenceUpdatedAt: intel.UpdatedAt);
            }
        }

        return new GetDocumentDetailResponse(
            DocumentId: document.Id,
            FileName: document.OriginalFileName,
            Status: document.Status.ToString(),
            CreatedAt: document.CreatedAt,
            UpdatedAt: document.UpdatedAt,
            LatestVersion: latestVersion,
            Intelligence: intelligence);
    }
}
