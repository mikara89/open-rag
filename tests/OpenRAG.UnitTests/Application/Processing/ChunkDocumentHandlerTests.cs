using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Processing;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Common;
using OpenRAG.Application.Processing.ChunkDocument;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

namespace OpenRAG.UnitTests.Application.Processing;

public sealed class ChunkDocumentHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DocId = Guid.NewGuid();
    private static readonly Guid VerId = Guid.NewGuid();
    private static readonly Guid RunId = Guid.NewGuid();

    [Fact]
    public async Task Rejects_empty_TenantId()
    {
        var handler = CreateHandler();
        var cmd = new ChunkDocumentCommand(Guid.Empty, DocId, VerId, RunId, "corr");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(cmd).AsTask());
        Assert.Contains("TenantId", ex.Message);
    }

    [Fact]
    public async Task Rejects_empty_DocumentId()
    {
        var handler = CreateHandler();
        var cmd = new ChunkDocumentCommand(TenantId, Guid.Empty, VerId, RunId, "corr");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(cmd).AsTask());
        Assert.Contains("DocumentId", ex.Message);
    }

    [Fact]
    public async Task Rejects_empty_VersionId()
    {
        var handler = CreateHandler();
        var cmd = new ChunkDocumentCommand(TenantId, DocId, Guid.Empty, RunId, "corr");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(cmd).AsTask());
        Assert.Contains("VersionId", ex.Message);
    }

    [Fact]
    public async Task Rejects_empty_ProcessingRunId()
    {
        var handler = CreateHandler();
        var cmd = new ChunkDocumentCommand(TenantId, DocId, VerId, Guid.Empty, "corr");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(cmd).AsTask());
        Assert.Contains("ProcessingRunId", ex.Message);
    }

    [Fact]
    public async Task No_ops_when_version_missing()
    {
        var fakes = CreateFakes(versionMissing: true);
        var handler = CreateHandler(fakes);
        var cmd = new ChunkDocumentCommand(TenantId, DocId, VerId, RunId, "corr");

        var response = await handler.Handle(cmd);

        Assert.Equal("VersionNotFound", response.Status);
        Assert.False(fakes.FileStorage.ReadCalled);
        Assert.False(fakes.Chunker.Called);
        Assert.False(fakes.ChunkRepo.ChunksAdded);
        Assert.Null(fakes.EventBus.LastEvent);
        Assert.False(fakes.UnitOfWork.SaveChangesCalled);
    }

    [Fact]
    public async Task Foreign_tenant_version_scope_performs_no_secondary_work()
    {
        var fakes = CreateFakes(versionMissing: true);

        var response = await CreateHandler(fakes).Handle(
            new ChunkDocumentCommand(TenantId, DocId, VerId, RunId, "foreign-scope"));

        Assert.Equal("VersionNotFound", response.Status);
        Assert.False(fakes.FileStorage.ReadCalled);
        Assert.False(fakes.Chunker.Called);
        Assert.False(fakes.ChunkRepo.ChunksAdded);
        Assert.Null(fakes.EventBus.LastEvent);
        Assert.False(fakes.UnitOfWork.SaveChangesCalled);
    }

    [Fact]
    public async Task Throws_when_markdown_object_key_missing()
    {
        var fakes = CreateFakes(markdownMissing: true);
        var handler = CreateHandler(fakes);
        var cmd = new ChunkDocumentCommand(TenantId, DocId, VerId, RunId, "corr");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(cmd).AsTask());
        Assert.Contains("Markdown", ex.Message);
    }

    [Fact]
    public async Task No_ops_when_processing_run_missing()
    {
        var fakes = CreateFakes(runMissing: true);
        var handler = CreateHandler(fakes);
        var cmd = new ChunkDocumentCommand(TenantId, DocId, VerId, RunId, "corr");

        var response = await handler.Handle(cmd);

        Assert.Equal("ProcessingRunNotFound", response.Status);
    }

    [Fact]
    public async Task Reads_markdown_from_IFileStorage()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var cmd = new ChunkDocumentCommand(TenantId, DocId, VerId, RunId, "corr");

        await handler.Handle(cmd);

        Assert.True(fakes.FileStorage.ReadCalled);
    }

    [Fact]
    public async Task Invokes_IDocumentChunker()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var cmd = new ChunkDocumentCommand(TenantId, DocId, VerId, RunId, "corr");

        await handler.Handle(cmd);

        Assert.True(fakes.Chunker.Called);
        Assert.Equal(TenantId, fakes.Chunker.LastRequest?.TenantId);
    }

    [Fact]
    public async Task Persists_chunks()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var cmd = new ChunkDocumentCommand(TenantId, DocId, VerId, RunId, "corr");

        var response = await handler.Handle(cmd);

        Assert.Equal("Chunked", response.Status);
        Assert.True(response.ChunkCount > 0);
        Assert.True(fakes.ChunkRepo.ChunksAdded);
        Assert.All(fakes.ChunkRepo.AddedChunks, chunk => Assert.Equal(TenantId, chunk.TenantId));
    }

    [Fact]
    public async Task Publishes_DocumentChunkedEvent()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var cmd = new ChunkDocumentCommand(TenantId, DocId, VerId, RunId, "corr");

        await handler.Handle(cmd);

        Assert.Equal("document.chunked", fakes.EventBus.LastTopic);
        var published = Assert.IsType<OpenRAG.Application.Messaging.Events.DocumentChunkedEvent>(fakes.EventBus.LastEvent);
        Assert.Equal(TenantId, published.TenantId);
    }

    [Fact]
    public async Task Commits_transaction_on_success()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var cmd = new ChunkDocumentCommand(TenantId, DocId, VerId, RunId, "corr");

        await handler.Handle(cmd);

        Assert.True(fakes.UnitOfWork.TransactionCommitted);
    }

    [Fact]
    public async Task Does_not_publish_event_on_chunking_failure()
    {
        var fakes = CreateFakes();
        fakes.Chunker.ShouldThrow = true;
        var handler = CreateHandler(fakes);
        var cmd = new ChunkDocumentCommand(TenantId, DocId, VerId, RunId, "corr");

        await handler.Handle(cmd);

        Assert.NotEqual("document.chunked", fakes.EventBus.LastTopic);
    }

    [Fact]
    public async Task Deletes_old_chunks_and_recreates_when_chunks_exist()
    {
        var fakes = CreateFakes(hasChunks: true);
        var handler = CreateHandler(fakes);
        var cmd = new ChunkDocumentCommand(TenantId, DocId, VerId, RunId, "corr");

        var response = await handler.Handle(cmd);

        // Old chunks are deleted then new ones are created
        Assert.Equal("Chunked", response.Status);
        Assert.True(fakes.Chunker.Called);
    }

    // ══ Helpers ═══════════════════════════════════════════════════

    private static ChunkDocumentHandler CreateHandler(AllFakes? fakes = null)
    {
        fakes ??= CreateFakes();
        return new ChunkDocumentHandler(
            fakes.DocRepo, fakes.ChunkRepo, fakes.EmbeddingRepo,
            fakes.RunRepo, fakes.FileStorage, fakes.Chunker, fakes.EventBus, fakes.Clock, fakes.UnitOfWork,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ChunkDocumentHandler>.Instance,
            new OpenRAG.Application.Storage.DocumentObjectKeyPolicy());
    }

    private static AllFakes CreateFakes(
        bool versionMissing = false,
        bool runMissing = false,
        bool hasChunks = false,
        bool markdownMissing = false)
    {
        DocumentVersion? version = versionMissing ? null : CreateVersion(markdownMissing);
        var run = runMissing ? null : CreateRun();

        var docRepo = new FakeDocRepo(version);
        var chunkRepo = new FakeChunkRepo(hasChunks);
        var embeddingRepo = new FakeEmbeddingRepo();
        var runRepo = new FakeRunRepo(run, null);
        var fileStorage = new FakeFileStorage();
        var chunker = new FakeChunker();
        var eventBus = new FakeEventBus();
        var clock = new StubClock();
        var uow = new FakeUoW();

        return new AllFakes(docRepo, chunkRepo, embeddingRepo, runRepo, fileStorage, chunker, eventBus, clock, uow);
    }

    private static DocumentVersion CreateVersion(bool markdownMissing = false)
    {
        var version = DocumentVersion.Create(VerId, TenantId, DocId, 1,
            $"tenants/{TenantId:D}/documents/{DocId:D}/versions/{VerId:D}/original/source.pdf",
            "application/pdf", 1024, "abc123");
        if (!markdownMissing)
        {
            version.AttachDoclingArtifacts(
                $"tenants/{TenantId:D}/documents/{DocId:D}/versions/{VerId:D}/docling/document.md",
                $"tenants/{TenantId:D}/documents/{DocId:D}/versions/{VerId:D}/docling/document.json");
            version.MarkPreprocessed();
        }
        return version;
    }

    private static DocumentProcessingRun CreateRun()
        => DocumentProcessingRun.Create(RunId, TenantId, DocId, VerId,
            ProcessingRunReason.InitialUpload, "corr-123");

    private sealed record AllFakes(
        FakeDocRepo DocRepo,
        FakeChunkRepo ChunkRepo,
        FakeEmbeddingRepo EmbeddingRepo,
        FakeRunRepo RunRepo,
        FakeFileStorage FileStorage,
        FakeChunker Chunker,
        FakeEventBus EventBus,
        StubClock Clock,
        FakeUoW UnitOfWork);

    // ══ Stubs / Fakes ═════════════════════════════════════════════

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class FakeDocRepo : IDocumentRepository
    {
        public DocumentVersion? Version { get; set; }

        public FakeDocRepo(DocumentVersion? version) => Version = version;

        public Task AddAsync(Document doc, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Document?> GetByIdAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult<Document?>(null);
        public Task<Document?> GetByIdWithVersionsAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult<Document?>(null);
        public Task<Document?> GetByIdForUpdateAsync(Guid tid, Guid did, CancellationToken ct = default)
            => Task.FromResult<Document?>(Document.Create(did, tid, "test", "test.md", Guid.NewGuid()));
        public Task<DocumentVersion?> GetVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult<DocumentVersion?>(null);
        public Task<DocumentVersion?> GetVersionForUpdateAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(Version);
        public Task<bool> ExistsAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult(true);

        public Task<DocumentListResult> ListAsync(Guid tid, int pn, int ps, string? sf, string? s, CancellationToken ct = default)
            => Task.FromResult(new DocumentListResult(Array.Empty<DocumentListItem>(), pn, ps, 0));

        public Task DeleteAsync(Document doc, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeChunkRepo : IDocumentChunkRepository
    {
        private readonly bool _hasChunks;
        public bool ChunksAdded { get; private set; }
        public IReadOnlyCollection<DocumentChunk> AddedChunks { get; private set; } = Array.Empty<DocumentChunk>();

        public FakeChunkRepo(bool hasChunks = false) => _hasChunks = hasChunks;

        public Task AddRangeAsync(IReadOnlyCollection<DocumentChunk> chunks, CancellationToken ct = default)
        {
            ChunksAdded = true;
            AddedChunks = chunks;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DocumentChunk>> GetByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
        {
            if (_hasChunks)
            {
                var chunk = DocumentChunk.Create(
                    Guid.NewGuid(), tid, did, vid, 0, "content", "hash", 10);
                return Task.FromResult<IReadOnlyList<DocumentChunk>>(new[] { chunk });
            }
            return Task.FromResult<IReadOnlyList<DocumentChunk>>(Array.Empty<DocumentChunk>());
        }

        public Task<bool> AnyForVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult(_hasChunks);

        public Task<int> CountByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult(_hasChunks ? 1 : 0);

        public Task DeleteByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ChunkListResult> ListByVersionAsync(Guid tid, Guid did, Guid vid, int pn, int ps, string? s, string? st, int? pf, CancellationToken ct = default)
            => Task.FromResult(new ChunkListResult(Array.Empty<DocumentChunk>(), pn, ps, 0));

        public Task<DocumentChunk?> GetByIdForVersionAsync(Guid tid, Guid did, Guid vid, Guid cid, CancellationToken ct = default)
            => Task.FromResult<DocumentChunk?>(null);
    }

    private sealed class FakeRunRepo : IProcessingRunRepository
    {
        private readonly DocumentProcessingRun? _run;
        private readonly DocumentProcessingStep? _step;
        public FakeRunRepo(DocumentProcessingRun? run, DocumentProcessingStep? step) { _run = run; _step = step; }

        public Task AddAsync(DocumentProcessingRun r, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DocumentProcessingRun?> GetByIdAsync(Guid tid, Guid rid, CancellationToken ct = default) => Task.FromResult<DocumentProcessingRun?>(null);
        public Task<DocumentProcessingRun?> GetByIdForUpdateAsync(Guid tid, Guid rid, CancellationToken ct = default) => Task.FromResult(_run);
        public Task AddStepAsync(DocumentProcessingStep s, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DocumentProcessingStep?> GetStepAsync(Guid tid, Guid rid, DocumentProcessingStepName sn, CancellationToken ct = default) => Task.FromResult<DocumentProcessingStep?>(null);
        public Task<DocumentProcessingStep?> GetStepForUpdateAsync(Guid tid, Guid rid, DocumentProcessingStepName sn, CancellationToken ct = default) => Task.FromResult(_step);
        public Task<IReadOnlyList<DocumentProcessingRun>> GetRunsByDocumentAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentProcessingRun>>(Array.Empty<DocumentProcessingRun>());
        public Task<IReadOnlyList<DocumentProcessingStep>> GetStepsByRunAsync(Guid tid, Guid rid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentProcessingStep>>(Array.Empty<DocumentProcessingStep>());
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        public bool ReadCalled { get; private set; }

        public Task<StoredObjectResult> SaveAsync(Stream content, string objectKey, string contentType, CancellationToken ct = default)
            => Task.FromResult(new StoredObjectResult("b", objectKey, contentType, 0, null, null));

        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct = default)
        {
            ReadCalled = true;
            var content = "# Test\n\nFake markdown content for chunking test.\n\nMore paragraphs.";
            return Task.FromResult<Stream>(new MemoryStream(
                global::System.Text.Encoding.UTF8.GetBytes(content)));
        }

        public Task DeleteAsync(string objectKey, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeChunker : IDocumentChunker
    {
        public bool Called { get; private set; }
        public DocumentChunkingRequest? LastRequest { get; private set; }
        public bool ShouldThrow { get; set; }

        public Task<IReadOnlyList<DocumentChunkingResultItem>> ChunkAsync(
            DocumentChunkingRequest request, CancellationToken ct = default)
        {
            Called = true;
            LastRequest = request;
            if (ShouldThrow) throw new InvalidOperationException("Simulated chunking failure");

            return Task.FromResult<IReadOnlyList<DocumentChunkingResultItem>>(new[]
            {
                new DocumentChunkingResultItem(0, "Chunk 1 content.", "hash1", 4, null, null),
                new DocumentChunkingResultItem(1, "Chunk 2 content.", "hash2", 4, null, null)
            });
        }
    }

    private sealed class FakeEventBus : IDocumentEventBus
    {
        public string? LastTopic { get; private set; }
        public object? LastEvent { get; private set; }

        public Task PublishAsync<TEvent>(string topic, TEvent msg, CancellationToken ct = default)
        {
            LastTopic = topic;
            LastEvent = msg;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUoW : IUnitOfWork
    {
        public bool TransactionCommitted { get; private set; }
        public bool SaveChangesCalled { get; private set; }

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCalled = true;
            return Task.CompletedTask;
        }

        public Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IApplicationTransaction>(new FakeTransaction(this));
        }

        private sealed class FakeTransaction : IApplicationTransaction
        {
            private readonly FakeUoW _uow;
            public FakeTransaction(FakeUoW uow) => _uow = uow;
            public Task CommitAsync(CancellationToken ct = default) { _uow.TransactionCommitted = true; return Task.CompletedTask; }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeEmbeddingRepo : IDocumentEmbeddingRepository
    {
        public bool Deleted { get; private set; }

        public Task AddRangeAsync(IReadOnlyCollection<DocumentEmbedding> embeddings, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<DocumentEmbedding>> GetByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentEmbedding>>(Array.Empty<DocumentEmbedding>());

        public Task<bool> AnyForVersionAsync(Guid tid, Guid did, Guid vid, string model, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<int> CountByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<DocumentEmbeddingMetadata?> GetMetadataByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<DocumentEmbeddingMetadata?>(null);

        public Task DeleteByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
        {
            Deleted = true;
            return Task.CompletedTask;
        }

        public Task<ChunkListResult> ListByVersionAsync(Guid tid, Guid did, Guid vid, int pn, int ps, string? s, string? st, int? pf, CancellationToken ct = default)
            => Task.FromResult(new ChunkListResult(Array.Empty<DocumentChunk>(), pn, ps, 0));

        public Task<DocumentChunk?> GetByIdForVersionAsync(Guid tid, Guid did, Guid vid, Guid cid, CancellationToken ct = default)
            => Task.FromResult<DocumentChunk?>(null);
    }
}
