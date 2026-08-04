using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenRAG.Api.Security;
using OpenRAG.Application.Abstractions.Vector;
using OpenRAG.LiveIntegrationTests.Infrastructure;

namespace OpenRAG.LiveIntegrationTests.Rag;

[Collection(OpenRagLiveInfrastructureTestGroup.Name)]
[Trait("Category", "LiveIntegration")]
[Trait("Security", "CrossTenant")]
public sealed class LiveRagIsolationTests
{
    private readonly OpenRagLiveInfrastructureFixture _fixture;

    public LiveRagIsolationTests(OpenRagLiveInfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Unfiltered_and_authorized_RAG_use_only_Tenant_B_pgvector_context()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(600);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        await _fixture.SeedDocumentAsync(LiveTestData.TenantB1(ids));
        using var tenantB = _fixture.CreateTenantBClient();

        using (var unfiltered = await tenantB.PostAsJsonAsync(
                   "/api/rag/ask",
                   new { question = "What belongs to this tenant?" }))
        {
            Assert.Equal(HttpStatusCode.OK, unfiltered.StatusCode);
            var body = await unfiltered.Content.ReadAsStringAsync();
            Assert.Contains(LiveTestConstants.TenantBMarker, body, StringComparison.Ordinal);
            Assert.Contains(ids.DocumentB1.ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(LiveTestConstants.TenantAMarker, body, StringComparison.Ordinal);
            Assert.DoesNotContain(ids.DocumentA1.ToString(), body, StringComparison.OrdinalIgnoreCase);
        }

        var firstChat = Assert.Single(_fixture.ProviderProbe.ChatRequests);
        var firstPrompt = string.Join("\n", firstChat.Messages.Select(message => message.Content));
        Assert.Contains(LiveTestConstants.TenantBMarker, firstPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveTestConstants.TenantAMarker, firstPrompt, StringComparison.Ordinal);

        _fixture.ProviderProbe.Reset();
        using var filtered = await tenantB.PostAsJsonAsync(
            "/api/rag/ask",
            new
            {
                question = "Use my selected document.",
                documentIds = new[] { ids.DocumentB1 },
                topK = 1
            });
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        var filteredBody = await filtered.Content.ReadAsStringAsync();
        Assert.Contains(LiveTestConstants.TenantBMarker, filteredBody, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveTestConstants.TenantAMarker, filteredBody, StringComparison.Ordinal);
        var filteredPrompt = string.Join(
            "\n",
            Assert.Single(_fixture.ProviderProbe.ChatRequests).Messages.Select(message => message.Content));
        Assert.Contains(LiveTestConstants.TenantBMarker, filteredPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveTestConstants.TenantAMarker, filteredPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Foreign_mixed_and_missing_filters_fail_closed_before_AI_work()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(601);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        await _fixture.SeedDocumentAsync(LiveTestData.TenantB1(ids));
        using var tenantB = _fixture.CreateTenantBClient();
        var missingId = Guid.NewGuid();

        using (var missing = await AskWithFilterAsync(tenantB, [missingId]))
        using (var foreign = await AskWithFilterAsync(tenantB, [ids.DocumentA1]))
        {
            await ProblemDetailsAssertions.AssertEquivalentMissingAndForeignAsync(missing, foreign);
        }
        Assert.Empty(_fixture.ProviderProbe.EmbeddingRequests);
        Assert.Empty(_fixture.ProviderProbe.ChatRequests);

        using var mixed = await AskWithFilterAsync(
            tenantB,
            [ids.DocumentB1, ids.DocumentA1]);
        Assert.Equal(HttpStatusCode.NotFound, mixed.StatusCode);
        Assert.Contains(
            "resource.not_found",
            await mixed.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Empty(_fixture.ProviderProbe.EmbeddingRequests);
        Assert.Empty(_fixture.ProviderProbe.ChatRequests);
    }

    [Fact]
    public async Task Invalid_vector_identity_returns_generic_500_before_foreign_content_reaches_chat()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(602);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantB1(ids));
        var invalidVector = new InvalidIdentityVectorSearchService(ids);
        using var factory = _fixture.ApiFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IVectorSearchService>();
                services.AddSingleton<IVectorSearchService>(invalidVector);
            }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _fixture.ApiFactory.CreateToken(
            [
                new Claim(OpenRagClaimTypes.UserId, LiveTestConstants.UserB.ToString("D")),
                new Claim(OpenRagClaimTypes.TenantId, LiveTestConstants.TenantB.ToString("D"))
            ]));

        using var response = await client.PostAsJsonAsync(
            "/api/rag/ask",
            new { question = "Reject a corrupted vector identity." });
        await ProblemDetailsAssertions.AssertGenericInternalServerErrorAsync(response);
        Assert.True(invalidVector.Called);
        Assert.Single(_fixture.ProviderProbe.EmbeddingRequests);
        Assert.Empty(_fixture.ProviderProbe.ChatRequests);
    }

    private static Task<HttpResponseMessage> AskWithFilterAsync(
        HttpClient client,
        IReadOnlyCollection<Guid> documentIds) =>
        client.PostAsJsonAsync(
            "/api/rag/ask",
            new { question = "Use the requested filter.", documentIds });

    private sealed class InvalidIdentityVectorSearchService : IVectorSearchService
    {
        private readonly LiveTestIds _ids;

        public InvalidIdentityVectorSearchService(LiveTestIds ids)
        {
            _ids = ids;
        }

        public bool Called { get; private set; }

        public Task<VectorSearchResponse> SearchAsync(
            VectorSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Called = true;
            return Task.FromResult(new VectorSearchResponse(
            [
                new VectorSearchResultItem(
                    LiveTestConstants.TenantA,
                    _ids.ChunkA1,
                    _ids.DocumentA1,
                    _ids.VersionA1,
                    LiveTestConstants.TenantAMarker,
                    1,
                    "corrupted",
                    1)
            ],
            1,
            1,
            null));
        }
    }
}
