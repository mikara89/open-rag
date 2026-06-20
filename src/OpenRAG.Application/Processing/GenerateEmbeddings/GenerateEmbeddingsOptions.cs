namespace OpenRAG.Application.Processing.GenerateEmbeddings;

public sealed class GenerateEmbeddingsOptions
{
    public const string SectionName = "AI:Embeddings";

    /// <summary>
    /// Default embedding model name used for idempotency checks and embedding requests.
    /// Overridden by the actual model returned from IEmbeddingService.
    /// </summary>
    public string Model { get; init; } = "mock-embedding-8";
}
