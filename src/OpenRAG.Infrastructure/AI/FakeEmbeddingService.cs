using OpenRAG.Application.Abstractions.AI;

namespace OpenRAG.Infrastructure.AI;

/// <summary>
/// Placeholder embedding service. Returns a zero vector.
/// TODO: Replace with real OpenAI/Azure embedding service.
/// </summary>
public sealed class FakeEmbeddingService : IEmbeddingService
{
    public Task<EmbeddingResult> GenerateEmbeddingAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        var vector = Enumerable.Repeat(0f, 1536).ToList().AsReadOnly();
        return Task.FromResult(new EmbeddingResult(vector, "Fake", request.Model, 1536, "v1"));
    }
}
