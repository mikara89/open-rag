using Microsoft.EntityFrameworkCore;
using OpenRAG.Application.Abstractions.Vector;
using OpenRAG.Application.Common;
using OpenRAG.Domain.Documents;
using OpenRAG.Infrastructure.Persistence;
using Pgvector;

namespace OpenRAG.Infrastructure.VectorSearch;

/// <summary>
/// Audited PostgreSQL pgvector query. All variable values remain interpolation
/// arguments on FormattableString instances so EF/Npgsql parameterizes them.
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
        Validate(request);

        var totalCount = await BuildTotalCountQuery(request)
            .SingleAsync(cancellationToken);
        if (totalCount == 0)
        {
            return new VectorSearchResponse(
                Array.Empty<VectorSearchResultItem>(),
                0,
                0,
                "No indexed document embeddings were found in the authorized scope.");
        }

        var compatibleCount = await BuildCompatibleCountQuery(request)
            .SingleAsync(cancellationToken);
        if (compatibleCount == 0)
        {
            return new VectorSearchResponse(
                Array.Empty<VectorSearchResultItem>(),
                totalCount,
                0,
                "No compatible document embeddings were found in the authorized scope.");
        }

        var rows = await BuildSearchQuery(request).ToListAsync(cancellationToken);
        var results = rows.Select(row => new VectorSearchResultItem(
            TenantId: row.TenantId,
            ChunkId: row.ChunkId,
            DocumentId: row.DocumentId,
            VersionId: row.VersionId,
            Content: row.Content,
            PageNumber: row.PageNumber,
            SectionTitle: row.SectionTitle,
            Score: Math.Max(0.0, row.Score))).ToArray();

        return new VectorSearchResponse(results, totalCount, compatibleCount, null);
    }

    internal IQueryable<int> BuildTotalCountQuery(VectorSearchRequest request)
    {
        var documentIds = NormalizeDocumentIds(request.DocumentIds);
        var deletedStatus = DocumentStatus.Deleted.ToString();

        return _dbContext.Database.SqlQuery<int>($"""
            SELECT COUNT(*)::integer AS "Value"
            FROM document_embeddings e
            INNER JOIN documents d
              ON d."TenantId" = e."TenantId"
             AND d."Id" = e."DocumentId"
            INNER JOIN document_versions v
              ON v."TenantId" = e."TenantId"
             AND v."DocumentId" = e."DocumentId"
             AND v."Id" = e."VersionId"
            INNER JOIN document_chunks c
              ON c."TenantId" = e."TenantId"
             AND c."DocumentId" = e."DocumentId"
             AND c."VersionId" = e."VersionId"
             AND c."Id" = e."ChunkId"
            WHERE e."TenantId" = {request.TenantId}
              AND d."Status" <> {deletedStatus}
              AND (cardinality({documentIds}) = 0 OR e."DocumentId" = ANY({documentIds}))
            """);
    }

    internal IQueryable<int> BuildCompatibleCountQuery(VectorSearchRequest request)
    {
        var documentIds = NormalizeDocumentIds(request.DocumentIds);
        var provider = NormalizeOptional(request.EmbeddingProvider);
        var model = NormalizeOptional(request.EmbeddingModel);
        var dimensions = request.EmbeddingDimensions ?? 0;
        var version = NormalizeOptional(request.EmbeddingVersion);
        var deletedStatus = DocumentStatus.Deleted.ToString();

        return _dbContext.Database.SqlQuery<int>($"""
            SELECT COUNT(*)::integer AS "Value"
            FROM document_embeddings e
            INNER JOIN documents d
              ON d."TenantId" = e."TenantId"
             AND d."Id" = e."DocumentId"
            INNER JOIN document_versions v
              ON v."TenantId" = e."TenantId"
             AND v."DocumentId" = e."DocumentId"
             AND v."Id" = e."VersionId"
            INNER JOIN document_chunks c
              ON c."TenantId" = e."TenantId"
             AND c."DocumentId" = e."DocumentId"
             AND c."VersionId" = e."VersionId"
             AND c."Id" = e."ChunkId"
            WHERE e."TenantId" = {request.TenantId}
              AND d."Status" <> {deletedStatus}
              AND (cardinality({documentIds}) = 0 OR e."DocumentId" = ANY({documentIds}))
              AND ({provider} = '' OR e."EmbeddingProvider" = {provider})
              AND ({model} = '' OR e."EmbeddingModel" = {model})
              AND ({dimensions} = 0 OR e."EmbeddingDimensions" = {dimensions})
              AND ({version} = '' OR e."EmbeddingVersion" = {version})
            """);
    }

    internal IQueryable<VectorSearchRawResult> BuildSearchQuery(VectorSearchRequest request)
    {
        var documentIds = NormalizeDocumentIds(request.DocumentIds);
        var provider = NormalizeOptional(request.EmbeddingProvider);
        var model = NormalizeOptional(request.EmbeddingModel);
        var dimensions = request.EmbeddingDimensions ?? 0;
        var version = NormalizeOptional(request.EmbeddingVersion);
        var deletedStatus = DocumentStatus.Deleted.ToString();
        var queryVector = new Vector(request.QueryVector.ToArray());

        return _dbContext.Database.SqlQuery<VectorSearchRawResult>($"""
            SELECT e."TenantId", e."ChunkId", e."DocumentId", e."VersionId",
                   c."Content", c."PageNumber", c."SectionTitle",
                   1.0 - (e."Vector" <=> {queryVector}) AS "Score"
            FROM document_embeddings e
            INNER JOIN documents d
              ON d."TenantId" = e."TenantId"
             AND d."Id" = e."DocumentId"
            INNER JOIN document_versions v
              ON v."TenantId" = e."TenantId"
             AND v."DocumentId" = e."DocumentId"
             AND v."Id" = e."VersionId"
            INNER JOIN document_chunks c
              ON c."TenantId" = e."TenantId"
             AND c."DocumentId" = e."DocumentId"
             AND c."VersionId" = e."VersionId"
             AND c."Id" = e."ChunkId"
            WHERE e."TenantId" = {request.TenantId}
              AND d."Status" <> {deletedStatus}
              AND (cardinality({documentIds}) = 0 OR e."DocumentId" = ANY({documentIds}))
              AND ({provider} = '' OR e."EmbeddingProvider" = {provider})
              AND ({model} = '' OR e."EmbeddingModel" = {model})
              AND ({dimensions} = 0 OR e."EmbeddingDimensions" = {dimensions})
              AND ({version} = '' OR e."EmbeddingVersion" = {version})
            ORDER BY e."Vector" <=> {queryVector}
            LIMIT {request.Limit}
            """);
    }

    private static Guid[] NormalizeDocumentIds(IReadOnlyCollection<Guid>? documentIds) =>
        documentIds?.Distinct().ToArray() ?? Array.Empty<Guid>();

    private static string NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value;

    private static void Validate(VectorSearchRequest request)
    {
        if (request.TenantId == Guid.Empty)
            throw new IsolationViolationException("Vector search tenant scope is empty.");

        if (request.QueryVector is null || request.QueryVector.Count == 0)
            throw new RequestValidationException("The query vector cannot be empty.");

        if (request.Limit <= 0)
            throw new RequestValidationException("The vector result limit must be positive.");

        if (request.DocumentIds?.Any(id => id == Guid.Empty) == true)
            throw new RequestValidationException("Vector document filters must be non-empty identifiers.");
    }

    internal sealed class VectorSearchRawResult
    {
        public Guid TenantId { get; init; }
        public Guid ChunkId { get; init; }
        public Guid DocumentId { get; init; }
        public Guid VersionId { get; init; }
        public required string Content { get; init; }
        public int? PageNumber { get; init; }
        public string? SectionTitle { get; init; }
        public double Score { get; init; }
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
