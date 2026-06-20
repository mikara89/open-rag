namespace OpenRAG.Infrastructure.AI;

public sealed class OpenAiCompatibleEmbeddingOptions
{
    public const string SectionName = "AI:Embeddings";

    public string Provider { get; init; } = "mock";
    public string BaseUrl { get; init; } = "http://localhost:1234/v1";
    public string ApiKey { get; init; } = "lm-studio";
    public string Model { get; init; } = "nomic-embed-text-v1.5";
    public string EmbeddingVersion { get; init; } = "v1";
    public int TimeoutSeconds { get; init; } = 120;
}
