using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;
using OpenRAG.Application.Documents.ListDocuments;
using OpenRAG.Domain.Documents;

namespace OpenRAG.UnitTests.Application.Documents;

public sealed class ListDocumentsHandlerTests
{
    private static readonly Guid TenantId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Returns_documents_for_current_tenant()
    {
        var items = new[]
        {
            new DocumentListItem(Guid.NewGuid(), "a.md", "", "Ready", DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid()),
            new DocumentListItem(Guid.NewGuid(), "b.md", "", "Uploaded", DateTime.UtcNow, DateTime.UtcNow, null)
        };
        var fakes = new Fakes(new DocumentListResult(items, 1, 20, 2));
        var handler = new ListDocumentsHandler(fakes.DocRepo, fakes.ChunkRepo, fakes.EmbeddingRepo, fakes.Tenant);

        var response = (await handler.Handle(new ListDocumentsQuery(1, 20))).Value;

        Assert.Equal(2, response.TotalCount);
        Assert.Equal(2, response.Items.Count);
        Assert.Equal("a.md", response.Items[0].FileName);
    }

    [Fact]
    public async Task Rejects_page_size_above_100()
    {
        var fakes = new Fakes(new DocumentListResult(Array.Empty<DocumentListItem>(), 1, 100, 0));
        var handler = new ListDocumentsHandler(fakes.DocRepo, fakes.ChunkRepo, fakes.EmbeddingRepo, fakes.Tenant);

        var result = await handler.Handle(new ListDocumentsQuery(1, 200));

        Assert.Equal("request.page_size_invalid", result.PrimaryError.Code);
    }

    [Fact]
    public async Task Rejects_invalid_page_number()
    {
        var fakes = new Fakes(new DocumentListResult(Array.Empty<DocumentListItem>(), 1, 20, 0));
        var handler = new ListDocumentsHandler(fakes.DocRepo, fakes.ChunkRepo, fakes.EmbeddingRepo, fakes.Tenant);

        var result = await handler.Handle(new ListDocumentsQuery(-1, 20));

        Assert.Equal("request.page_number_invalid", result.PrimaryError.Code);
    }

    [Fact]
    public async Task Includes_chunk_and_embedding_counts()
    {
        var versionId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var items = new[]
        {
            new DocumentListItem(docId, "test.md", "", "Ready", DateTime.UtcNow, DateTime.UtcNow, versionId)
        };
        var fakes = new Fakes(new DocumentListResult(items, 1, 20, 1));
        var handler = new ListDocumentsHandler(fakes.DocRepo, fakes.ChunkRepo, fakes.EmbeddingRepo, fakes.Tenant);

        var response = (await handler.Handle(new ListDocumentsQuery(1, 20))).Value;

        Assert.Equal(5, response.Items[0].ChunkCount);
        Assert.Equal(3, response.Items[0].EmbeddingCount);
    }

    private sealed class Fakes
    {
        public Fakes(DocumentListResult result) => DocRepo = new FakeDocRepo(result);

        public FakeDocRepo DocRepo { get; }
        public FakeChunkRepo ChunkRepo { get; } = new();
        public FakeEmbeddingRepo EmbeddingRepo { get; } = new();
        public StubTenant Tenant => new(TenantId);
    }

    private sealed class FakeDocRepo : IDocumentRepository
    {
        private readonly DocumentListResult _result;
        public FakeDocRepo(DocumentListResult result) => _result = result;

        public Task AddAsync(Document document, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Document?> GetByIdAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult<Document?>(null);
        public Task<Document?> GetByIdWithVersionsAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult<Document?>(null);
        public Task<Document?> GetByIdForUpdateAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult<Document?>(null);
        public Task<DocumentVersion?> GetVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult<DocumentVersion?>(null);
        public Task<DocumentVersion?> GetVersionForUpdateAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult<DocumentVersion?>(null);
        public Task<bool> ExistsAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult(true);
        public Task<DocumentListResult> ListAsync(Guid tid, int pn, int ps, string? sf, string? s, CancellationToken ct = default)
            => Task.FromResult(_result);
        public Task DeleteAsync(Document doc, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeChunkRepo : IDocumentChunkRepository
    {
        public Task AddRangeAsync(IReadOnlyCollection<DocumentChunk> chunks, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentChunk>> GetByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentChunk>>(Array.Empty<DocumentChunk>());
        public Task<bool> AnyForVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(false);
        public Task<int> CountByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(5);
        public Task DeleteByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.CompletedTask;

        public Task<ChunkListResult> ListByVersionAsync(Guid tid, Guid did, Guid vid, int pn, int ps, string? s, string? st, int? pf, CancellationToken ct = default)
            => Task.FromResult(new ChunkListResult(Array.Empty<DocumentChunk>(), pn, ps, 0));

        public Task<DocumentChunk?> GetByIdForVersionAsync(Guid tid, Guid did, Guid vid, Guid cid, CancellationToken ct = default)
            => Task.FromResult<DocumentChunk?>(null);
    }

    private sealed class FakeEmbeddingRepo : IDocumentEmbeddingRepository
    {
        public Task AddRangeAsync(IReadOnlyCollection<DocumentEmbedding> embeddings, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentEmbedding>> GetByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentEmbedding>>(Array.Empty<DocumentEmbedding>());
        public Task<bool> AnyForVersionAsync(Guid tid, Guid did, Guid vid, string model, CancellationToken ct = default) => Task.FromResult(false);
        public Task<int> CountByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(3);
        public Task<DocumentEmbeddingMetadata?> GetMetadataByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<DocumentEmbeddingMetadata?>(null);
        public Task DeleteByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.CompletedTask;

        public Task<ChunkListResult> ListByVersionAsync(Guid tid, Guid did, Guid vid, int pn, int ps, string? s, string? st, int? pf, CancellationToken ct = default)
            => Task.FromResult(new ChunkListResult(Array.Empty<DocumentChunk>(), pn, ps, 0));

        public Task<DocumentChunk?> GetByIdForVersionAsync(Guid tid, Guid did, Guid vid, Guid cid, CancellationToken ct = default)
            => Task.FromResult<DocumentChunk?>(null);
    }

    private sealed class StubTenant : ICurrentTenant
    {
        public StubTenant(Guid tenantId) => TenantId = tenantId;
        public Guid TenantId { get; }
    }
}
