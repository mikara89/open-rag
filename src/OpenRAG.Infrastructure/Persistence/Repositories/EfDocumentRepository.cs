using Microsoft.EntityFrameworkCore;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Domain.Documents;

namespace OpenRAG.Infrastructure.Persistence.Repositories;

public sealed class EfDocumentRepository : IDocumentRepository, IDocumentAuthorizationRepository
{
    private readonly AppDbContext _dbContext;

    public EfDocumentRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        var tenantId = document.TenantId;
        RepositoryIsolationGuard.Equal(document.TenantId, tenantId, nameof(document.TenantId));
        RepositoryIsolationGuard.NonEmpty(document.Id, nameof(document.Id));
        foreach (var version in document.Versions)
        {
            RepositoryIsolationGuard.Equal(version.TenantId, tenantId, nameof(version.TenantId));
            RepositoryIsolationGuard.Equal(version.DocumentId, document.Id, nameof(version.DocumentId));
            RepositoryIsolationGuard.NonEmpty(version.Id, nameof(version.Id));
        }

        await _dbContext.Documents.AddAsync(document, cancellationToken);
    }

    public async Task<Document?> GetByIdAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.TenantId == tenantId && d.Id == documentId,
                cancellationToken);
    }

    public async Task<Document?> GetByIdWithVersionsAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Documents
            .AsNoTracking()
            .Include("_versions")
            .FirstOrDefaultAsync(
                d => d.TenantId == tenantId && d.Id == documentId,
                cancellationToken);
    }

    public async Task<Document?> GetByIdForUpdateAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Documents
            .Include("_versions")
            .FirstOrDefaultAsync(
                d => d.TenantId == tenantId && d.Id == documentId,
                cancellationToken);
    }

    public async Task<DocumentVersion?> GetVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                v => v.TenantId == tenantId
                     && v.DocumentId == documentId
                     && v.Id == versionId,
                cancellationToken);
    }

    public async Task<DocumentVersion?> GetVersionForUpdateAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentVersions
            .FirstOrDefaultAsync(
                v => v.TenantId == tenantId
                     && v.DocumentId == documentId
                     && v.Id == versionId,
                cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Documents
            .AsNoTracking()
            .AnyAsync(
                d => d.TenantId == tenantId && d.Id == documentId,
                cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetExistingIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken cancellationToken = default)
    {
        RepositoryIsolationGuard.NonEmpty(tenantId, nameof(tenantId));
        if (documentIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = await _dbContext.Documents
            .AsNoTracking()
            .Where(document => document.TenantId == tenantId
                               && documentIds.Contains(document.Id)
                               && document.Status != DocumentStatus.Deleted)
            .Select(document => document.Id)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    public async Task<DocumentListResult> ListAsync(
        Guid tenantId,
        int pageNumber,
        int pageSize,
        string? statusFilter = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Documents
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            query = query.Where(d => d.Status.ToString() == statusFilter);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(d => d.OriginalFileName.Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DocumentListItem(
                d.Id,
                d.OriginalFileName,
                string.Empty, // ContentType not directly on Document
                d.Status.ToString(),
                d.CreatedAt,
                d.UpdatedAt,
                d.CurrentVersionId))
            .ToListAsync(cancellationToken);

        return new DocumentListResult(items, pageNumber, pageSize, totalCount);
    }

    public Task DeleteAsync(
        Document document,
        CancellationToken cancellationToken = default)
    {
        RepositoryIsolationGuard.NonEmpty(document.TenantId, nameof(document.TenantId));
        RepositoryIsolationGuard.NonEmpty(document.Id, nameof(document.Id));
        _dbContext.Documents.Remove(document);
        return Task.CompletedTask;
    }
}
