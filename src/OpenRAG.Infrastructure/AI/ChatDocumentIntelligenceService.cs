using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Processing.GenerateIntelligence;

namespace OpenRAG.Infrastructure.AI;

/// <summary>
/// Chat-based document intelligence service. Uses the existing chat completion
/// service to generate classification, summary, keywords, and entities from
/// document markdown content via a structured prompt.
/// </summary>
public sealed class ChatDocumentIntelligenceService : IDocumentIntelligenceService
{
    private readonly IChatCompletionService _chatService;
    private readonly GenerateIntelligenceOptions _options;
    private readonly ILogger<ChatDocumentIntelligenceService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ChatDocumentIntelligenceService(
        IChatCompletionService chatService,
        IOptions<GenerateIntelligenceOptions> options,
        ILogger<ChatDocumentIntelligenceService> logger)
    {
        _chatService = chatService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DocumentIntelligenceResult> GenerateAsync(
        DocumentIntelligenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildPrompt(request);
        var messages = new List<ChatMessageDto>
        {
            new("system", "You are a document intelligence analyzer. You classify documents, " +
                          "generate concise summaries, extract keywords, and identify named entities. " +
                          "Always respond with valid JSON in the exact format requested."),
            new("user", prompt)
        };

        var chatRequest = new ChatCompletionRequest(
            TenantId: request.TenantId,
            Messages: messages,
            Model: "deepseek-chat", // Will use configured chat model
            CorrelationId: request.CorrelationId);

        var chatResult = await _chatService.CompleteAsync(chatRequest, cancellationToken);

        _logger.LogDebug("Chat intelligence response: {Response}", chatResult.Content);

        var parsed = ParseResponse(chatResult.Content);

        return new DocumentIntelligenceResult(
            Classification: parsed.Classification,
            Summary: Truncate(parsed.Summary),
            Keywords: parsed.Keywords ?? Array.Empty<string>(),
            Entities: parsed.Entities ?? Array.Empty<IntelligenceEntity>(),
            ExtractedMetadata: parsed.Metadata ?? new Dictionary<string, string>(),
            Provider: chatResult.Provider,
            Model: chatResult.Model);
    }

    private string BuildPrompt(DocumentIntelligenceRequest request)
    {
        return $"""
            Analyze the following document and return a JSON object with these fields:
            - classification (string): document type/category
            - summary (string): concise summary, max {_options.SummaryMaxCharacters} characters
            - keywords (string array): 5-10 key terms
            - entities (object array): each with "name" and "type" fields
            - metadata (object): key-value pairs for document-level metadata

            Document filename: {request.FileName}

            Content (truncated to {_options.MaxInputCharacters} chars):
            {request.MarkdownContent}

            Respond ONLY with valid JSON, no markdown fences, no additional text.
            """;
    }

    private static IntelligenceResponse ParseResponse(string content)
    {
        try
        {
            // Strip markdown code fences if present
            var json = content.Trim();
            if (json.StartsWith("```"))
            {
                var start = json.IndexOf('\n') + 1;
                var end = json.LastIndexOf("```");
                if (start > 0 && end > start)
                    json = json[start..end].Trim();
            }

            return JsonSerializer.Deserialize<IntelligenceResponse>(json, JsonOptions)
                   ?? new IntelligenceResponse();
        }
        catch (JsonException ex)
        {
            // Fallback: return raw content as summary
            return new IntelligenceResponse
            {
                Classification = "Unknown",
                Summary = content.Length > 500 ? content[..500] : content,
                Keywords = Array.Empty<string>(),
                Entities = Array.Empty<IntelligenceEntity>(),
                Metadata = new Dictionary<string, string>()
            };
        }
    }

    private string? Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        return value.Length > _options.SummaryMaxCharacters
            ? value[.._options.SummaryMaxCharacters]
            : value;
    }

    private sealed class IntelligenceResponse
    {
        public string? Classification { get; set; }
        public string? Summary { get; set; }
        public IReadOnlyList<string>? Keywords { get; set; }
        public IReadOnlyList<IntelligenceEntity>? Entities { get; set; }
        public IReadOnlyDictionary<string, string>? Metadata { get; set; }
    }
}
