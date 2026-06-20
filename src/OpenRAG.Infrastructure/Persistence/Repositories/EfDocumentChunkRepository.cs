using Microsoft.EntityFrameworkCore;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Domain.Documents;

namespace OpenRAG.Infrastructure.Persistence.Repositories;

public sealed class EfDocumentChunkRepository : IDocumentChunkRepository
{
    private readonly AppDbContext _dbContext;

    public EfDocumentChunkRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddRangeAsync(
        IReadOnlyCollection<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.DocumentChunks.AddRangeAsync(chunks, cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentChunk>> GetByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentChunks
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId
                        && c.DocumentId == documentId
                        && c.VersionId == versionId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> AnyForVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentChunks
            .AsNoTracking()
            .AnyAsync(c => c.TenantId == tenantId
                           && c.DocumentId == documentId
                           && c.VersionId == versionId,
                cancellationToken);
    }

    public async Task<int> CountByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentChunks
            .AsNoTracking()
            .CountAsync(c => c.TenantId == tenantId
                             && c.DocumentId == documentId
                             && c.VersionId == versionId,
                cancellationToken);
    }
}
