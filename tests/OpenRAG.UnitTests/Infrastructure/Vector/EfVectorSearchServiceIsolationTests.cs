using Microsoft.EntityFrameworkCore;
using OpenRAG.Application.Abstractions.Vector;
using OpenRAG.Infrastructure.Persistence;
using OpenRAG.Infrastructure.VectorSearch;
using Pgvector.EntityFrameworkCore;

namespace OpenRAG.UnitTests.Infrastructure.Vector;

public sealed class EfVectorSearchServiceIsolationTests
{
    private static readonly Guid TenantId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DocumentId = new("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Final_query_enforces_every_scope_compatibility_and_join_predicate()
    {
        using var context = CreateContext();
        var service = new EfVectorSearchService(context);

        var sql = CommandText(service.BuildSearchQuery(CreateRequest()).ToQueryString());

        Assert.Contains("WHERE e.\"TenantId\" = @", sql, StringComparison.Ordinal);
        Assert.Contains("e.\"DocumentId\" = ANY(@", sql, StringComparison.Ordinal);
        Assert.Contains("e.\"EmbeddingProvider\" = @", sql, StringComparison.Ordinal);
        Assert.Contains("e.\"EmbeddingModel\" = @", sql, StringComparison.Ordinal);
        Assert.Contains("e.\"EmbeddingDimensions\" = @", sql, StringComparison.Ordinal);
        Assert.Contains("e.\"EmbeddingVersion\" = @", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT @", sql, StringComparison.Ordinal);
        Assert.Contains("d.\"TenantId\" = e.\"TenantId\"", sql, StringComparison.Ordinal);
        Assert.Contains("v.\"TenantId\" = e.\"TenantId\"", sql, StringComparison.Ordinal);
        Assert.Contains("v.\"DocumentId\" = e.\"DocumentId\"", sql, StringComparison.Ordinal);
        Assert.Contains("c.\"TenantId\" = e.\"TenantId\"", sql, StringComparison.Ordinal);
        Assert.Contains("c.\"DocumentId\" = e.\"DocumentId\"", sql, StringComparison.Ordinal);
        Assert.Contains("c.\"VersionId\" = e.\"VersionId\"", sql, StringComparison.Ordinal);
        Assert.Contains("c.\"Id\" = e.\"ChunkId\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Final_query_parameterizes_all_dynamic_values()
    {
        using var context = CreateContext();
        var service = new EfVectorSearchService(context);

        var sql = CommandText(service.BuildSearchQuery(CreateRequest()).ToQueryString());

        Assert.DoesNotContain(TenantId.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DocumentId.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider-controlled", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("model-controlled", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("version-controlled", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("0.125", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Count_queries_use_the_same_authorized_and_compatible_scope()
    {
        using var context = CreateContext();
        var service = new EfVectorSearchService(context);
        var request = CreateRequest();

        var total = CommandText(service.BuildTotalCountQuery(request).ToQueryString());
        var compatible = CommandText(service.BuildCompatibleCountQuery(request).ToQueryString());

        Assert.Contains("e.\"TenantId\" = @", total, StringComparison.Ordinal);
        Assert.Contains("e.\"DocumentId\" = ANY(@", total, StringComparison.Ordinal);
        Assert.Contains("d.\"Status\" <> @", total, StringComparison.Ordinal);
        Assert.Contains("c.\"TenantId\" = e.\"TenantId\"", total, StringComparison.Ordinal);
        Assert.Contains("e.\"TenantId\" = @", compatible, StringComparison.Ordinal);
        Assert.Contains("e.\"DocumentId\" = ANY(@", compatible, StringComparison.Ordinal);
        Assert.Contains("e.\"EmbeddingProvider\" = @", compatible, StringComparison.Ordinal);
        Assert.Contains("e.\"EmbeddingModel\" = @", compatible, StringComparison.Ordinal);
        Assert.Contains("e.\"EmbeddingDimensions\" = @", compatible, StringComparison.Ordinal);
        Assert.Contains("e.\"EmbeddingVersion\" = @", compatible, StringComparison.Ordinal);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=openrag_query_test;Username=test;Password=test",
                npgsql => npgsql.UseVector())
            .Options;
        return new AppDbContext(options);
    }

    private static VectorSearchRequest CreateRequest() => new(
        TenantId,
        [0.125f, 0.25f],
        7,
        [DocumentId],
        "provider-controlled",
        "model-controlled",
        2,
        "version-controlled",
        "query-test");

    private static string CommandText(string queryString)
    {
        var selectIndex = queryString.IndexOf("SELECT", StringComparison.Ordinal);
        Assert.True(selectIndex >= 0, "Generated query did not contain SELECT.");
        return queryString[selectIndex..];
    }
}
