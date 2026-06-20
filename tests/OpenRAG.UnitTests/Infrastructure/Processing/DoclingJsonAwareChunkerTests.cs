using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.Processing;
using OpenRAG.Infrastructure.Processing;

namespace OpenRAG.UnitTests.Infrastructure.Processing;

public sealed class DoclingJsonAwareChunkerTests
{
    private static DoclingJsonAwareChunker CreateChunker()
    {
        var options = Options.Create(new ChunkingOptions
        {
            Provider = "DoclingJson",
            MaxChunkCharacters = 2000,
            OverlapCharacters = 200,
            UseDoclingJsonWhenAvailable = true
        });
        var fallback = new SimpleMarkdownChunker(options);
        return new DoclingJsonAwareChunker(options, fallback);
    }

    [Fact]
    public async Task Falls_back_to_markdown_when_json_is_null()
    {
        var chunker = CreateChunker();
        var request = new DocumentChunkingRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "# Title\n\nContent here.", null, "corr");

        var results = await chunker.ChunkAsync(request);

        Assert.NotEmpty(results);
        Assert.Contains("Content here", results[0].Content);
    }

    [Fact]
    public async Task Falls_back_to_markdown_when_json_is_invalid()
    {
        var chunker = CreateChunker();
        var request = new DocumentChunkingRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "# Title\n\nContent.", "not valid json!!!", "corr");

        var results = await chunker.ChunkAsync(request);

        Assert.NotEmpty(results);
        Assert.Contains("Content", results[0].Content);
    }

    [Fact]
    public async Task Extracts_blocks_from_root_texts_array()
    {
        var json = JsonSerializer.Serialize(new
        {
            texts = new object[]
            {
                new { text = "First paragraph.", section_title = "Intro" },
                new { text = "Second paragraph.", section_title = (string?)null }
            }
        });
        var chunker = CreateChunker();
        var request = DocRequest(json);

        var results = await chunker.ChunkAsync(request);

        Assert.NotEmpty(results);
        Assert.Contains("First paragraph", results[0].Content);
    }

    [Fact]
    public async Task Extracts_blocks_from_nested_children()
    {
        var json = JsonSerializer.Serialize(new
        {
            body = new
            {
                children = new object[]
                {
                    new { text = "Child 1 content.", heading = "Section A" },
                    new { text = "Child 2 content." }
                }
            }
        });
        var chunker = CreateChunker();
        var request = DocRequest(json);

        var results = await chunker.ChunkAsync(request);

        Assert.NotEmpty(results);
        Assert.Contains("Child 1", results[0].Content);
    }

    [Fact]
    public async Task Sets_section_title_from_heading()
    {
        var json = JsonSerializer.Serialize(new
        {
            texts = new[]
            {
                new { text = "Content.", heading = "Introduction" }
            }
        });
        var chunker = CreateChunker();
        var request = DocRequest(json);

        var results = await chunker.ChunkAsync(request);

        Assert.NotEmpty(results);
        Assert.Equal("Introduction", results[0].SectionTitle);
    }

    [Fact]
    public async Task Sets_page_number()
    {
        var json = JsonSerializer.Serialize(new
        {
            texts = new[]
            {
                new { text = "Page 1 content.", page_no = 1 },
                new { text = "Page 2 content.", page_no = 2 }
            }
        });
        var chunker = CreateChunker();
        var request = DocRequest(json);

        var results = await chunker.ChunkAsync(request);

        Assert.True(results.Count >= 2);
        Assert.Equal(1, results[0].PageNumber);
        Assert.Equal(2, results[^1].PageNumber);
    }

    [Fact]
    public async Task Does_not_emit_empty_chunks()
    {
        var json = JsonSerializer.Serialize(new
        {
            texts = new[]
            {
                new { text = "Real content." },
                new { text = "" },
                new { text = "   " }
            }
        });
        var chunker = CreateChunker();
        var request = DocRequest(json);

        var results = await chunker.ChunkAsync(request);

        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.Content)));
    }

    [Fact]
    public async Task Splits_large_blocks()
    {
        var bigText = new string('A', 2500);
        var json = JsonSerializer.Serialize(new { texts = new[] { new { text = bigText } } });
        var chunker = CreateChunker();
        var request = DocRequest(json);

        var results = await chunker.ChunkAsync(request);

        Assert.True(results.Count >= 2, $"Expected >=2 chunks but got {results.Count}");
    }

    [Fact]
    public async Task Preserves_block_order()
    {
        var json = JsonSerializer.Serialize(new
        {
            texts = new[]
            {
                new { text = "AAAA" },
                new { text = "BBBB" },
                new { text = "CCCC" }
            }
        });
        var chunker = CreateChunker();
        var request = DocRequest(json);

        var results = await chunker.ChunkAsync(request);

        Assert.Contains("AAAA", results[0].Content);
    }

    [Fact]
    public async Task Computes_content_hash()
    {
        var json = JsonSerializer.Serialize(new { texts = new[] { new { text = "Hash me." } } });
        var chunker = CreateChunker();
        var request = DocRequest(json);

        var results = await chunker.ChunkAsync(request);

        Assert.NotEmpty(results);
        Assert.Equal(64, results[0].ContentHash.Length);
    }

    [Fact]
    public async Task Sets_positive_token_count()
    {
        var json = JsonSerializer.Serialize(new { texts = new[] { new { text = "Token test." } } });
        var chunker = CreateChunker();
        var request = DocRequest(json);

        var results = await chunker.ChunkAsync(request);

        Assert.NotEmpty(results);
        Assert.True(results[0].TokenCount > 0);
    }

    [Fact]
    public async Task Falls_back_when_json_has_no_extractable_blocks()
    {
        var json = JsonSerializer.Serialize(new { metadata = new { author = "test" } });
        var chunker = CreateChunker();
        var request = DocRequest(json);

        var results = await chunker.ChunkAsync(request);

        Assert.NotEmpty(results); // Falls back to markdown
    }

    [Fact]
    public async Task Splits_on_section_title_change()
    {
        var json = JsonSerializer.Serialize(new
        {
            texts = new[]
            {
                new { text = "Section A content.", section_title = "A" },
                new { text = "Section B content.", section_title = "B" }
            }
        });
        var chunker = CreateChunker();
        var request = DocRequest(json);

        var results = await chunker.ChunkAsync(request);

        Assert.True(results.Count >= 2);
        Assert.Equal("A", results[0].SectionTitle);
        Assert.Equal("B", results[^1].SectionTitle);
    }

    // ══ Helpers ═══════════════════════════════════════════════════

    private static DocumentChunkingRequest DocRequest(string json)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "# Markdown fallback", json, "corr");
}
