using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Common;

namespace OpenRAG.Infrastructure.AI;

/// <summary>
/// OpenAI-compatible REST embedding service.
/// Calls a local or remote /v1/embeddings endpoint (e.g., LM Studio, Ollama, OpenAI).
/// </summary>
public sealed class OpenAiCompatibleEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiCompatibleEmbeddingOptions _options;

    public OpenAiCompatibleEmbeddingService(
        HttpClient httpClient,
        Microsoft.Extensions.Options.IOptions<OpenAiCompatibleEmbeddingOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            throw new InvalidOperationException("OpenAI-compatible embedding BaseUrl cannot be empty.");

        if (string.IsNullOrWhiteSpace(_options.Model))
            throw new InvalidOperationException("OpenAI-compatible embedding Model cannot be empty.");
    }

    public async Task<EmbeddingResult> GenerateEmbeddingAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Input))
            throw new AppException("Embedding input cannot be empty.");

        var model = !string.IsNullOrWhiteSpace(request.Model)
            ? request.Model
            : _options.Model;

        var requestBody = new { model, input = request.Input };
        var url = $"{_options.BaseUrl.TrimEnd('/')}/embeddings";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json")
        };

        httpRequest.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is not AppException)
        {
            throw new AppException(
                $"Failed to call embedding service at {url}: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var truncatedBody = errorBody.Length > 300 ? errorBody[..300] + "..." : errorBody;
            throw new AppException(
                $"Embedding service returned HTTP {(int)response.StatusCode}: {truncatedBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<EmbeddingResponse>(responseJson);

        if (result is null || result.Data is null || result.Data.Count == 0)
            throw new AppException("Embedding service returned no embeddings.");

        var embedding = result.Data[0].Embedding;
        if (embedding is null || embedding.Length == 0)
            throw new AppException("Embedding service returned an empty vector.");

        return new EmbeddingResult(
            Vector: embedding,
            Provider: "OpenAICompatible",
            Model: result.Model ?? model,
            Dimensions: embedding.Length,
            EmbeddingVersion: _options.EmbeddingVersion);
    }

    // ── JSON models ────────────────────────────────────────────────

    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData>? Data { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }
    }

    private sealed class EmbeddingData
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}
