namespace OpenRAG.Infrastructure.AI;

public sealed class OpenAiCompatibleEmbeddingOptions
{
    public const string SectionName = "AI:Embeddings";

    public string Provider { get; init; } = "Mock";
    public string BaseUrl { get; init; } = "http://localhost:1234/v1";
    public string ApiKey { get; init; } = "";
    public string ApiKeyEnvironmentVariable { get; init; } = "";
    public string Model { get; init; } = "nomic-embed-text-v1.5";
    public int? Dimensions { get; init; }
    public string EmbeddingVersion { get; init; } = "v1";
    public int TimeoutSeconds { get; init; } = 120;
}
