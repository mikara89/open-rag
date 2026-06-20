using OpenRAG.Domain.Documents;

namespace OpenRAG.Application.Abstractions.Persistence;

public sealed record ChunkListResult(
    IReadOnlyList<DocumentChunk> Items,
    int PageNumber,
    int PageSize,
    int TotalCount
);

public interface IDocumentChunkRepository
{
    Task AddRangeAsync(
        IReadOnlyCollection<DocumentChunk> chunks,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentChunk>> GetByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<bool> AnyForVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<int> CountByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task DeleteByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<ChunkListResult> ListByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        int pageNumber,
        int pageSize,
        string? search = null,
        string? sectionTitle = null,
        int? pageNumberFilter = null,
        CancellationToken cancellationToken = default);

    Task<DocumentChunk?> GetByIdForVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        Guid chunkId,
        CancellationToken cancellationToken = default);
}
