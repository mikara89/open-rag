using OpenRAG.Application.Abstractions.Vector;

namespace OpenRAG.Infrastructure.VectorSearch;

/// <summary>
/// Placeholder vector search service. Returns an empty result list.
/// TODO: Replace with real pgvector search service.
/// </summary>
public sealed class FakeVectorSearchService : IVectorSearchService
{
    public Task<VectorSearchResponse> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new VectorSearchResponse(
            Array.Empty<VectorSearchResultItem>(), 0, 0,
            "No indexed document embeddings were found for this tenant."));
    }
}
