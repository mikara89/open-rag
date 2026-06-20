using OpenRAG.Domain.Documents;

namespace OpenRAG.Application.Abstractions.Persistence;

public sealed record DocumentListResult(
    IReadOnlyList<DocumentListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount
);

public sealed record DocumentListItem(
    Guid DocumentId,
    string FileName,
    string ContentType,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? LatestVersionId
);

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

    Task<DocumentListResult> ListAsync(
        Guid tenantId,
        int pageNumber,
        int pageSize,
        string? statusFilter = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Document document,
        CancellationToken cancellationToken = default);
}
