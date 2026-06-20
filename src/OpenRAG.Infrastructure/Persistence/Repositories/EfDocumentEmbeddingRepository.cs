using Microsoft.EntityFrameworkCore;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Domain.Documents;

namespace OpenRAG.Infrastructure.Persistence.Repositories;

public sealed class EfDocumentEmbeddingRepository : IDocumentEmbeddingRepository
{
    private readonly AppDbContext _dbContext;

    public EfDocumentEmbeddingRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddRangeAsync(
        IReadOnlyCollection<DocumentEmbedding> embeddings,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.DocumentEmbeddings.AddRangeAsync(embeddings, cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentEmbedding>> GetByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentEmbeddings
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId
                        && e.DocumentId == documentId
                        && e.VersionId == versionId)
            .OrderBy(e => e.ChunkId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> AnyForVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        string embeddingModel,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentEmbeddings
            .AsNoTracking()
            .AnyAsync(e => e.TenantId == tenantId
                           && e.DocumentId == documentId
                           && e.VersionId == versionId
                           && e.EmbeddingModel == embeddingModel,
                cancellationToken);
    }

    public async Task<int> CountByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentEmbeddings
            .AsNoTracking()
            .CountAsync(e => e.TenantId == tenantId
                             && e.DocumentId == documentId
                             && e.VersionId == versionId,
                cancellationToken);
    }

    public async Task<DocumentEmbeddingMetadata?> GetMetadataByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var first = await _dbContext.DocumentEmbeddings
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId
                        && e.DocumentId == documentId
                        && e.VersionId == versionId)
            .OrderBy(e => e.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (first is null) return null;

        var count = await _dbContext.DocumentEmbeddings
            .AsNoTracking()
            .CountAsync(e => e.TenantId == tenantId
                             && e.DocumentId == documentId
                             && e.VersionId == versionId,
                cancellationToken);

        return new DocumentEmbeddingMetadata(
            Provider: first.EmbeddingProvider,
            Model: first.EmbeddingModel,
            Dimensions: first.EmbeddingDimensions,
            Version: first.EmbeddingVersion,
            Count: count);
    }
}
