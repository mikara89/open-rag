using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Common;
using OpenRAG.Infrastructure.AI;

namespace OpenRAG.UnitTests.Infrastructure.AI;

public sealed class OpenAiCompatibleChatCompletionServiceTests
{
    [Fact]
    public async Task Returns_content_from_response()
    {
        var handler = CreateHandler(CreateSuccessResponse("Hello from DeepSeek!"));
        var service = CreateService(handler);

        var request = new ChatCompletionRequest(
            Guid.NewGuid(),
            new[] { new ChatMessageDto("user", "Hi") },
            "deepseek-chat", "corr");

        var result = await service.CompleteAsync(request);

        Assert.Equal("Hello from DeepSeek!", result.Content);
        Assert.Equal("OpenAICompatible", result.Provider);
    }

    [Fact]
    public async Task Uses_configured_model_when_request_model_empty()
    {
        string? requestBody = null;
        var handler = new InterceptingHandler(async (req, _) =>
        {
            requestBody = await req.Content!.ReadAsStringAsync();
            return CreateSuccessResponse("ok");
        });
        var service = CreateService(handler);

        await service.CompleteAsync(new ChatCompletionRequest(
            Guid.NewGuid(),
            new[] { new ChatMessageDto("user", "Hi") },
            "", "corr"));

        Assert.Contains("deepseek-chat", requestBody);
    }

    [Fact]
    public async Task Uses_request_model_when_provided()
    {
        string? requestBody = null;
        var handler = new InterceptingHandler(async (req, _) =>
        {
            requestBody = await req.Content!.ReadAsStringAsync();
            return CreateSuccessResponse("ok");
        });
        var service = CreateService(handler);

        await service.CompleteAsync(new ChatCompletionRequest(
            Guid.NewGuid(),
            new[] { new ChatMessageDto("user", "Hi") },
            "custom-model", "corr"));

        Assert.Contains("custom-model", requestBody);
    }

    [Fact]
    public async Task Throws_on_non_success_status()
    {
        var handler = new InterceptingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":\"Invalid API key\"}")
            }));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.CompleteAsync(new ChatCompletionRequest(
                Guid.NewGuid(),
                new[] { new ChatMessageDto("user", "Hi") },
                "deepseek-chat", "corr")));

        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task Throws_when_messages_empty()
    {
        var handler = CreateHandler(CreateSuccessResponse("ok"));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.CompleteAsync(new ChatCompletionRequest(
                Guid.NewGuid(), Array.Empty<ChatMessageDto>(), "model", "corr")));

        Assert.Contains("messages", ex.Message);
    }

    [Fact]
    public async Task Sets_authorization_header()
    {
        string? authHeader = null;
        var handler = new InterceptingHandler((req, _) =>
        {
            authHeader = req.Headers.Authorization?.ToString();
            return Task.FromResult(CreateSuccessResponse("ok"));
        });
        var service = CreateService(handler);

        await service.CompleteAsync(new ChatCompletionRequest(
            Guid.NewGuid(),
            new[] { new ChatMessageDto("user", "Hi") },
            "deepseek-chat", "corr"));

        Assert.NotNull(authHeader);
        Assert.Contains("Bearer", authHeader);
    }

    [Fact]
    public async Task Returns_token_counts_from_usage()
    {
        var handler = CreateHandler(CreateSuccessResponse("answer", promptTokens: 50, completionTokens: 30));
        var service = CreateService(handler);

        var result = await service.CompleteAsync(new ChatCompletionRequest(
            Guid.NewGuid(),
            new[] { new ChatMessageDto("user", "Hi") },
            "deepseek-chat", "corr"));

        Assert.Equal(50, result.InputTokens);
        Assert.Equal(30, result.OutputTokens);
    }

    // ══ Helpers ═══════════════════════════════════════════════════

    private static OpenAiCompatibleChatCompletionService CreateService(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new OpenAiCompatibleChatOptions
        {
            Provider = "OpenAICompatible",
            BaseUrl = "https://api.deepseek.com/v1",
            ApiKey = "sk-test-key",
            Model = "deepseek-chat",
            TimeoutSeconds = 120
        });
        return new OpenAiCompatibleChatCompletionService(httpClient, options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenAiCompatibleChatCompletionService>.Instance);
    }

    private static HttpMessageHandler CreateHandler(HttpResponseMessage response)
        => new InterceptingHandler((_, _) => Task.FromResult(response));

    private static HttpResponseMessage CreateSuccessResponse(
        string content, int promptTokens = 10, int completionTokens = 5)
    {
        var body = new
        {
            model = "deepseek-chat",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content }
                }
            },
            usage = new
            {
                prompt_tokens = promptTokens,
                completion_tokens = completionTokens,
                total_tokens = promptTokens + completionTokens
            }
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
    }

    private sealed class InterceptingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public InterceptingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> h) => _handler = h;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => _handler(r, ct);
    }
}
