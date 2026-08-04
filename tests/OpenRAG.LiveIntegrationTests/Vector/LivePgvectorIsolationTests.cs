using Microsoft.Extensions.DependencyInjection;
using OpenRAG.Application.Abstractions.Vector;
using OpenRAG.Domain.Documents;
using OpenRAG.Infrastructure.VectorSearch;
using OpenRAG.LiveIntegrationTests.Infrastructure;

namespace OpenRAG.LiveIntegrationTests.Vector;

[Collection(OpenRagLiveInfrastructureTestGroup.Name)]
[Trait("Category", "LiveIntegration")]
[Trait("Security", "CrossTenant")]
public sealed class LivePgvectorIsolationTests
{
    private readonly OpenRagLiveInfrastructureFixture _fixture;

    public LivePgvectorIsolationTests(OpenRagLiveInfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Real_pgvector_sql_enforces_tenant_filter_ranking_limit_and_identity()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(300);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA2(ids));
        await _fixture.SeedDocumentAsync(LiveTestData.TenantB1(ids));

        using var scope = _fixture.ApiFactory.Services.CreateScope();
        var vectorSearch = scope.ServiceProvider.GetRequiredService<IVectorSearchService>();
        Assert.IsType<EfVectorSearchService>(vectorSearch);

        var tenantA = await vectorSearch.SearchAsync(Request(
            LiveTestConstants.TenantA,
            [1f, 0f, 0f],
            limit: 1));
        var topA = Assert.Single(tenantA.Results);
        Assert.Equal(ids.DocumentA1, topA.DocumentId);
        Assert.Equal(ids.VersionA1, topA.VersionId);
        Assert.Equal(ids.ChunkA1, topA.ChunkId);
        Assert.Equal(LiveTestConstants.TenantA, topA.TenantId);

        var tenantB = await vectorSearch.SearchAsync(Request(
            LiveTestConstants.TenantB,
            [0f, 1f, 0f],
            limit: 5));
        var resultB = Assert.Single(tenantB.Results);
        Assert.Equal(ids.DocumentB1, resultB.DocumentId);
        Assert.Equal(ids.VersionB1, resultB.VersionId);
        Assert.Equal(ids.ChunkB1, resultB.ChunkId);
        Assert.Equal(LiveTestConstants.TenantB, resultB.TenantId);
        Assert.DoesNotContain(tenantB.Results, result => result.DocumentId == ids.DocumentA1);

        var foreignFilter = await vectorSearch.SearchAsync(Request(
            LiveTestConstants.TenantB,
            [0f, 1f, 0f],
            documentIds: [ids.DocumentA1]));
        Assert.Empty(foreignFilter.Results);

        var ownFilter = await vectorSearch.SearchAsync(Request(
            LiveTestConstants.TenantB,
            [0f, 1f, 0f],
            documentIds: [ids.DocumentB1]));
        Assert.Equal(ids.DocumentB1, Assert.Single(ownFilter.Results).DocumentId);
    }

    [Fact]
    public async Task Compatibility_predicates_and_deleted_documents_are_excluded_by_live_sql()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(301);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantB1(ids));

        using var scope = _fixture.ApiFactory.Services.CreateScope();
        var vectorSearch = scope.ServiceProvider.GetRequiredService<IVectorSearchService>();
        var mismatches = new[]
        {
            Request(LiveTestConstants.TenantB, [0f, 1f, 0f]) with { EmbeddingProvider = "other" },
            Request(LiveTestConstants.TenantB, [0f, 1f, 0f]) with { EmbeddingModel = "other" },
            Request(LiveTestConstants.TenantB, [0f, 1f, 0f]) with { EmbeddingDimensions = 4 },
            Request(LiveTestConstants.TenantB, [0f, 1f, 0f]) with { EmbeddingVersion = "other" }
        };
        foreach (var mismatch in mismatches)
        {
            var response = await vectorSearch.SearchAsync(mismatch);
            Assert.Empty(response.Results);
            Assert.Equal(0, response.CompatibleEmbeddingCount);
        }

        await _fixture.ResetAsync();
        var deletedIds = LiveTestIds.ForScenario(302);
        await _fixture.SeedDocumentAsync(
            LiveTestData.TenantB1(deletedIds, DocumentStatus.Deleted));
        using var deletedScope = _fixture.ApiFactory.Services.CreateScope();
        var deletedSearch = deletedScope.ServiceProvider.GetRequiredService<IVectorSearchService>();
        var deletedResponse = await deletedSearch.SearchAsync(Request(
            LiveTestConstants.TenantB,
            [0f, 1f, 0f]));
        Assert.Empty(deletedResponse.Results);
        Assert.Equal(0, deletedResponse.TotalEmbeddingCount);
    }

    [Fact]
    public async Task Simultaneous_tenant_searches_remain_isolated()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(303);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        await _fixture.SeedDocumentAsync(LiveTestData.TenantB1(ids));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var tenantATask = SearchInDedicatedScopeAsync(
            Request(LiveTestConstants.TenantA, [1f, 0f, 0f]),
            cancellation.Token);
        var tenantBTask = SearchInDedicatedScopeAsync(
            Request(LiveTestConstants.TenantB, [0f, 1f, 0f]),
            cancellation.Token);
        await Task.WhenAll(tenantATask, tenantBTask);
        var tenantA = await tenantATask;
        var tenantB = await tenantBTask;

        Assert.All(tenantA.Results, result =>
            Assert.Equal(LiveTestConstants.TenantA, result.TenantId));
        Assert.All(tenantB.Results, result =>
            Assert.Equal(LiveTestConstants.TenantB, result.TenantId));
        Assert.Equal(ids.DocumentA1, Assert.Single(tenantA.Results).DocumentId);
        Assert.Equal(ids.DocumentB1, Assert.Single(tenantB.Results).DocumentId);
    }

    private async Task<VectorSearchResponse> SearchInDedicatedScopeAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken)
    {
        using var scope = _fixture.ApiFactory.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<IVectorSearchService>()
            .SearchAsync(request, cancellationToken);
    }

    private static VectorSearchRequest Request(
        Guid tenantId,
        IReadOnlyList<float> vector,
        int limit = 5,
        IReadOnlyCollection<Guid>? documentIds = null) =>
        new(
            tenantId,
            vector,
            limit,
            documentIds,
            LiveTestConstants.EmbeddingProvider,
            LiveTestConstants.EmbeddingModel,
            LiveTestConstants.EmbeddingDimensions,
            LiveTestConstants.EmbeddingVersion,
            $"vector-{Guid.NewGuid():N}");
}
