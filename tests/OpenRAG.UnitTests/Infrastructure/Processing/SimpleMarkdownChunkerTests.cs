using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.Processing;
using OpenRAG.Infrastructure.Processing;

namespace OpenRAG.UnitTests.Infrastructure.Processing;

public sealed class SimpleMarkdownChunkerTests
{
    private static SimpleMarkdownChunker CreateChunker(int maxChars = 2000, int overlap = 200)
    {
        var options = Options.Create(new ChunkingOptions
        {
            MaxChunkCharacters = maxChars,
            OverlapCharacters = overlap
        });
        return new SimpleMarkdownChunker(options);
    }

    [Fact]
    public async Task Does_not_emit_empty_chunks()
    {
        var chunker = CreateChunker();
        var request = new DocumentChunkingRequest(
            TenantId: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            VersionId: Guid.NewGuid(),
            Markdown: "# Title\n\nSome content here.\n\n\n\nMore content.",
            DoclingJson: null, CorrelationId: "corr");

        var results = await chunker.ChunkAsync(request);

        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.Content)));
    }

    [Fact]
    public async Task Chunks_long_markdown_into_multiple_chunks()
    {
        var chunker = CreateChunker(maxChars: 50, overlap: 0);
        var longContent = string.Join("\n\n", Enumerable.Range(0, 20).Select(i =>
            $"Paragraph {i}: This is a sentence that is reasonably long to fill space in the paragraph."));

        var request = new DocumentChunkingRequest(
            TenantId: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            VersionId: Guid.NewGuid(),
            Markdown: longContent,
            DoclingJson: null, CorrelationId: "corr");

        var results = await chunker.ChunkAsync(request);

        Assert.True(results.Count > 1, $"Expected multiple chunks but got {results.Count}");
    }

    [Fact]
    public async Task Computes_content_hash()
    {
        var chunker = CreateChunker();
        var request = new DocumentChunkingRequest(
            TenantId: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            VersionId: Guid.NewGuid(),
            Markdown: "# Test\n\nSome content.",
            DoclingJson: null, CorrelationId: "corr");

        var results = await chunker.ChunkAsync(request);

        Assert.NotEmpty(results);
        Assert.All(results, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.ContentHash));
            Assert.Equal(64, r.ContentHash.Length); // SHA-256 hex is 64 chars
        });
    }

    [Fact]
    public async Task Sets_positive_token_count()
    {
        var chunker = CreateChunker();
        var request = new DocumentChunkingRequest(
            TenantId: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            VersionId: Guid.NewGuid(),
            Markdown: "# Test\n\nSome content here.",
            DoclingJson: null, CorrelationId: "corr");

        var results = await chunker.ChunkAsync(request);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.True(r.TokenCount > 0, $"TokenCount {r.TokenCount} should be > 0"));
    }

    [Fact]
    public async Task Single_short_markdown_produces_one_chunk()
    {
        var chunker = CreateChunker();
        var request = new DocumentChunkingRequest(
            TenantId: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            VersionId: Guid.NewGuid(),
            Markdown: "# Hello\n\nWorld.",
            DoclingJson: null, CorrelationId: "corr");

        var results = await chunker.ChunkAsync(request);

        Assert.Single(results);
    }

    [Fact]
    public async Task Empty_markdown_produces_no_chunks()
    {
        var chunker = CreateChunker();
        var request = new DocumentChunkingRequest(
            TenantId: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            VersionId: Guid.NewGuid(),
            Markdown: "",
            DoclingJson: null, CorrelationId: "corr");

        var results = await chunker.ChunkAsync(request);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Chunk_indices_are_sequential()
    {
        var chunker = CreateChunker(maxChars: 50, overlap: 0);
        var longContent = string.Join("\n\n", Enumerable.Range(0, 10).Select(i =>
            $"Paragraph {i}: This is a sentence that fills space for chunking purposes."));

        var request = new DocumentChunkingRequest(
            TenantId: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            VersionId: Guid.NewGuid(),
            Markdown: longContent,
            DoclingJson: null, CorrelationId: "corr");

        var results = await chunker.ChunkAsync(request);

        for (var i = 0; i < results.Count; i++)
        {
            Assert.Equal(i, results[i].ChunkIndex);
        }
    }
}

