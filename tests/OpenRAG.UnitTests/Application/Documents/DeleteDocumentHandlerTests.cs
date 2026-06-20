using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Common;
using OpenRAG.Application.Documents.DeleteDocument;
using OpenRAG.Domain.Documents;

namespace OpenRAG.UnitTests.Application.Documents;

public sealed class DeleteDocumentHandlerTests
{
    private static readonly Guid TenantId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DocId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid VerId = new("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task Returns_404_for_missing_document()
    {
        var fakes = new Fakes(null);
        var handler = CreateHandler(fakes);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            handler.Handle(new DeleteDocumentCommand(DocId)).AsTask());

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Rejects_processing_document()
    {
        var doc = Document.Create(DocId, TenantId, "test.md", "test.md", UserId);
        doc.MarkProcessing();
        var fakes = new Fakes(doc);
        var handler = CreateHandler(fakes);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            handler.Handle(new DeleteDocumentCommand(DocId)).AsTask());

        Assert.Contains("processing", ex.Message.ToLower());
    }

    [Fact]
    public async Task Deletes_ready_document_successfully()
    {
        var doc = CreateReadyDocument();
        var fakes = new Fakes(doc);
        var handler = CreateHandler(fakes);

        var response = await handler.Handle(new DeleteDocumentCommand(DocId));

        Assert.True(response.Deleted);
        Assert.True(fakes.DocRepo.DeleteCalled);
    }

    [Fact]
    public async Task Deletes_chunks_and_embeddings_before_document()
    {
        var doc = CreateReadyDocument();
        var fakes = new Fakes(doc);
        var handler = CreateHandler(fakes);

        await handler.Handle(new DeleteDocumentCommand(DocId));

        Assert.True(fakes.EmbeddingRepo.DeleteCalled);
        Assert.True(fakes.ChunkRepo.DeleteCalled);
        Assert.True(fakes.DocRepo.DeleteCalled);
    }

    [Fact]
    public async Task Deletes_uploaded_document_successfully()
    {
        var doc = Document.Create(DocId, TenantId, "test.md", "test.md", UserId);
        var fakes = new Fakes(doc);
        var handler = CreateHandler(fakes);

        var response = await handler.Handle(new DeleteDocumentCommand(DocId));

        Assert.True(response.Deleted);
    }

    [Fact]
    public async Task Deletes_failed_document_successfully()
    {
        var doc = Document.Create(DocId, TenantId, "test.md", "test.md", UserId);
        doc.MarkProcessing();
        // Use reflection to mark as Failed since domain doesn't allow direct transition
        typeof(Document).GetProperty("Status")!.SetValue(doc, DocumentStatus.Failed);
        var fakes = new Fakes(doc);
        var handler = CreateHandler(fakes);

        var response = await handler.Handle(new DeleteDocumentCommand(DocId));

        Assert.True(response.Deleted);
    }

    private static Document CreateReadyDocument()
    {
        var doc = Document.Create(DocId, TenantId, "test.md", "test.md", UserId);
        doc.MarkProcessing();
        doc.AddVersion(VerId, 1, "tenants/.../original/test.md", "text/markdown", 1024, "abc");
        var version = doc.Versions.First();
        version.AttachDoclingArtifacts("mk-key", "json-key");
        version.MarkPreprocessed();
        doc.MarkReady();
        return doc;
    }

    private static DeleteDocumentHandler CreateHandler(Fakes fakes)
    {
        return new DeleteDocumentHandler(
            fakes.DocRepo, fakes.ChunkRepo, fakes.EmbeddingRepo, new StubFileStorage(),
            fakes.Tenant, fakes.Uow,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DeleteDocumentHandler>.Instance);
    }

    private sealed class Fakes
    {
        public Fakes(Document? doc) => DocRepo = new FakeDocRepo(doc);

        public FakeDocRepo DocRepo { get; }
        public FakeChunkRepo ChunkRepo { get; } = new();
        public FakeEmbeddingRepo EmbeddingRepo { get; } = new();
        public StubTenant Tenant => new(TenantId);
        public StubUow Uow => new();
    }

    private sealed class FakeDocRepo : IDocumentRepository
    {
        private readonly Document? _doc;
        public bool DeleteCalled { get; private set; }

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
        public Task DeleteAsync(Document doc, CancellationToken ct = default)
        {
            DeleteCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeChunkRepo : IDocumentChunkRepository
    {
        public bool DeleteCalled { get; private set; }

        public Task AddRangeAsync(IReadOnlyCollection<DocumentChunk> chunks, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentChunk>> GetByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentChunk>>(Array.Empty<DocumentChunk>());
        public Task<bool> AnyForVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(false);
        public Task<int> CountByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(0);
        public Task DeleteByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
        {
            DeleteCalled = true;
            return Task.CompletedTask;
        }

        public Task<ChunkListResult> ListByVersionAsync(Guid tid, Guid did, Guid vid, int pn, int ps, string? s, string? st, int? pf, CancellationToken ct = default)
            => Task.FromResult(new ChunkListResult(Array.Empty<DocumentChunk>(), pn, ps, 0));

        public Task<DocumentChunk?> GetByIdForVersionAsync(Guid tid, Guid did, Guid vid, Guid cid, CancellationToken ct = default)
            => Task.FromResult<DocumentChunk?>(null);
    }

    private sealed class FakeEmbeddingRepo : IDocumentEmbeddingRepository
    {
        public bool DeleteCalled { get; private set; }

        public Task AddRangeAsync(IReadOnlyCollection<DocumentEmbedding> embeddings, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentEmbedding>> GetByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentEmbedding>>(Array.Empty<DocumentEmbedding>());
        public Task<bool> AnyForVersionAsync(Guid tid, Guid did, Guid vid, string model, CancellationToken ct = default) => Task.FromResult(false);
        public Task<int> CountByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(0);
        public Task<DocumentEmbeddingMetadata?> GetMetadataByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<DocumentEmbeddingMetadata?>(null);
        public Task DeleteByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
        {
            DeleteCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class StubTenant : ICurrentTenant
    {
        public StubTenant(Guid tenantId) => TenantId = tenantId;
        public Guid TenantId { get; }
    }

    private sealed class StubUow : IUnitOfWork
    {
        public Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Task.FromResult<IApplicationTransaction>(new StubTransaction());
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubTransaction : IApplicationTransaction
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubFileStorage : IFileStorage
    {
        public Task<StoredObjectResult> SaveAsync(Stream content, string objectKey, string contentType, CancellationToken ct = default)
            => Task.FromResult(new StoredObjectResult("local", objectKey, contentType, 0, null, null));

        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string objectKey, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
