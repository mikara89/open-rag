namespace OpenRAG.Application.Abstractions.AI;

/// <summary>
/// Request to generate intelligence (classification, summary, keywords, entities) for a document.
/// </summary>
public sealed record DocumentIntelligenceRequest(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    string FileName,
    string MarkdownContent,
    string? JsonContent,
    string CorrelationId
);

/// <summary>
/// Generated intelligence result for a document version.
/// </summary>
public sealed record DocumentIntelligenceResult(
    string? Classification,
    string? Summary,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<IntelligenceEntity> Entities,
    IReadOnlyDictionary<string, string> ExtractedMetadata,
    string Provider,
    string Model
);

/// <summary>
/// A named entity extracted from the document.
/// </summary>
public sealed record IntelligenceEntity(
    string Name,
    string Type
);

/// <summary>
/// Abstraction for document intelligence generation.
/// Implementations: Mock (returns static data), Chat (uses chat completion via prompt).
/// </summary>
public interface IDocumentIntelligenceService
{
    Task<DocumentIntelligenceResult> GenerateAsync(
        DocumentIntelligenceRequest request,
        CancellationToken cancellationToken = default);
}
