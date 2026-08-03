using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;
using OpenRAG.Application.Documents.GetDocumentChunk;
using OpenRAG.Domain.Documents;

namespace OpenRAG.UnitTests.Application.Documents;

public sealed class GetDocumentChunkHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DocId = Guid.NewGuid();
    private static readonly Guid VerId = Guid.NewGuid();
    private static readonly Guid ChunkId = Guid.NewGuid();

    [Fact]
    public async Task Returns_chunk_with_embedding_metadata()
    {
        var chunk = DocumentChunk.Create(ChunkId, TenantId, DocId, VerId, 0, "content", "hash", 10);
        var fakes = new Fakes(chunk, hasEmbedding: true);
        var handler = new GetDocumentChunkHandler(fakes.ChunkRepo, fakes.EmbeddingRepo, fakes.DocRepo, fakes.Tenant);

        var response = (await handler.Handle(new GetDocumentChunkQuery(DocId, VerId, ChunkId))).Value;

        Assert.Equal(ChunkId, response.ChunkId);
        Assert.Equal("content", response.Content);
        Assert.True(response.HasEmbedding);
        Assert.Equal("Mock", response.EmbeddingProvider);
    }

    [Fact]
    public async Task Returns_chunk_without_embedding()
    {
        var chunk = DocumentChunk.Create(ChunkId, TenantId, DocId, VerId, 0, "content", "hash", 10);
        var fakes = new Fakes(chunk, hasEmbedding: false);
        var handler = new GetDocumentChunkHandler(fakes.ChunkRepo, fakes.EmbeddingRepo, fakes.DocRepo, fakes.Tenant);

        var response = (await handler.Handle(new GetDocumentChunkQuery(DocId, VerId, ChunkId))).Value;

        Assert.False(response.HasEmbedding);
        Assert.Null(response.EmbeddingProvider);
    }

    [Fact]
    public async Task Returns_404_when_chunk_not_found()
    {
        var fakes = new Fakes(null, false);
        var handler = new GetDocumentChunkHandler(fakes.ChunkRepo, fakes.EmbeddingRepo, fakes.DocRepo, fakes.Tenant);

        var result = await handler.Handle(new GetDocumentChunkQuery(DocId, VerId, ChunkId));

        Assert.True(result.IsFailure);
        Assert.Equal("resource.not_found", result.PrimaryError.Code);
        Assert.False(fakes.EmbeddingRepo.WasRead);
    }

    private sealed class Fakes
    {
        public Fakes(DocumentChunk? chunk, bool hasEmbedding)
        {
            ChunkRepo = new FakeChunkRepo(chunk);
            EmbeddingRepo = new FakeEmbeddingRepo(hasEmbedding);
            DocRepo = new FakeDocRepo(CreateVersion());
        }
        public FakeChunkRepo ChunkRepo { get; }
        public FakeEmbeddingRepo EmbeddingRepo { get; }
        public FakeDocRepo DocRepo { get; }
        public StubTenant Tenant => new(TenantId);
    }

    private static DocumentVersion CreateVersion()
    {
        return DocumentVersion.Create(VerId, TenantId, DocId, 1, "orig", "text/md", 100, "abc");
    }

    private sealed class FakeChunkRepo : IDocumentChunkRepository
    {
        private readonly DocumentChunk? _chunk;
        public FakeChunkRepo(DocumentChunk? chunk) => _chunk = chunk;

        public Task AddRangeAsync(IReadOnlyCollection<DocumentChunk> c, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentChunk>> GetByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentChunk>>(_chunk is not null ? new[] { _chunk } : Array.Empty<DocumentChunk>());
        public Task<bool> AnyForVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(_chunk is not null);
        public Task<int> CountByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(_chunk is not null ? 1 : 0);
        public Task DeleteByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ChunkListResult> ListByVersionAsync(Guid tid, Guid did, Guid vid, int pn, int ps, string? s, string? st, int? pf, CancellationToken ct = default)
            => Task.FromResult(new ChunkListResult(Array.Empty<DocumentChunk>(), pn, ps, 0));
        public Task<DocumentChunk?> GetByIdForVersionAsync(Guid tid, Guid did, Guid vid, Guid cid, CancellationToken ct = default)
            => Task.FromResult(_chunk);
    }

    private sealed class FakeEmbeddingRepo : IDocumentEmbeddingRepository
    {
        private readonly bool _hasEmbedding;
        public FakeEmbeddingRepo(bool hasEmbedding) => _hasEmbedding = hasEmbedding;

        public bool WasRead { get; private set; }

        public Task AddRangeAsync(IReadOnlyCollection<DocumentEmbedding> e, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentEmbedding>> GetByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentEmbedding>>(Array.Empty<DocumentEmbedding>());
        public Task<bool> AnyForVersionAsync(Guid tid, Guid did, Guid vid, string model, CancellationToken ct = default) => Task.FromResult(_hasEmbedding);
        public Task<int> CountByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(_hasEmbedding ? 1 : 0);
        public Task<DocumentEmbeddingMetadata?> GetMetadataByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
        {
            WasRead = true;
            return Task.FromResult<DocumentEmbeddingMetadata?>(
                _hasEmbedding ? new DocumentEmbeddingMetadata("Mock", "mock", 8, "v1", 1) : null);
        }
        public Task DeleteByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.CompletedTask;
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
