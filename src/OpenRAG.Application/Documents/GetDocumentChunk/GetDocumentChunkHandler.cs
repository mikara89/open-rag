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

        // Validate document/version exist for tenant
        var version = await _documentRepository.GetVersionAsync(
            tenantId, query.DocumentId, query.VersionId, cancellationToken);

        if (version is null)
            throw new AppException($"Version '{query.VersionId}' not found for document '{query.DocumentId}'.");

        var chunk = await _chunkRepository.GetByIdForVersionAsync(
            tenantId, query.DocumentId, query.VersionId, query.ChunkId, cancellationToken);

        if (chunk is null)
            throw new AppException($"Chunk '{query.ChunkId}' not found.");

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
