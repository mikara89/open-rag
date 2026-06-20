using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.Processing;

namespace OpenRAG.Infrastructure.Processing;

/// <summary>
/// Simple deterministic Markdown chunker for MVP.
/// Splits Markdown into paragraphs, groups them until approximate max characters
/// is reached, with optional overlap.
/// </summary>
public sealed class SimpleMarkdownChunker : IDocumentChunker
{
    private readonly ChunkingOptions _options;

    public SimpleMarkdownChunker(IOptions<ChunkingOptions> options)
    {
        _options = options.Value;
    }

    public Task<IReadOnlyList<DocumentChunkingResultItem>> ChunkAsync(
        DocumentChunkingRequest request,
        CancellationToken cancellationToken = default)
    {
        var paragraphs = SplitIntoParagraphs(request.Markdown);
        var chunks = GroupParagraphsIntoChunks(paragraphs);

        var results = new List<DocumentChunkingResultItem>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            var content = chunks[i];
            var hash = ComputeSha256(content);
            var tokenCount = Math.Max(1, (int)Math.Ceiling(content.Length / 4.0));

            results.Add(new DocumentChunkingResultItem(
                ChunkIndex: i,
                Content: content,
                ContentHash: hash,
                TokenCount: tokenCount,
                PageNumber: null,
                SectionTitle: null));
        }

        return Task.FromResult<IReadOnlyList<DocumentChunkingResultItem>>(results);
    }

    private List<string> SplitIntoParagraphs(string markdown)
    {
        // Split on double newlines to get paragraphs
        var paragraphs = new List<string>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var current = new StringBuilder();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (current.Length > 0)
                {
                    paragraphs.Add(current.ToString().Trim());
                    current.Clear();
                }
            }
            else
            {
                if (current.Length > 0)
                    current.Append('\n');
                current.Append(line);
            }
        }

        if (current.Length > 0)
        {
            paragraphs.Add(current.ToString().Trim());
        }

        return paragraphs;
    }

    private List<string> GroupParagraphsIntoChunks(List<string> paragraphs)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();
        var lastParagraph = string.Empty;

        foreach (var paragraph in paragraphs)
        {
            // If adding this paragraph would exceed max and we already have content
            if (current.Length > 0 &&
                current.Length + paragraph.Length + 1 > _options.MaxChunkCharacters)
            {
                chunks.Add(current.ToString().Trim());

                // Start new chunk with overlap from last paragraph
                current.Clear();
                if (_options.OverlapCharacters > 0 && lastParagraph.Length > 0)
                {
                    var overlapText = TruncateToCharacters(lastParagraph, _options.OverlapCharacters);
                    current.Append(overlapText);
                }
            }

            if (current.Length > 0)
                current.Append("\n\n");
            current.Append(paragraph);

            lastParagraph = paragraph;
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString().Trim());
        }

        // Never emit empty chunks
        chunks.RemoveAll(string.IsNullOrWhiteSpace);

        return chunks;
    }

    private static string TruncateToCharacters(string text, int maxChars)
    {
        if (text.Length <= maxChars)
            return text;

        // Try to break at a word boundary
        var truncated = text[..maxChars];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > maxChars / 2)
            return truncated[..lastSpace];

        return truncated;
    }

    private static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}

public sealed class ChunkingOptions
{
    public const string SectionName = "Chunking";

    public string Provider { get; set; } = "SimpleMarkdown";
    public int MaxChunkCharacters { get; set; } = 2000;
    public int OverlapCharacters { get; set; } = 200;
    public bool UseDoclingJsonWhenAvailable { get; set; } = true;
}
