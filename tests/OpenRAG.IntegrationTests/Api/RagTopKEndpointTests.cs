using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenRAG.Api.Security;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Abstractions.Vector;
using OpenRAG.Application.Rag;

namespace OpenRAG.IntegrationTests.Api;

public sealed class RagTopKEndpointTests
    : IClassFixture<AuthenticatedApiWebApplicationFactory>
{
    private static readonly Guid UserId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantId = new("22222222-2222-2222-2222-222222222222");
    private readonly AuthenticatedApiWebApplicationFactory _factory;

    public RagTopKEndpointTests(AuthenticatedApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_top_k_returns_400_without_invoking_rag_services(int topK)
    {
        var capture = new RagCaptureServices();
        using var factory = CreateFactory(capture);
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/rag/ask",
            new { question = "What is the answer?", topK },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = json.RootElement.GetProperty("errors").EnumerateArray().ToArray();
        var error = Assert.Single(errors);
        Assert.Equal("request.top_k_invalid", error.GetProperty("code").GetString());
        Assert.Equal("topK", error.GetProperty("target").GetString());
        Assert.Null(capture.EmbeddingRequest);
        Assert.Null(capture.VectorSearchRequest);
        Assert.Null(capture.ChatCompletionRequest);
    }

    [Fact]
    public async Task Omitted_top_k_succeeds_using_configured_default()
    {
        var capture = new RagCaptureServices();
        using var factory = CreateFactory(capture);
        var configuredTopK = factory.Services.GetRequiredService<IOptions<RagOptions>>().Value.TopK;
        using var client = CreateAuthenticatedClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/rag/ask",
            new { question = "What is the answer?" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(capture.EmbeddingRequest);
        Assert.Equal(configuredTopK, capture.VectorSearchRequest?.Limit);
        Assert.NotNull(capture.ChatCompletionRequest);
    }

    private WebApplicationFactory<OpenRAG.Api.AssemblyReference> CreateFactory(
        RagCaptureServices capture)
        => _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmbeddingService>();
                services.RemoveAll<IVectorSearchService>();
                services.RemoveAll<IChatCompletionService>();
                services.AddSingleton<IEmbeddingService>(capture);
                services.AddSingleton<IVectorSearchService>(capture);
                services.AddSingleton<IChatCompletionService>(capture);
            }));

    private HttpClient CreateAuthenticatedClient(
        WebApplicationFactory<OpenRAG.Api.AssemblyReference> factory)
    {
        var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _factory.CreateToken(
            [
                new Claim(OpenRagClaimTypes.UserId, UserId.ToString("D")),
                new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString("D"))
            ]));
        return client;
    }

    private sealed class RagCaptureServices :
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
                    "Relevant context",
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
            return Task.FromResult(new ChatCompletionResult("The answer", "test", "test", null, null));
        }
    }
}
