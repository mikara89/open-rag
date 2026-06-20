using Microsoft.EntityFrameworkCore;
using OpenRAG.Application.Abstractions.Vector;
using OpenRAG.Infrastructure.Persistence;

namespace OpenRAG.Infrastructure.Vector;

/// <summary>
/// EF Core-based vector search service.
/// Loads embeddings from the database and computes cosine similarity in memory.
/// TODO: Replace with pgvector cosine similarity (<-> operator) when Pgvector.EntityFrameworkCore
/// supports EF Core 10 / Npgsql 10. Embeddings are currently stored as bytea.
/// </summary>
public sealed class EfVectorSearchService : IVectorSearchService
{
    private readonly AppDbContext _dbContext;

    public EfVectorSearchService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<VectorSearchResponse> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate
        if (request.QueryVector is null || request.QueryVector.Count == 0)
            return new VectorSearchResponse(Array.Empty<VectorSearchResultItem>(), 0, 0, "Query vector is empty.");

        if (request.Limit <= 0)
            return new VectorSearchResponse(Array.Empty<VectorSearchResultItem>(), 0, 0, null);

        // 2. Load embeddings for tenant
        var query = _dbContext.DocumentEmbeddings
            .AsNoTracking()
            .Where(e => e.TenantId == request.TenantId);

        // 3. Optionally filter by DocumentIds
        if (request.DocumentIds is not null && request.DocumentIds.Count > 0)
        {
            query = query.Where(e => request.DocumentIds.Contains(e.DocumentId));
        }

        var allEmbeddings = await query
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        var totalCount = allEmbeddings.Count;

        if (totalCount == 0)
            return new VectorSearchResponse(
                Array.Empty<VectorSearchResultItem>(),
                TotalEmbeddingCount: 0,
                CompatibleEmbeddingCount: 0,
                DiagnosticMessage: "No indexed document embeddings were found for this tenant.");

        // 4. Filter by compatibility (model, provider, dimensions, version)
        var compatibleEmbeddings = allEmbeddings.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.EmbeddingModel))
            compatibleEmbeddings = compatibleEmbeddings.Where(e =>
                string.Equals(e.EmbeddingModel, request.EmbeddingModel, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(request.EmbeddingProvider))
            compatibleEmbeddings = compatibleEmbeddings.Where(e =>
                string.Equals(e.EmbeddingProvider, request.EmbeddingProvider, StringComparison.OrdinalIgnoreCase));

        if (request.EmbeddingDimensions.HasValue)
            compatibleEmbeddings = compatibleEmbeddings.Where(e =>
                e.EmbeddingDimensions == request.EmbeddingDimensions.Value);

        if (!string.IsNullOrWhiteSpace(request.EmbeddingVersion))
            compatibleEmbeddings = compatibleEmbeddings.Where(e =>
                string.Equals(e.EmbeddingVersion, request.EmbeddingVersion, StringComparison.OrdinalIgnoreCase));

        var compatibleList = compatibleEmbeddings.ToList();
        var compatibleCount = compatibleList.Count;

        if (compatibleCount == 0)
        {
            var diagMsg = $"Indexed embeddings exist ({totalCount} total), but none match the current query embedding: " +
                          $"model={request.EmbeddingModel ?? "any"}, dimensions={request.EmbeddingDimensions?.ToString() ?? "any"}. " +
                          $"Re-index documents with the same embedding provider/model, " +
                          $"or switch the ask endpoint to the same embedding provider used during ingestion.";
            return new VectorSearchResponse(
                Array.Empty<VectorSearchResultItem>(),
                totalCount, 0, diagMsg);
        }

        // 5. Filter out dimension mismatches and score
        var scoredResults = new List<(Domain.Documents.DocumentEmbedding Embedding, double Score)>();

        foreach (var emb in compatibleList)
        {
            if (emb.Vector.Length != request.QueryVector.Count)
                continue;

            var score = CosineSimilarity(request.QueryVector, emb.Vector);
            scoredResults.Add((emb, score));
        }

        var dimensionMatchedCount = scoredResults.Count;

        // 6. Sort descending by score, take top K
        var topResults = scoredResults
            .OrderByDescending(r => r.Score)
            .Take(request.Limit)
            .ToList();

        if (topResults.Count == 0)
        {
            var diagMsg = dimensionMatchedCount == 0
                ? $"Found {compatibleCount} compatible embeddings but none matched the query vector dimensions ({request.QueryVector.Count})."
                : null;
            return new VectorSearchResponse(
                Array.Empty<VectorSearchResultItem>(),
                totalCount, compatibleCount, diagMsg);
        }

        // 7. Load associated chunks
        var topChunkIds = topResults.Select(r => r.Embedding.ChunkId).Distinct().ToList();
        var chunks = await _dbContext.DocumentChunks
            .AsNoTracking()
            .Where(c => c.TenantId == request.TenantId && topChunkIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c, cancellationToken);

        // 8. Build results
        var results = new List<VectorSearchResultItem>();
        foreach (var (emb, score) in topResults)
        {
            chunks.TryGetValue(emb.ChunkId, out var chunk);

            results.Add(new VectorSearchResultItem(
                ChunkId: emb.ChunkId,
                DocumentId: emb.DocumentId,
                VersionId: emb.VersionId,
                Content: chunk?.Content ?? "[chunk not found]",
                PageNumber: chunk?.PageNumber,
                SectionTitle: chunk?.SectionTitle,
                Score: score));
        }

        return new VectorSearchResponse(results, totalCount, compatibleCount, null);
    }

    internal static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count != right.Count)
            throw new ArgumentException("Vectors must have the same dimensions.");

        double dotProduct = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;

        for (var i = 0; i < left.Count; i++)
        {
            dotProduct += (double)left[i] * right[i];
            leftMagnitude += (double)left[i] * left[i];
            rightMagnitude += (double)right[i] * right[i];
        }

        if (leftMagnitude == 0 || rightMagnitude == 0)
            return 0;

        return dotProduct / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }
}
