using Microsoft.EntityFrameworkCore;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Domain.Documents;

namespace OpenRAG.Infrastructure.Persistence.Repositories;

public sealed class EfDocumentIntelligenceRepository : IDocumentIntelligenceRepository
{
    private readonly AppDbContext _dbContext;

    public EfDocumentIntelligenceRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DocumentIntelligence?> GetByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<DocumentIntelligence>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i =>
                i.TenantId == tenantId
                && i.DocumentId == documentId
                && i.VersionId == versionId,
            cancellationToken);
    }

    public async Task AddAsync(
        DocumentIntelligence intelligence,
        CancellationToken cancellationToken = default)
    {
        RepositoryIsolationGuard.NonEmpty(intelligence.TenantId, nameof(intelligence.TenantId));
        RepositoryIsolationGuard.NonEmpty(intelligence.DocumentId, nameof(intelligence.DocumentId));
        RepositoryIsolationGuard.NonEmpty(intelligence.VersionId, nameof(intelligence.VersionId));
        RepositoryIsolationGuard.NonEmpty(intelligence.Id, nameof(intelligence.Id));
        await _dbContext.Set<DocumentIntelligence>().AddAsync(intelligence, cancellationToken);
    }

    public async Task DeleteByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var records = await _dbContext.Set<DocumentIntelligence>()
            .Where(i =>
                i.TenantId == tenantId
                && i.DocumentId == documentId
                && i.VersionId == versionId)
            .ToListAsync(cancellationToken);

        _dbContext.Set<DocumentIntelligence>().RemoveRange(records);
    }
}
