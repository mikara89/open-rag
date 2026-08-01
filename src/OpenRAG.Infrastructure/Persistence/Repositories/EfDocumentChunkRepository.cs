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
        if (chunks.Count == 0)
            return;

        var first = chunks.First();
        var tenantId = first.TenantId;
        var documentId = first.DocumentId;
        var versionId = first.VersionId;
        RepositoryIsolationGuard.NonEmpty(tenantId, nameof(tenantId));
        RepositoryIsolationGuard.NonEmpty(documentId, nameof(documentId));
        RepositoryIsolationGuard.NonEmpty(versionId, nameof(versionId));
        foreach (var chunk in chunks)
        {
            RepositoryIsolationGuard.Equal(chunk.TenantId, tenantId, nameof(chunk.TenantId));
            RepositoryIsolationGuard.Equal(chunk.DocumentId, documentId, nameof(chunk.DocumentId));
            RepositoryIsolationGuard.Equal(chunk.VersionId, versionId, nameof(chunk.VersionId));
            RepositoryIsolationGuard.NonEmpty(chunk.Id, nameof(chunk.Id));
        }

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

    public async Task DeleteByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var chunks = await _dbContext.DocumentChunks
            .Where(c => c.TenantId == tenantId
                        && c.DocumentId == documentId
                        && c.VersionId == versionId)
            .ToListAsync(cancellationToken);

        _dbContext.DocumentChunks.RemoveRange(chunks);
    }

    public async Task<ChunkListResult> ListByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        int pageNumber,
        int pageSize,
        string? search = null,
        string? sectionTitle = null,
        int? pageNumberFilter = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.DocumentChunks
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId
                        && c.DocumentId == documentId
                        && c.VersionId == versionId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c => c.Content.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(sectionTitle))
        {
            query = query.Where(c => c.SectionTitle != null && c.SectionTitle.Contains(sectionTitle));
        }

        if (pageNumberFilter.HasValue)
        {
            query = query.Where(c => c.PageNumber == pageNumberFilter.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(c => c.ChunkIndex)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new ChunkListResult(items, pageNumber, pageSize, totalCount);
    }

    public async Task<DocumentChunk?> GetByIdForVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        Guid chunkId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentChunks
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.TenantId == tenantId
                     && c.DocumentId == documentId
                     && c.VersionId == versionId
                     && c.Id == chunkId,
                cancellationToken);
    }
}
