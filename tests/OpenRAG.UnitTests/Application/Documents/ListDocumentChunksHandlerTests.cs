using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;
using OpenRAG.Application.Documents.ListDocumentChunks;
using OpenRAG.Domain.Documents;

namespace OpenRAG.UnitTests.Application.Documents;

public sealed class ListDocumentChunksHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DocId = Guid.NewGuid();
    private static readonly Guid VerId = Guid.NewGuid();

    [Fact]
    public async Task Returns_chunks_for_version()
    {
        var chunks = new[] { CreateChunk(0, "Content A"), CreateChunk(1, "Content B") };
        var fakes = new Fakes(chunks, chunkCount: 2);
        var handler = new ListDocumentChunksHandler(fakes.ChunkRepo, fakes.DocRepo, fakes.Tenant);

        var response = await handler.Handle(new ListDocumentChunksQuery(DocId, VerId));

        Assert.Equal(2, response.TotalCount);
        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public async Task Caps_page_size_at_100()
    {
        var fakes = new Fakes(Array.Empty<DocumentChunk>(), 0);
        var handler = new ListDocumentChunksHandler(fakes.ChunkRepo, fakes.DocRepo, fakes.Tenant);

        var response = await handler.Handle(new ListDocumentChunksQuery(DocId, VerId, 1, 200));

        Assert.True(response.PageSize <= 100);
    }

    [Fact]
    public async Task Returns_404_when_version_not_found()
    {
        var fakes = new Fakes(Array.Empty<DocumentChunk>(), 0, null, useVersion: false);
        var handler = new ListDocumentChunksHandler(fakes.ChunkRepo, fakes.DocRepo, fakes.Tenant);

        var ex = await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            handler.Handle(new ListDocumentChunksQuery(DocId, VerId)).AsTask());
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Includes_content_preview()
    {
        var longContent = new string('x', 500);
        var chunks = new[] { CreateChunk(0, longContent) };
        var fakes = new Fakes(chunks, 1);
        var handler = new ListDocumentChunksHandler(fakes.ChunkRepo, fakes.DocRepo, fakes.Tenant);

        var response = await handler.Handle(new ListDocumentChunksQuery(DocId, VerId));

        Assert.EndsWith("...", response.Items[0].ContentPreview);
        Assert.True(response.Items[0].ContentPreview.Length <= 303); // 300 + "..."
    }

    private static DocumentChunk CreateChunk(int index, string content)
    {
        return DocumentChunk.Create(Guid.NewGuid(), TenantId, DocId, VerId, index, content, "hash-" + index, 10);
    }

    private sealed class Fakes
    {
        public Fakes(IReadOnlyList<DocumentChunk> chunks, int chunkCount, DocumentVersion? version = null, bool useVersion = true)
        {
            ChunkRepo = new FakeChunkRepo(chunks, chunkCount);
            DocRepo = new FakeDocRepo(useVersion ? (version ?? CreateVersion()) : null);
        }

        public FakeChunkRepo ChunkRepo { get; }
        public FakeDocRepo DocRepo { get; }
        public StubTenant Tenant => new(TenantId);
    }

    private static DocumentVersion CreateVersion()
    {
        return DocumentVersion.Create(VerId, TenantId, DocId, 1, "orig", "text/md", 100, "abc");
    }

    private sealed class FakeChunkRepo : IDocumentChunkRepository
    {
        private readonly IReadOnlyList<DocumentChunk> _chunks;
        private readonly int _count;
        public FakeChunkRepo(IReadOnlyList<DocumentChunk> chunks, int count) { _chunks = chunks; _count = count; }

        public Task AddRangeAsync(IReadOnlyCollection<DocumentChunk> c, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentChunk>> GetByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult(_chunks);
        public Task<bool> AnyForVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(_count > 0);
        public Task<int> CountByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(_count);
        public Task DeleteByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ChunkListResult> ListByVersionAsync(Guid tid, Guid did, Guid vid, int pn, int ps, string? s, string? st, int? pf, CancellationToken ct = default)
            => Task.FromResult(new ChunkListResult(_chunks, pn, ps, _count));
        public Task<DocumentChunk?> GetByIdForVersionAsync(Guid tid, Guid did, Guid vid, Guid cid, CancellationToken ct = default)
            => Task.FromResult<DocumentChunk?>(_chunks.FirstOrDefault(c => c.Id == cid));
    }

    private sealed class FakeDocRepo : IDocumentRepository
    {
        private readonly DocumentVersion? _version;
        public FakeDocRepo(DocumentVersion? version) => _version = version;

        public Task AddAsync(Document d, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Document?> GetByIdAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult<Document?>(null);
        public Task<Document?> GetByIdWithVersionsAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult<Document?>(null);
        public Task<Document?> GetByIdForUpdateAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult<Document?>(null);
        public Task<DocumentVersion?> GetVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(_version);
        public Task<DocumentVersion?> GetVersionForUpdateAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(_version);
        public Task<bool> ExistsAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult(true);
        public Task<DocumentListResult> ListAsync(Guid tid, int pn, int ps, string? sf, string? s, CancellationToken ct = default)
            => Task.FromResult(new DocumentListResult(Array.Empty<DocumentListItem>(), pn, ps, 0));
        public Task DeleteAsync(Document d, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubTenant : ICurrentTenant
    {
        public StubTenant(Guid tenantId) => TenantId = tenantId;
        public Guid TenantId { get; }
    }
}
