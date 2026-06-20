namespace OpenRAG.Infrastructure.AI;

public sealed class OpenAiCompatibleChatOptions
{
    public const string SectionName = "AI:Chat";

    public string Provider { get; init; } = "Mock";
    public string BaseUrl { get; init; } = "https://api.deepseek.com/v1";
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "deepseek-chat";
    public int TimeoutSeconds { get; init; } = 120;
    public double Temperature { get; init; } = 0.2;
    public int? MaxTokens { get; init; } = 1024;
}
