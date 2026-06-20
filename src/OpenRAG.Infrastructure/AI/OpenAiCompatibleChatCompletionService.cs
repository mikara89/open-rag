using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Common;

namespace OpenRAG.Infrastructure.AI;

/// <summary>
/// OpenAI-compatible REST chat completion service.
/// Calls /v1/chat/completions (DeepSeek, LM Studio, Ollama, OpenAI, etc.).
/// </summary>
public sealed class OpenAiCompatibleChatCompletionService : IChatCompletionService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiCompatibleChatOptions _options;
    private readonly string? _resolvedApiKey;

    public OpenAiCompatibleChatCompletionService(
        HttpClient httpClient,
        Microsoft.Extensions.Options.IOptions<OpenAiCompatibleChatOptions> options,
        ILogger<OpenAiCompatibleChatCompletionService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            throw new InvalidOperationException("Chat completion BaseUrl cannot be empty.");

        if (string.IsNullOrWhiteSpace(_options.Model))
            throw new InvalidOperationException("Chat completion Model cannot be empty.");

        _resolvedApiKey = SecureApiKeyResolver.ResolveApiKey(
            _options.ApiKey,
            _options.ApiKeyEnvironmentVariable,
            ["OPENAI_API_KEY", "DEEPSEEK_API_KEY", "OPENRAG_CHAT_API_KEY"],
            logger);

        logger.LogInformation(
            "Chat completion configured: BaseUrl={BaseUrl}, Model={Model}, ApiKey={ApiKeyStatus}",
            _options.BaseUrl, _options.Model, SecureApiKeyResolver.KeyStatus(_resolvedApiKey));
    }

    public async Task<ChatCompletionResult> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Messages is null || request.Messages.Count == 0)
            throw new AppException("Chat messages cannot be empty.");

        var model = !string.IsNullOrWhiteSpace(request.Model)
            ? request.Model
            : _options.Model;

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = request.Messages.Select(m => new
            {
                role = m.Role.ToLowerInvariant(),
                content = m.Content
            }).ToList(),
            ["temperature"] = _options.Temperature
        };

        if (_options.MaxTokens.HasValue)
            requestBody["max_tokens"] = _options.MaxTokens.Value;

        var requestJson = JsonSerializer.Serialize(requestBody);

        var url = $"{_options.BaseUrl.TrimEnd('/')}/chat/completions";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_resolvedApiKey))
        {
            httpRequest.Headers.Add("Authorization", $"Bearer {_resolvedApiKey}");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is not AppException)
        {
            throw new AppException(
                $"Failed to call chat completion service at {url}: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var truncatedBody = errorBody.Length > 300 ? errorBody[..300] + "..." : errorBody;
            throw new AppException(
                $"Chat completion returned HTTP {(int)response.StatusCode}: {truncatedBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<ChatCompletionResponse>(responseJson);

        if (result?.Choices is null || result.Choices.Count == 0)
            throw new AppException("Chat completion returned no choices.");

        var content = result.Choices[0].Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
            throw new AppException("Chat completion returned empty content.");

        return new ChatCompletionResult(
            Content: content,
            Provider: "OpenAICompatible",
            Model: result.Model ?? model,
            InputTokens: result.Usage?.PromptTokens,
            OutputTokens: result.Usage?.CompletionTokens);
    }

    // ── JSON models ────────────────────────────────────────────────

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }

        [JsonPropertyName("usage")]
        public Usage? Usage { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("message")]
        public ChoiceMessage? Message { get; set; }
    }

    private sealed class ChoiceMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private sealed class Usage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }
    }
}
