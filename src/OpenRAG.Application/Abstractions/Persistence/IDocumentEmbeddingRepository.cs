using OpenRAG.Domain.Documents;

namespace OpenRAG.Application.Abstractions.Persistence;

public sealed record DocumentEmbeddingMetadata(
    string Provider,
    string Model,
    int Dimensions,
    string Version,
    int Count
);

public interface IDocumentEmbeddingRepository
{
    Task AddRangeAsync(
        IReadOnlyCollection<DocumentEmbedding> embeddings,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentEmbedding>> GetByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<bool> AnyForVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        string embeddingModel,
        CancellationToken cancellationToken = default);

    Task<int> CountByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<DocumentEmbeddingMetadata?> GetMetadataByVersionAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default);
}
