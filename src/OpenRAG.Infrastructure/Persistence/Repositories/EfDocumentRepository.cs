using Microsoft.EntityFrameworkCore;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Domain.Documents;

namespace OpenRAG.Infrastructure.Persistence.Repositories;

public sealed class EfDocumentRepository : IDocumentRepository
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
}
