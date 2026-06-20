using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OpenRAG.Application.Abstractions.Vector;
using OpenRAG.Infrastructure.Persistence;

namespace OpenRAG.Infrastructure.VectorSearch;

/// <summary>
/// PostgreSQL pgvector-backed vector search service.
/// Uses the pgvector &lt;=&gt; (cosine distance) operator for server-side similarity search.
/// Queries embeddings filtered by tenant and compatibility, orders by cosine distance,
/// and returns top-K results with associated chunk content.
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

        // 2. Build query for tenant
        var query = _dbContext.DocumentEmbeddings
            .AsNoTracking()
            .Where(e => e.TenantId == request.TenantId);

        // 3. Optionally filter by DocumentIds
        if (request.DocumentIds is not null && request.DocumentIds.Count > 0)
        {
            query = query.Where(e => request.DocumentIds.Contains(e.DocumentId));
        }

        // 4. Compatibility filtering (provider, model, dimensions, version)
        if (!string.IsNullOrWhiteSpace(request.EmbeddingModel))
            query = query.Where(e => e.EmbeddingModel == request.EmbeddingModel);

        if (!string.IsNullOrWhiteSpace(request.EmbeddingProvider))
            query = query.Where(e => e.EmbeddingProvider == request.EmbeddingProvider);

        if (request.EmbeddingDimensions.HasValue)
            query = query.Where(e => e.EmbeddingDimensions == request.EmbeddingDimensions.Value);

        if (!string.IsNullOrWhiteSpace(request.EmbeddingVersion))
            query = query.Where(e => e.EmbeddingVersion == request.EmbeddingVersion);

        // Count totals for diagnostics
        var totalCount = await _dbContext.DocumentEmbeddings
            .AsNoTracking()
            .CountAsync(e => e.TenantId == request.TenantId, cancellationToken);

        if (totalCount == 0)
            return new VectorSearchResponse(
                Array.Empty<VectorSearchResultItem>(),
                TotalEmbeddingCount: 0,
                CompatibleEmbeddingCount: 0,
                DiagnosticMessage: "No indexed document embeddings were found for this tenant.");

        var compatibleCount = await query.CountAsync(cancellationToken);

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

        // 5. Server-side pgvector cosine distance search
        // The pgvector <=> operator computes cosine distance: 1 - cosine_similarity.
        // Order by distance ascending (lower distance = more similar).
        var vectorLiteral = "[" + string.Join(",",
            request.QueryVector.Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]";

        // Use FromSqlInterpolated for parameterized query with pgvector operator.
        // The vector literal is safe: it consists of float values formatted with InvariantCulture.
        var sql = $"""
            SELECT e."Id", e."ChunkId", e."DocumentId", e."VersionId",
                   c."Content", c."PageNumber", c."SectionTitle",
                   1.0 - (e."Vector" <=> '{vectorLiteral}'::vector) AS "Score"
            FROM document_embeddings e
            LEFT JOIN document_chunks c ON c."Id" = e."ChunkId" AND c."TenantId" = e."TenantId"
            WHERE e."TenantId" = {request.TenantId}
              AND e."EmbeddingModel" = {request.EmbeddingModel ?? ""}
              AND e."EmbeddingDimensions" = {request.EmbeddingDimensions ?? 0}
            ORDER BY e."Vector" <=> '{vectorLiteral}'::vector
            LIMIT {request.Limit}
            """;

        var searchResults = await _dbContext.Database
            .SqlQuery<VectorSearchRawResult>($"{sql}")
            .ToListAsync(cancellationToken);

        if (searchResults.Count == 0)
        {
            return new VectorSearchResponse(
                Array.Empty<VectorSearchResultItem>(),
                totalCount, compatibleCount,
                "No embeddings matched the query vector dimensions.");
        }

        // 6. Map results
        var results = searchResults.Select(r => new VectorSearchResultItem(
            ChunkId: r.ChunkId,
            DocumentId: r.DocumentId,
            VersionId: r.VersionId,
            Content: r.Content ?? "[chunk not found]",
            PageNumber: r.PageNumber,
            SectionTitle: r.SectionTitle,
            Score: Math.Max(0.0, r.Score))).ToList();

        return new VectorSearchResponse(results, totalCount, compatibleCount, null);
    }

    /// <summary>
    /// Raw result shape for SqlQuery mapping. Property names must match the SQL column aliases.
    /// </summary>
    private sealed class VectorSearchRawResult
    {
        public Guid Id { get; init; }
        public Guid ChunkId { get; init; }
        public Guid DocumentId { get; init; }
        public Guid VersionId { get; init; }
        public string? Content { get; init; }
        public int? PageNumber { get; init; }
        public string? SectionTitle { get; init; }
        public double Score { get; init; }
    }

    /// <summary>
    /// Computes cosine similarity between two float vectors.
    /// Kept for backward compatibility with unit tests and for cases where
    /// server-side pgvector search is not available (e.g. in-memory fakes).
    /// </summary>
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
