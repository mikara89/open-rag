using Mediator;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;

namespace OpenRAG.Application.Documents.GetDocumentChunk;

public sealed class GetDocumentChunkHandler : IRequestHandler<GetDocumentChunkQuery, GetDocumentChunkResponse>
{
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentEmbeddingRepository _embeddingRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly ICurrentTenant _currentTenant;

    public GetDocumentChunkHandler(
        IDocumentChunkRepository chunkRepository,
        IDocumentEmbeddingRepository embeddingRepository,
        IDocumentRepository documentRepository,
        ICurrentTenant currentTenant)
    {
        _chunkRepository = chunkRepository;
        _embeddingRepository = embeddingRepository;
        _documentRepository = documentRepository;
        _currentTenant = currentTenant;
    }

    public async ValueTask<GetDocumentChunkResponse> Handle(
        GetDocumentChunkQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _currentTenant.TenantId;
        if (query.DocumentId == Guid.Empty
            || query.VersionId == Guid.Empty
            || query.ChunkId == Guid.Empty)
        {
            throw new RequestValidationException(
                "Document, version, and chunk identifiers must be non-empty.");
        }

        // Validate document/version exist for tenant
        var version = await _documentRepository.GetVersionAsync(
            tenantId, query.DocumentId, query.VersionId, cancellationToken);

        if (version is null)
            throw new ResourceNotFoundException();

        IsolationGuard.Equal(version.TenantId, tenantId, nameof(version.TenantId));
        IsolationGuard.Equal(version.DocumentId, query.DocumentId, nameof(version.DocumentId));
        IsolationGuard.Equal(version.Id, query.VersionId, nameof(version.Id));

        var chunk = await _chunkRepository.GetByIdForVersionAsync(
            tenantId, query.DocumentId, query.VersionId, query.ChunkId, cancellationToken);

        if (chunk is null)
            throw new ResourceNotFoundException();

        IsolationGuard.Equal(chunk.TenantId, tenantId, nameof(chunk.TenantId));
        IsolationGuard.Equal(chunk.DocumentId, query.DocumentId, nameof(chunk.DocumentId));
        IsolationGuard.Equal(chunk.VersionId, query.VersionId, nameof(chunk.VersionId));
        IsolationGuard.Equal(chunk.Id, query.ChunkId, nameof(chunk.Id));

        // Get embedding metadata for this chunk
        var embeddingMeta = await _embeddingRepository.GetMetadataByVersionAsync(
            tenantId, query.DocumentId, query.VersionId, cancellationToken);

        return new GetDocumentChunkResponse(
            ChunkId: chunk.Id,
            ChunkIndex: chunk.ChunkIndex,
            Content: chunk.Content,
            ContentHash: chunk.ContentHash,
            TokenCount: chunk.TokenCount,
            SectionTitle: chunk.SectionTitle,
            PageNumber: chunk.PageNumber,
            CreatedAt: chunk.CreatedAt,
            EmbeddingProvider: embeddingMeta?.Provider,
            EmbeddingModel: embeddingMeta?.Model,
            EmbeddingDimensions: embeddingMeta?.Dimensions,
            HasEmbedding: embeddingMeta is not null);
    }
}
