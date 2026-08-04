using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OpenRAG.Domain.Documents;
using OpenRAG.LiveIntegrationTests.Infrastructure;

namespace OpenRAG.LiveIntegrationTests.Api;

[Collection(OpenRagLiveInfrastructureTestGroup.Name)]
[Trait("Category", "LiveIntegration")]
[Trait("Security", "CrossTenant")]
public sealed class LiveMutationConcurrencyTests
{
    private readonly OpenRagLiveInfrastructureFixture _fixture;

    public LiveMutationConcurrencyTests(OpenRagLiveInfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Tenant_A_reprocess_and_Tenant_B_delete_remain_isolated_when_concurrent()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(520);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        using var tenantA = _fixture.CreateTenantAClient();
        using var tenantB = _fixture.CreateTenantBClient();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var reprocess = tenantA.PostAsJsonAsync(
            $"/api/documents/{ids.DocumentA1}/reprocess",
            new { forcePreprocess = true },
            cancellation.Token);
        var foreignDelete = tenantB.DeleteAsync(
            $"/api/documents/{ids.DocumentA1}",
            cancellation.Token);
        await Task.WhenAll(reprocess, foreignDelete);
        using var reprocessResponse = await reprocess;
        using var deleteResponse = await foreignDelete;

        Assert.Equal(HttpStatusCode.Accepted, reprocessResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
        await using var context = _fixture.CreateDbContext();
        var document = await context.Documents.SingleAsync(item => item.Id == ids.DocumentA1);
        Assert.Equal(LiveTestConstants.TenantA, document.TenantId);
        Assert.Equal(DocumentStatus.Processing, document.Status);
    }

    [Fact]
    public async Task Tenant_A_delete_and_Tenant_B_artifact_read_cannot_cross_boundary()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(521);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        using var tenantA = _fixture.CreateTenantAClient();
        using var tenantB = _fixture.CreateTenantBClient();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var delete = tenantA.DeleteAsync(
            $"/api/documents/{ids.DocumentA1}",
            cancellation.Token);
        var foreignRead = tenantB.GetAsync(
            $"/api/documents/{ids.DocumentA1}/versions/{ids.VersionA1}/artifacts/markdown",
            cancellation.Token);
        await Task.WhenAll(delete, foreignRead);
        using var deleteResponse = await delete;
        using var readResponse = await foreignRead;

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, readResponse.StatusCode);
        var body = await readResponse.Content.ReadAsStringAsync(cancellation.Token);
        Assert.DoesNotContain(LiveTestConstants.TenantAMarker, body, StringComparison.Ordinal);

        await using var context = _fixture.CreateDbContext();
        Assert.False(await context.Documents.AnyAsync(item => item.Id == ids.DocumentA1));
        Assert.False(await context.DocumentChunks.AnyAsync(item => item.Id == ids.ChunkA1));
        Assert.False(await context.DocumentEmbeddings.AnyAsync(item => item.Id == ids.EmbeddingA1));
    }
}
