namespace OpenRAG.Application.Abstractions.AI;

public sealed record EmbeddingRequest(
    Guid TenantId,
    string Input,
    string Model,
    string CorrelationId
);

public sealed record EmbeddingResult(
    IReadOnlyList<float> Vector,
    string Provider,
    string Model,
    int Dimensions,
    string EmbeddingVersion
);

public interface IEmbeddingService
{
    Task<EmbeddingResult> GenerateEmbeddingAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default);
}
