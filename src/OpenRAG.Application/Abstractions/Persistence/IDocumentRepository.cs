using OpenRAG.Domain.Documents;

namespace OpenRAG.Application.Abstractions.Persistence;

public interface IDocumentRepository
{
    Task AddAsync(
        Document document,
        CancellationToken cancellationToken = default);

    Task<Document?> GetByIdAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<Document?> GetByIdWithVersionsAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<Document?> GetByIdForUpdateAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<DocumentVersion?> GetVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<DocumentVersion?> GetVersionForUpdateAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default);
}
