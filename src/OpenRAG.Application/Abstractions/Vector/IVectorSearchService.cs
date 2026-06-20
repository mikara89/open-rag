namespace OpenRAG.Application.Abstractions.Vector;

public sealed record VectorSearchRequest(
    Guid TenantId,
    IReadOnlyList<float> QueryVector,
    int Limit,
    IReadOnlyCollection<Guid>? DocumentIds,
    string? EmbeddingProvider,
    string? EmbeddingModel,
    int? EmbeddingDimensions,
    string? EmbeddingVersion,
    string CorrelationId
);

public sealed record VectorSearchResultItem(
    Guid ChunkId,
    Guid DocumentId,
    Guid VersionId,
    string Content,
    int? PageNumber,
    string? SectionTitle,
    double Score
);

public sealed record VectorSearchResponse(
    IReadOnlyList<VectorSearchResultItem> Results,
    int TotalEmbeddingCount,
    int CompatibleEmbeddingCount,
    string? DiagnosticMessage
);

public interface IVectorSearchService
{
    Task<VectorSearchResponse> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken = default);
}
