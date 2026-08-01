using System.Text.Json;
using OpenRAG.Application.Abstractions.AI;

namespace OpenRAG.Infrastructure.AI;

/// <summary>
/// Mock document intelligence service. Returns static, deterministic intelligence
/// for local development and testing without requiring an external AI provider.
/// </summary>
public sealed class MockDocumentIntelligenceService : IDocumentIntelligenceService
{
    public Task<DocumentIntelligenceResult> GenerateAsync(
        DocumentIntelligenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var classification = DetermineClassification(request.FileName, request.MarkdownContent);
        var summary = GenerateSummary(request.MarkdownContent);
        var keywords = ExtractKeywords(request.MarkdownContent);
        var entities = ExtractEntities(request.MarkdownContent);
        var metadata = new Dictionary<string, string>
        {
            ["language"] = "en",
            ["pageCount"] = "1",
            ["contentType"] = "document"
        };

        return Task.FromResult(new DocumentIntelligenceResult(
            Classification: classification,
            Summary: summary,
            Keywords: keywords,
            Entities: entities,
            ExtractedMetadata: metadata,
            Provider: "Mock",
            Model: "mock-intelligence-v1"));
    }

    private static string DetermineClassification(string fileName, string markdown)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith(".pdf")) return "Technical Document";
        if (lower.EndsWith(".docx") || lower.EndsWith(".doc")) return "Word Document";
        if (lower.EndsWith(".md")) return "Markdown Document";
        return "General Document";
    }

    private static string GenerateSummary(string markdown)
    {
        // Take first 200 chars of content as a mock summary
        var clean = markdown.Replace("#", "").Replace("*", "").Replace("`", "").Trim();
        return clean.Length > 200 ? clean[..200] + "..." : clean;
    }

    private static IReadOnlyList<string> ExtractKeywords(string markdown)
    {
        // Simple keyword extraction: words > 4 chars, most frequent
        var words = markdown.Split([' ', '\n', '\r', '\t', '.', ',', ';', ':', '(', ')', '[', ']'],
            StringSplitOptions.RemoveEmptyEntries);
        return words
            .Where(w => w.Length > 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(w => w.ToLowerInvariant())
            .ToList()
            .AsReadOnly();
    }

    private static IReadOnlyList<IntelligenceEntity> ExtractEntities(string markdown)
    {
        return new List<IntelligenceEntity>
        {
            new("Document", "DocumentType"),
            new("OpenRAG", "Project")
        }.AsReadOnly();
    }
}
