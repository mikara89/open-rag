using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Common;
using OpenRAG.Infrastructure.AI;

namespace OpenRAG.UnitTests.Infrastructure.AI;

public sealed class OpenAiCompatibleEmbeddingServiceTests
{
    [Fact]
    public async Task Returns_vector_from_openai_compatible_response()
    {
        var handler = CreateHandler(CreateSuccessResponse([0.1f, 0.2f, 0.3f]));
        var service = CreateService(handler);

        var request = new EmbeddingRequest(
            Guid.NewGuid(), "test input", "nomic-embed-text-v1.5", "corr");

        var result = await service.GenerateEmbeddingAsync(request);

        Assert.NotNull(result);
        Assert.Equal(3, result.Dimensions);
        Assert.Equal(3, result.Vector.Count);
        Assert.Equal(0.1f, result.Vector[0], 6);
    }

    [Fact]
    public async Task Uses_configured_base_url_and_embeddings_endpoint()
    {
        string? actualUrl = null;
        var handler = new InterceptingHandler((req, _) =>
        {
            actualUrl = req.RequestUri?.ToString();
            return Task.FromResult(CreateSuccessResponse([0.1f]));
        });
        var service = CreateService(handler);

        await service.GenerateEmbeddingAsync(
            new EmbeddingRequest(Guid.NewGuid(), "test", "nomic-embed-text-v1.5", "corr"));

        Assert.NotNull(actualUrl);
        Assert.Contains("/embeddings", actualUrl);
    }

    [Fact]
    public async Task Sets_authorization_bearer_header()
    {
        string? authHeader = null;
        var handler = new InterceptingHandler((req, _) =>
        {
            authHeader = req.Headers.Authorization?.ToString();
            return Task.FromResult(CreateSuccessResponse([0.1f]));
        });
        var service = CreateService(handler);

        await service.GenerateEmbeddingAsync(
            new EmbeddingRequest(Guid.NewGuid(), "test", "nomic-embed-text-v1.5", "corr"));

        Assert.NotNull(authHeader);
        Assert.Contains("Bearer", authHeader);
        Assert.Contains("lm-studio", authHeader);
    }

    [Fact]
    public async Task Uses_request_model_when_provided()
    {
        string? requestBody = null;
        var handler = new InterceptingHandler(async (req, _) =>
        {
            requestBody = await req.Content!.ReadAsStringAsync();
            return CreateSuccessResponse([0.1f]);
        });
        var service = CreateService(handler);

        await service.GenerateEmbeddingAsync(
            new EmbeddingRequest(Guid.NewGuid(), "test", "custom-model", "corr"));

        Assert.NotNull(requestBody);
        Assert.Contains("custom-model", requestBody);
    }

    [Fact]
    public async Task Uses_configured_model_when_request_model_is_empty()
    {
        string? requestBody = null;
        var handler = new InterceptingHandler(async (req, _) =>
        {
            requestBody = await req.Content!.ReadAsStringAsync();
            return CreateSuccessResponse([0.1f]);
        });
        var service = CreateService(handler);

        await service.GenerateEmbeddingAsync(
            new EmbeddingRequest(Guid.NewGuid(), "test", "", "corr"));

        Assert.NotNull(requestBody);
        Assert.Contains("nomic-embed-text-v1.5", requestBody);
    }

    [Fact]
    public async Task Throws_on_non_success_http_status()
    {
        var handler = new InterceptingHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":\"server error\"}")
            };
            return Task.FromResult(response);
        });
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.GenerateEmbeddingAsync(
                new EmbeddingRequest(Guid.NewGuid(), "test", "model", "corr")));

        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task Throws_when_response_has_no_embeddings()
    {
        var handler = CreateHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"object\":\"list\",\"data\":[],\"model\":\"test\"}")
        });
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.GenerateEmbeddingAsync(
                new EmbeddingRequest(Guid.NewGuid(), "test", "model", "corr")));

        Assert.Contains("no embeddings", ex.Message);
    }

    [Fact]
    public async Task Returns_correct_dimensions_from_response_vector_length()
    {
        var handler = CreateHandler(CreateSuccessResponse([1f, 2f, 3f, 4f, 5f]));
        var service = CreateService(handler);

        var result = await service.GenerateEmbeddingAsync(
            new EmbeddingRequest(Guid.NewGuid(), "test", "model", "corr"));

        Assert.Equal(5, result.Dimensions);
        Assert.Equal(5, result.Vector.Count);
    }

    [Fact]
    public async Task Returns_provider_and_model_metadata()
    {
        var handler = CreateHandler(CreateSuccessResponse([0.1f]));
        var service = CreateService(handler);

        var result = await service.GenerateEmbeddingAsync(
            new EmbeddingRequest(Guid.NewGuid(), "test", "custom-model", "corr"));

        Assert.Equal("OpenAICompatible", result.Provider);
        Assert.Equal("nomic-embed-text-v1.5", result.Model);
        Assert.Equal("v1", result.EmbeddingVersion);
    }

    [Fact]
    public async Task Throws_when_input_is_empty()
    {
        var handler = CreateHandler(CreateSuccessResponse([0.1f]));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.GenerateEmbeddingAsync(
                new EmbeddingRequest(Guid.NewGuid(), "", "model", "corr")));

        Assert.Contains("input", ex.Message);
    }

    // ══ Helpers ═══════════════════════════════════════════════════

    private static OpenAiCompatibleEmbeddingService CreateService(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:1234/v1")
        };

        var options = Options.Create(new OpenAiCompatibleEmbeddingOptions
        {
            Provider = "OpenAICompatible",
            BaseUrl = "http://localhost:1234/v1",
            ApiKey = "lm-studio",
            Model = "nomic-embed-text-v1.5",
            EmbeddingVersion = "v1",
            TimeoutSeconds = 120
        });

        return new OpenAiCompatibleEmbeddingService(httpClient, options);
    }

    private static HttpMessageHandler CreateHandler(HttpResponseMessage response)
    {
        return new InterceptingHandler((_, _) => Task.FromResult(response));
    }

    private static HttpResponseMessage CreateSuccessResponse(float[] embedding, string model = "nomic-embed-text-v1.5")
    {
        var responseData = new
        {
            @object = "list",
            data = new[]
            {
                new
                {
                    @object = "embedding",
                    index = 0,
                    embedding
                }
            },
            model,
            usage = new
            {
                prompt_tokens = 5,
                total_tokens = 5
            }
        };

        var json = JsonSerializer.Serialize(responseData);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class InterceptingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public InterceptingHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }
}
