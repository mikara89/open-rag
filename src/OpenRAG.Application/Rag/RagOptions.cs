namespace OpenRAG.Application.Rag;

/// <summary>
/// Configuration options for the RAG (Retrieval-Augmented Generation) pipeline.
/// Controls vector search behavior and chunk retrieval parameters.
/// </summary>
public sealed class RagOptions
{
    public const string SectionName = "Rag";

    /// <summary>
    /// Default number of top chunks to retrieve from vector search.
    /// Overridden by the TopK query parameter in ask requests.
    /// </summary>
    public int TopK { get; init; } = 5;

    /// <summary>
    /// Optional minimum cosine similarity score threshold.
    /// Chunks with a score below this value are excluded from context.
    /// Null means no minimum threshold (all results included).
    /// </summary>
    public double? MinScore { get; init; }
}
