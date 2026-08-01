using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenRAG.Api.Security;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Abstractions.Vector;

namespace OpenRAG.IntegrationTests.Api;

public sealed class TenantResolutionEndpointTests
    : IClassFixture<AuthenticatedApiWebApplicationFactory>
{
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private readonly AuthenticatedApiWebApplicationFactory _factory;

    public TenantResolutionEndpointTests(AuthenticatedApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("tenantId")]
    [InlineData("TenantId")]
    public async Task Jwt_tenant_wins_over_body_query_and_header_spoofing(string bodyPropertyName)
    {
        var capture = new TenantCaptureServices();
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmbeddingService>();
                services.RemoveAll<IVectorSearchService>();
                services.RemoveAll<IChatCompletionService>();
                services.AddSingleton<IEmbeddingService>(capture);
                services.AddSingleton<IVectorSearchService>(capture);
                services.AddSingleton<IChatCompletionService>(capture);
            }));
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _factory.CreateToken(
            [
                new Claim(OpenRagClaimTypes.UserId, UserId.ToString()),
                new Claim(OpenRagClaimTypes.TenantId, TenantA.ToString())
            ]));
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TenantB.ToString());

        using var response = await client.PostAsJsonAsync(
            $"/api/rag/ask?tenantId={TenantB}",
            new Dictionary<string, object?>
            {
                ["question"] = "Which tenant is trusted?",
                [bodyPropertyName] = TenantB
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TenantA, capture.EmbeddingRequest?.TenantId);
        Assert.Equal(TenantA, capture.VectorSearchRequest?.TenantId);
        Assert.Equal(TenantA, capture.ChatCompletionRequest?.TenantId);
    }

    [Fact]
    public async Task Request_without_tenant_input_uses_authenticated_claim()
    {
        var capture = new TenantCaptureServices();
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmbeddingService>();
                services.RemoveAll<IVectorSearchService>();
                services.RemoveAll<IChatCompletionService>();
                services.AddSingleton<IEmbeddingService>(capture);
                services.AddSingleton<IVectorSearchService>(capture);
                services.AddSingleton<IChatCompletionService>(capture);
            }));
        using var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _factory.CreateToken(
            [
                new Claim(OpenRagClaimTypes.UserId, UserId.ToString()),
                new Claim(OpenRagClaimTypes.TenantId, TenantA.ToString())
            ]));

        using var response = await client.PostAsJsonAsync(
            "/api/rag/ask",
            new { question = "Which tenant is trusted?" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TenantA, capture.EmbeddingRequest?.TenantId);
    }

    private sealed class TenantCaptureServices :
        IEmbeddingService,
        IVectorSearchService,
        IChatCompletionService
    {
        public EmbeddingRequest? EmbeddingRequest { get; private set; }
        public VectorSearchRequest? VectorSearchRequest { get; private set; }
        public ChatCompletionRequest? ChatCompletionRequest { get; private set; }

        public Task<EmbeddingResult> GenerateEmbeddingAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default)
        {
            EmbeddingRequest = request;
            return Task.FromResult(new EmbeddingResult([0.1f, 0.2f], "test", "test", 2, "v1"));
        }

        public Task<VectorSearchResponse> SearchAsync(
            VectorSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            VectorSearchRequest = request;
            return Task.FromResult(new VectorSearchResponse(
            [
                new VectorSearchResultItem(
                    request.TenantId,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "Trusted context",
                    null,
                    null,
                    0.9)
            ],
            1,
            1,
            null));
        }

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            ChatCompletionRequest = request;
            return Task.FromResult(new ChatCompletionResult("Tenant A", "test", "test", null, null));
        }
    }
}
