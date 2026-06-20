using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.Processing;

namespace OpenRAG.Infrastructure.Processing;

/// <summary>
/// Docling JSON-aware chunker that uses structured Docling JSON when available.
/// Falls back to SimpleMarkdownChunker when JSON is missing or unsupported.
/// </summary>
public sealed class DoclingJsonAwareChunker : IDocumentChunker
{
    private readonly SimpleMarkdownChunker _fallback;
    private readonly ChunkingOptions _options;

    public DoclingJsonAwareChunker(
        IOptions<ChunkingOptions> options,
        SimpleMarkdownChunker fallback)
    {
        _options = options.Value;
        _fallback = fallback;
    }

    public Task<IReadOnlyList<DocumentChunkingResultItem>> ChunkAsync(
        DocumentChunkingRequest request,
        CancellationToken cancellationToken = default)
    {
        // Fall back to markdown if no JSON or disabled
        if (string.IsNullOrWhiteSpace(request.DoclingJson) || !_options.UseDoclingJsonWhenAvailable)
            return _fallback.ChunkAsync(request, cancellationToken);

        try
        {
            var blocks = ExtractBlocks(request.DoclingJson);
            if (blocks.Count == 0)
                return _fallback.ChunkAsync(request, cancellationToken);

            var chunks = GroupBlocksIntoChunks(blocks);
            if (chunks.Count == 0)
                return _fallback.ChunkAsync(request, cancellationToken);

            return Task.FromResult<IReadOnlyList<DocumentChunkingResultItem>>(chunks);
        }
        catch (JsonException)
        {
            return _fallback.ChunkAsync(request, cancellationToken);
        }
    }

    // ── Block extraction ──────────────────────────────────────────

    private static List<DoclingBlock> ExtractBlocks(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var blocks = new List<DoclingBlock>();
        Traverse(doc.RootElement, "", null, blocks);
        return blocks;
    }

    private static void Traverse(
        JsonElement element, string currentSection, int? currentPage,
        List<DoclingBlock> blocks)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                TraverseObject(element, currentSection, currentPage, blocks);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Traverse(item, currentSection, currentPage, blocks);
                break;
        }
    }

    private static void TraverseObject(
        JsonElement obj, string currentSection, int? currentPage,
        List<DoclingBlock> blocks)
    {
        // Track section/heading changes
        var section = TryGetString(obj, "section_title", "sectionTitle", "heading", "title");
        if (!string.IsNullOrWhiteSpace(section))
            currentSection = section;

        // Track page number
        var page = TryGetInt(obj, "page_no", "pageNumber", "page");
        if (page.HasValue)
            currentPage = page.Value;

        // Try to extract text content
        var text = TryGetString(obj, "text", "content", "markdown");
        var kind = "text";

        // Check for table markdown
        if (string.IsNullOrWhiteSpace(text))
        {
            text = TryGetString(obj, "markdown");
            if (!string.IsNullOrWhiteSpace(text))
                kind = "table";
        }

        // Check for table data
        if (string.IsNullOrWhiteSpace(text) && obj.TryGetProperty("data", out _))
        {
            var tableMd = TryGetString(obj, "markdown");
            if (!string.IsNullOrWhiteSpace(tableMd))
            {
                text = "Table:\n" + tableMd;
                kind = "table";
            }
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            var label = TryGetString(obj, "label", "name");
            if (!string.IsNullOrWhiteSpace(label))
                text = label + ": " + text;

            blocks.Add(new DoclingBlock(text.Trim(), currentSection, currentPage, kind));
        }

        // Recurse into child collections
        foreach (var prop in new[] { "children", "body", "texts", "paragraphs", "tables", "items" })
        {
            if (obj.TryGetProperty(prop, out var child))
                Traverse(child, currentSection, currentPage, blocks);
        }
    }

    // ── Chunk grouping ─────────────────────────────────────────────

    private List<DocumentChunkingResultItem> GroupBlocksIntoChunks(List<DoclingBlock> blocks)
    {
        var results = new List<DocumentChunkingResultItem>();
        var currentContent = new StringBuilder();
        string? currentSection = null;
        int? currentPage = null;
        var chunkIndex = 0;

        foreach (var block in blocks)
        {
            var blockSize = block.Text.Length;

            // If this block alone exceeds max, split it
            if (blockSize >= _options.MaxChunkCharacters && currentContent.Length == 0)
            {
                foreach (var part in SplitLargeText(block.Text))
                {
                    results.Add(CreateResult(chunkIndex++, part, block.SectionTitle, block.PageNumber));
                }
                continue;
            }

            // Should we start a new chunk?
            var shouldSplit = currentContent.Length > 0 &&
                (currentContent.Length + blockSize + 1 > _options.MaxChunkCharacters ||
                 (currentSection != null && block.SectionTitle != null && currentSection != block.SectionTitle) ||
                 (currentPage != null && block.PageNumber != null && currentPage != block.PageNumber));

            if (shouldSplit)
            {
                results.Add(CreateResult(chunkIndex++, currentContent.ToString().Trim(), currentSection, currentPage));
                currentContent.Clear();
                currentSection = null;
                currentPage = null;
            }

            if (currentContent.Length > 0)
                currentContent.Append("\n\n");
            currentContent.Append(block.Text);
            currentSection = block.SectionTitle ?? currentSection;
            currentPage = block.PageNumber ?? currentPage;
        }

        if (currentContent.Length > 0)
            results.Add(CreateResult(chunkIndex, currentContent.ToString().Trim(), currentSection, currentPage));

        results.RemoveAll(r => string.IsNullOrWhiteSpace(r.Content));
        return results;
    }

    private static DocumentChunkingResultItem CreateResult(
        int index, string content, string? sectionTitle, int? pageNumber)
        => new(index, content, ComputeSha256(content),
            Math.Max(1, (int)Math.Ceiling(content.Length / 4.0)), pageNumber, sectionTitle);

    private List<string> SplitLargeText(string text)
    {
        var parts = new List<string>();
        var remaining = text;
        while (remaining.Length > 0)
        {
            var take = Math.Min(_options.MaxChunkCharacters, remaining.Length);
            parts.Add(remaining[..take]);
            remaining = remaining[take..];
        }
        return parts;
    }

    // ── JSON helpers ───────────────────────────────────────────────

    private static string? TryGetString(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var val = prop.GetString();
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
        }
        return null;
    }

    private static int? TryGetInt(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val))
                return val;
        }
        return null;
    }

    private static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    private sealed record DoclingBlock(
        string Text, string? SectionTitle, int? PageNumber, string Kind);
}
