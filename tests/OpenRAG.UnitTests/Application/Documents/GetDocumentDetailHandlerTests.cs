using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;
using OpenRAG.Application.Documents.GetDocumentDetail;
using OpenRAG.Domain.Documents;

namespace OpenRAG.UnitTests.Application.Documents;

public sealed class GetDocumentDetailHandlerTests
{
    private static readonly Guid TenantId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DocId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid VerId = new("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task Returns_404_for_missing_document()
    {
        var fakes = new Fakes(null);
        var handler = new GetDocumentDetailHandler(fakes.DocRepo, fakes.ChunkRepo, fakes.EmbeddingRepo, fakes.Tenant);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            handler.Handle(new GetDocumentDetailQuery(DocId)).AsTask());

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Returns_document_without_version_when_none_exists()
    {
        var doc = Document.Create(DocId, TenantId, "test.md", "test.md", UserId);
        var fakes = new Fakes(doc);
        var handler = new GetDocumentDetailHandler(fakes.DocRepo, fakes.ChunkRepo, fakes.EmbeddingRepo, fakes.Tenant);

        var response = await handler.Handle(new GetDocumentDetailQuery(DocId));

        Assert.Equal(DocId, response.DocumentId);
        Assert.Equal("test.md", response.FileName);
        Assert.Equal("Uploaded", response.Status);
        Assert.Null(response.LatestVersion);
    }

    [Fact]
    public async Task Returns_version_with_artifact_presence_flags()
    {
        var doc = CreateDocumentWithVersion();
        var fakes = new Fakes(doc);
        var handler = new GetDocumentDetailHandler(fakes.DocRepo, fakes.ChunkRepo, fakes.EmbeddingRepo, fakes.Tenant);

        var response = await handler.Handle(new GetDocumentDetailQuery(DocId));

        Assert.NotNull(response.LatestVersion);
        Assert.True(response.LatestVersion!.HasSourceFile);
        Assert.True(response.LatestVersion.HasMarkdownArtifact);
        Assert.True(response.LatestVersion.HasJsonArtifact);
    }

    [Fact]
    public async Task Returns_chunk_and_embedding_counts()
    {
        var doc = CreateDocumentWithVersion();
        var fakes = new Fakes(doc);
        var handler = new GetDocumentDetailHandler(fakes.DocRepo, fakes.ChunkRepo, fakes.EmbeddingRepo, fakes.Tenant);

        var response = await handler.Handle(new GetDocumentDetailQuery(DocId));

        Assert.NotNull(response.LatestVersion);
        Assert.Equal(5, response.LatestVersion!.ChunkCount);
        Assert.Equal(3, response.LatestVersion.EmbeddingCount);
    }

    private static Document CreateDocumentWithVersion()
    {
        var doc = Document.Create(DocId, TenantId, "test.md", "test.md", UserId);
        doc.AddVersion(VerId, 1, "tenants/.../original/test.md", "text/markdown", 1024, "abc");
        var version = doc.Versions.First();
        version.AttachDoclingArtifacts("markdown-key", "json-key");
        version.MarkPreprocessed();
        return doc;
    }

    private sealed class Fakes
    {
        public Fakes(Document? doc) => DocRepo = new FakeDocRepo(doc);

        public FakeDocRepo DocRepo { get; }
        public FakeChunkRepo ChunkRepo { get; } = new();
        public FakeEmbeddingRepo EmbeddingRepo { get; } = new();
        public StubTenant Tenant => new(TenantId);
    }

    private sealed class FakeDocRepo : IDocumentRepository
    {
        private readonly Document? _doc;
        public FakeDocRepo(Document? doc) => _doc = doc;

        public Task AddAsync(Document document, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Document?> GetByIdAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult(_doc);
        public Task<Document?> GetByIdWithVersionsAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult(_doc);
        public Task<Document?> GetByIdForUpdateAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult(_doc);
        public Task<DocumentVersion?> GetVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult<DocumentVersion?>(null);
        public Task<DocumentVersion?> GetVersionForUpdateAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult<DocumentVersion?>(null);
        public Task<bool> ExistsAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult(_doc is not null);
        public Task<DocumentListResult> ListAsync(Guid tid, int pn, int ps, string? sf, string? s, CancellationToken ct = default)
            => Task.FromResult(new DocumentListResult(Array.Empty<DocumentListItem>(), pn, ps, 0));
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
    }

    private sealed class FakeEmbeddingRepo : IDocumentEmbeddingRepository
    {
        public Task AddRangeAsync(IReadOnlyCollection<DocumentEmbedding> embeddings, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentEmbedding>> GetByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentEmbedding>>(Array.Empty<DocumentEmbedding>());
        public Task<bool> AnyForVersionAsync(Guid tid, Guid did, Guid vid, string model, CancellationToken ct = default) => Task.FromResult(false);
        public Task<int> CountByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(3);
        public Task<DocumentEmbeddingMetadata?> GetMetadataByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<DocumentEmbeddingMetadata?>(new DocumentEmbeddingMetadata("Mock", "mock", 8, "v1", 3));
        public Task DeleteByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubTenant : ICurrentTenant
    {
        public StubTenant(Guid tenantId) => TenantId = tenantId;
        public Guid TenantId { get; }
    }
}
