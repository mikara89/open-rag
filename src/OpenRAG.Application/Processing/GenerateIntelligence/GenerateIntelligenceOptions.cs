namespace OpenRAG.Application.Processing.GenerateIntelligence;

/// <summary>
/// Configuration for the document intelligence generation step.
/// </summary>
public sealed class GenerateIntelligenceOptions
{
    public const string SectionName = "Intelligence";

    /// <summary>
    /// Provider: "Mock" or "Chat".
    /// </summary>
    public string Provider { get; init; } = "Mock";

    /// <summary>
    /// Whether the intelligence step is enabled. If false, pipeline skips it.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Maximum characters of markdown content to pass to the intelligence provider.
    /// </summary>
    public int MaxInputCharacters { get; init; } = 12000;

    /// <summary>
    /// Maximum characters for the generated summary.
    /// </summary>
    public int SummaryMaxCharacters { get; init; } = 2000;
}
