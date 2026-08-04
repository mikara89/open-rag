using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenRAG.Domain.Documents;
using OpenRAG.LiveIntegrationTests.Infrastructure;

namespace OpenRAG.LiveIntegrationTests.Api;

[Collection(OpenRagLiveInfrastructureTestGroup.Name)]
[Trait("Category", "LiveIntegration")]
[Trait("Security", "CrossTenant")]
public sealed class LiveApiIsolationTests
{
    private readonly OpenRagLiveInfrastructureFixture _fixture;

    public LiveApiIsolationTests(OpenRagLiveInfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Tenant_scoped_list_and_positive_resource_reads_return_only_owned_data()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(500);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA2(ids));
        await _fixture.SeedDocumentAsync(LiveTestData.TenantB1(ids));

        using var tenantB = _fixture.CreateTenantBClient();
        using var listResponse = await tenantB.GetAsync("/api/documents");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains(ids.DocumentB1.ToString(), listBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant-b-one.txt", listBody, StringComparison.Ordinal);
        Assert.DoesNotContain(ids.DocumentA1.ToString(), listBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ids.DocumentA2.ToString(), listBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tenant A document", listBody, StringComparison.Ordinal);
        using (var json = JsonDocument.Parse(listBody))
        {
            Assert.Equal(1, json.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        }

        using var tenantA = _fixture.CreateTenantAClient();
        await AssertSuccessfulContainsAsync(
            tenantA,
            $"/api/documents/{ids.DocumentA1}",
            ids.DocumentA1.ToString());
        await AssertSuccessfulContainsAsync(
            tenantB,
            $"/api/documents/{ids.DocumentB1}",
            ids.DocumentB1.ToString());
        await AssertSuccessfulContainsAsync(
            tenantA,
            $"/api/documents/{ids.DocumentA1}/status",
            ids.VersionA1.ToString());
        await AssertSuccessfulContainsAsync(
            tenantA,
            $"/api/documents/{ids.DocumentA1}/versions/{ids.VersionA1}/chunks",
            LiveTestConstants.TenantAMarker);
        await AssertSuccessfulContainsAsync(
            tenantA,
            $"/api/documents/{ids.DocumentA1}/versions/{ids.VersionA1}/chunks/{ids.ChunkA1}",
            LiveTestConstants.TenantAMarker);
        await AssertSuccessfulContainsAsync(
            tenantA,
            $"/api/documents/{ids.DocumentA1}/versions/{ids.VersionA1}/intelligence",
            LiveTestConstants.TenantAMarker);
        await AssertSuccessfulContainsAsync(
            tenantA,
            $"/api/documents/{ids.DocumentA1}/versions/{ids.VersionA1}/artifacts/markdown",
            LiveTestConstants.TenantAMarker);
        await AssertSuccessfulContainsAsync(
            tenantB,
            $"/api/documents/{ids.DocumentB1}/versions/{ids.VersionB1}/artifacts/json",
            LiveTestConstants.TenantBMarker);
    }

    [Fact]
    public async Task Every_resource_endpoint_makes_missing_and_foreign_resources_indistinguishable()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(501);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        await _fixture.SeedDocumentAsync(LiveTestData.TenantB1(ids));
        var missingDocument = Guid.NewGuid();
        var missingVersion = Guid.NewGuid();
        var missingChunk = Guid.NewGuid();

        using var tenantB = _fixture.CreateTenantBClient();
        var routes = new[]
        {
            ($"/api/documents/{missingDocument}", $"/api/documents/{ids.DocumentA1}"),
            ($"/api/documents/{missingDocument}/status", $"/api/documents/{ids.DocumentA1}/status"),
            ($"/api/documents/{missingDocument}/versions/{missingVersion}/artifacts/markdown",
                $"/api/documents/{ids.DocumentA1}/versions/{ids.VersionA1}/artifacts/markdown"),
            ($"/api/documents/{missingDocument}/versions/{missingVersion}/artifacts/json",
                $"/api/documents/{ids.DocumentA1}/versions/{ids.VersionA1}/artifacts/json"),
            ($"/api/documents/{missingDocument}/versions/{missingVersion}/chunks",
                $"/api/documents/{ids.DocumentA1}/versions/{ids.VersionA1}/chunks"),
            ($"/api/documents/{missingDocument}/versions/{missingVersion}/chunks/{missingChunk}",
                $"/api/documents/{ids.DocumentA1}/versions/{ids.VersionA1}/chunks/{ids.ChunkA1}"),
            ($"/api/documents/{missingDocument}/versions/{missingVersion}/intelligence",
                $"/api/documents/{ids.DocumentA1}/versions/{ids.VersionA1}/intelligence")
        };

        foreach (var (missingRoute, foreignRoute) in routes)
        {
            using var missing = await tenantB.GetAsync(missingRoute);
            using var foreign = await tenantB.GetAsync(foreignRoute);
            await ProblemDetailsAssertions.AssertEquivalentMissingAndForeignAsync(missing, foreign);
        }
    }

    [Fact]
    public async Task Foreign_delete_and_reprocess_are_404_equivalent_and_have_no_side_effects()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(502);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        var beforeCounts = await CaptureCountsAsync();
        var beforeStorage = await _fixture.GetStorageManifestAsync();
        using var tenantB = _fixture.CreateTenantBClient();
        var missingId = Guid.NewGuid();

        using (var missingDelete = await tenantB.DeleteAsync($"/api/documents/{missingId}"))
        using (var foreignDelete = await tenantB.DeleteAsync($"/api/documents/{ids.DocumentA1}"))
        {
            await ProblemDetailsAssertions.AssertEquivalentMissingAndForeignAsync(
                missingDelete,
                foreignDelete);
        }

        using (var missingReprocess = await PostReprocessAsync(tenantB, missingId, forcePreprocess: true))
        using (var foreignReprocess = await PostReprocessAsync(
                   tenantB,
                   ids.DocumentA1,
                   forcePreprocess: true))
        {
            await ProblemDetailsAssertions.AssertEquivalentMissingAndForeignAsync(
                missingReprocess,
                foreignReprocess);
        }

        Assert.Equal(beforeCounts, await CaptureCountsAsync());
        Assert.Equal(beforeStorage, await _fixture.GetStorageManifestAsync());
        Assert.Empty(_fixture.EventBus.Events);
        Assert.Empty(_fixture.ProviderProbe.PreprocessingRequests);
        Assert.Empty(_fixture.ProviderProbe.EmbeddingRequests);
        Assert.Empty(_fixture.ProviderProbe.ChatRequests);
    }

    [Fact]
    public async Task Invalid_reprocess_is_400_without_mutation_and_positive_reprocess_is_real()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(503);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        var before = await CaptureCountsAsync();
        using var tenantA = _fixture.CreateTenantAClient();

        using (var invalid = await PostReprocessAsync(tenantA, ids.DocumentA1))
        {
            await ProblemDetailsAssertions.AssertValidationAsync(
                invalid,
                "request.reprocess_stage_required",
                "stages");
        }
        Assert.Equal(before, await CaptureCountsAsync());
        Assert.Empty(_fixture.EventBus.Events);

        using var positive = await PostReprocessAsync(
            tenantA,
            ids.DocumentA1,
            forcePreprocess: true);
        Assert.Equal(HttpStatusCode.Accepted, positive.StatusCode);
        var body = await positive.Content.ReadAsStringAsync();
        Assert.Contains(ids.DocumentA1.ToString(), body, StringComparison.OrdinalIgnoreCase);
        var published = Assert.Single(_fixture.EventBus.Events);
        Assert.Equal("document.preprocess.requested", published.Topic);

        await using var context = _fixture.CreateDbContext();
        var document = await context.Documents.SingleAsync(item => item.Id == ids.DocumentA1);
        Assert.Equal(DocumentStatus.Processing, document.Status);
        Assert.Equal(before.Runs + 1, await context.DocumentProcessingRuns.CountAsync());
    }

    [Fact]
    public async Task Own_processing_conflicts_are_409_but_foreign_processing_state_is_still_404()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(504);
        await _fixture.SeedDocumentAsync(
            LiveTestData.TenantA1(ids, DocumentStatus.Processing));
        using var tenantA = _fixture.CreateTenantAClient();
        using var tenantB = _fixture.CreateTenantBClient();

        using (var ownDelete = await tenantA.DeleteAsync($"/api/documents/{ids.DocumentA1}"))
            await ProblemDetailsAssertions.AssertConflictAsync(ownDelete, "document.processing");
        using (var ownReprocess = await PostReprocessAsync(
                   tenantA,
                   ids.DocumentA1,
                   forcePreprocess: true))
            await ProblemDetailsAssertions.AssertConflictAsync(ownReprocess, "document.processing");

        using var foreign = await tenantB.DeleteAsync($"/api/documents/{ids.DocumentA1}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var foreignBody = await foreign.Content.ReadAsStringAsync();
        Assert.Contains("resource.not_found", foreignBody, StringComparison.Ordinal);
        Assert.DoesNotContain("processing", foreignBody, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<DatabaseCounts> CaptureCountsAsync()
    {
        await using var context = _fixture.CreateDbContext();
        return new DatabaseCounts(
            await context.Documents.CountAsync(),
            await context.DocumentVersions.CountAsync(),
            await context.DocumentChunks.CountAsync(),
            await context.DocumentEmbeddings.CountAsync(),
            await context.DocumentIntelligence.CountAsync(),
            await context.DocumentProcessingRuns.CountAsync(),
            await context.DocumentProcessingSteps.CountAsync());
    }

    private static Task<HttpResponseMessage> PostReprocessAsync(
        HttpClient client,
        Guid documentId,
        bool forcePreprocess = false,
        bool forceChunk = false,
        bool forceIntelligence = false,
        bool forceEmbeddings = false) =>
        client.PostAsJsonAsync(
            $"/api/documents/{documentId}/reprocess",
            new { forcePreprocess, forceChunk, forceIntelligence, forceEmbeddings });

    private static async Task AssertSuccessfulContainsAsync(
        HttpClient client,
        string route,
        string expected)
    {
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            expected,
            await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record DatabaseCounts(
        int Documents,
        int Versions,
        int Chunks,
        int Embeddings,
        int Intelligence,
        int Runs,
        int Steps);
}
