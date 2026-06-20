using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Common;
using OpenRAG.Application.Processing.GenerateEmbeddings;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

namespace OpenRAG.UnitTests.Application.Processing;

public sealed class GenerateEmbeddingsHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DocId = Guid.NewGuid();
    private static readonly Guid VerId = Guid.NewGuid();
    private static readonly Guid RunId = Guid.NewGuid();

    [Fact]
    public async Task Rejects_empty_DocumentId()
    {
        var handler = CreateHandler();
        var cmd = new GenerateEmbeddingsCommand(Guid.Empty, VerId, RunId, "corr");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(cmd).AsTask());
        Assert.Contains("DocumentId", ex.Message);
    }

    [Fact]
    public async Task Rejects_empty_VersionId()
    {
        var handler = CreateHandler();
        var cmd = new GenerateEmbeddingsCommand(DocId, Guid.Empty, RunId, "corr");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(cmd).AsTask());
        Assert.Contains("VersionId", ex.Message);
    }

    [Fact]
    public async Task Rejects_empty_ProcessingRunId()
    {
        var handler = CreateHandler();
        var cmd = new GenerateEmbeddingsCommand(DocId, VerId, Guid.Empty, "corr");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(cmd).AsTask());
        Assert.Contains("ProcessingRunId", ex.Message);
    }

    [Fact]
    public async Task Throws_when_processing_run_missing()
    {
        var fakes = CreateFakes(runMissing: true);
        var handler = CreateHandler(fakes);
        var cmd = new GenerateEmbeddingsCommand(DocId, VerId, RunId, "corr");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(cmd).AsTask());
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Throws_when_no_chunks_exist()
    {
        var fakes = CreateFakes(hasChunks: false);
        var handler = CreateHandler(fakes);
        var cmd = new GenerateEmbeddingsCommand(DocId, VerId, RunId, "corr");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(cmd).AsTask());
        Assert.Contains("No chunks", ex.Message);
    }

    [Fact]
    public async Task Calls_embedding_service_once_per_chunk()
    {
        var fakes = CreateFakes();
        fakes.EmbeddingService.CallCount = 0;
        var handler = CreateHandler(fakes);
        var cmd = new GenerateEmbeddingsCommand(DocId, VerId, RunId, "corr");

        await handler.Handle(cmd);

        // We create 3 chunks in fake, so embedding should be called 3 times
        Assert.Equal(3, fakes.EmbeddingService.CallCount);
    }

    [Fact]
    public async Task Persists_embeddings()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var cmd = new GenerateEmbeddingsCommand(DocId, VerId, RunId, "corr");

        var response = await handler.Handle(cmd);

        Assert.Equal("Embedded", response.Status);
        Assert.True(response.EmbeddingCount > 0);
        Assert.True(fakes.EmbeddingRepo.EmbeddingsAdded);
    }

    [Fact]
    public async Task Publishes_DocumentEmbeddingsGeneratedEvent()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var cmd = new GenerateEmbeddingsCommand(DocId, VerId, RunId, "corr");

        await handler.Handle(cmd);

        Assert.Equal("document.embeddings.generated", fakes.EventBus.LastTopic);
        Assert.NotNull(fakes.EventBus.LastEvent);
    }

    [Fact]
    public async Task Commits_transaction_on_success()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var cmd = new GenerateEmbeddingsCommand(DocId, VerId, RunId, "corr");

        await handler.Handle(cmd);

        Assert.True(fakes.UnitOfWork.TransactionCommitted);
    }

    [Fact]
    public async Task Does_not_publish_on_embedding_failure()
    {
        var fakes = CreateFakes();
        fakes.EmbeddingService.ShouldThrow = true;
        var handler = CreateHandler(fakes);
        var cmd = new GenerateEmbeddingsCommand(DocId, VerId, RunId, "corr");

        await handler.Handle(cmd);

        Assert.NotEqual("document.embeddings.generated", fakes.EventBus.LastTopic);
    }

    [Fact]
    public async Task Persists_failed_step_on_embedding_failure()
    {
        var fakes = CreateFakes();
        fakes.EmbeddingService.ShouldThrow = true;
        var handler = CreateHandler(fakes);
        var cmd = new GenerateEmbeddingsCommand(DocId, VerId, RunId, "corr");

        var response = await handler.Handle(cmd);

        Assert.Equal("Failed", response.Status);
        Assert.True(fakes.UnitOfWork.SaveChangesCalled);
        Assert.True(fakes.UnitOfWork.TransactionCommitted);
    }

    [Fact]
    public async Task Is_idempotent_when_embeddings_already_exist()
    {
        var fakes = CreateFakes(hasEmbeddings: true);
        var handler = CreateHandler(fakes);
        var cmd = new GenerateEmbeddingsCommand(DocId, VerId, RunId, "corr");

        var response = await handler.Handle(cmd);

        Assert.Equal("AlreadyEmbedded", response.Status);
        Assert.Equal(0, fakes.EmbeddingService.CallCount); // Not re-invoked
    }

    // ══ Helpers ═══════════════════════════════════════════════════

    private static GenerateEmbeddingsHandler CreateHandler(AllFakes? fakes = null)
    {
        fakes ??= CreateFakes();
        var options = Options.Create(new GenerateEmbeddingsOptions { Model = "mock-embedding-8" });
        return new GenerateEmbeddingsHandler(
            fakes.Tenant, fakes.ChunkRepo, fakes.EmbeddingRepo, fakes.DocRepo, fakes.RunRepo,
            fakes.EmbeddingService, fakes.EventBus, fakes.Clock, fakes.UnitOfWork, options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GenerateEmbeddingsHandler>.Instance);
    }

    private static AllFakes CreateFakes(
        bool runMissing = false,
        bool hasChunks = true,
        bool hasEmbeddings = false)
    {
        var tenant = new StubTenant(TenantId);

        var chunksList = hasChunks
            ? new[]
            {
                DocumentChunk.Create(Guid.NewGuid(), TenantId, DocId, VerId, 0, "Chunk 0 content.", "h0", 4),
                DocumentChunk.Create(Guid.NewGuid(), TenantId, DocId, VerId, 1, "Chunk 1 content.", "h1", 4),
                DocumentChunk.Create(Guid.NewGuid(), TenantId, DocId, VerId, 2, "Chunk 2 content.", "h2", 4)
            }
            : Array.Empty<DocumentChunk>();

        var run = runMissing ? null : CreateRun();
        var docRepo = new FakeDocRepo();
        var chunkRepo = new FakeChunkRepo(chunksList);
        var embeddingRepo = new FakeEmbeddingRepo(hasEmbeddings);
        var runRepo = new FakeRunRepo(run);
        var embeddingService = new FakeEmbeddingService();
        var eventBus = new FakeEventBus();
        var clock = new StubClock();
        var uow = new FakeUoW();

        return new AllFakes(tenant, chunkRepo, embeddingRepo, docRepo, runRepo,
            embeddingService, eventBus, clock, uow);
    }

    private static DocumentProcessingRun CreateRun()
        => DocumentProcessingRun.Create(RunId, TenantId, DocId, VerId,
            ProcessingRunReason.InitialUpload, "corr-123");

    private sealed record AllFakes(
        StubTenant Tenant,
        FakeChunkRepo ChunkRepo,
        FakeEmbeddingRepo EmbeddingRepo,
        FakeDocRepo DocRepo,
        FakeRunRepo RunRepo,
        FakeEmbeddingService EmbeddingService,
        FakeEventBus EventBus,
        StubClock Clock,
        FakeUoW UnitOfWork);

    // ══ Stubs / Fakes ═════════════════════════════════════════════

    private sealed class StubTenant : ICurrentTenant
    {
        public StubTenant(Guid id) => TenantId = id;
        public Guid TenantId { get; }
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class FakeChunkRepo : IDocumentChunkRepository
    {
        private readonly IReadOnlyList<DocumentChunk> _chunks;
        public FakeChunkRepo(IReadOnlyList<DocumentChunk> chunks) => _chunks = chunks;

        public Task AddRangeAsync(IReadOnlyCollection<DocumentChunk> chunks, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentChunk>> GetByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult(_chunks);
        public Task<bool> AnyForVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult(_chunks.Count > 0);
        public Task<int> CountByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult(_chunks.Count);

        public Task DeleteByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ChunkListResult> ListByVersionAsync(Guid tid, Guid did, Guid vid, int pn, int ps, string? s, string? st, int? pf, CancellationToken ct = default)
            => Task.FromResult(new ChunkListResult(Array.Empty<DocumentChunk>(), pn, ps, 0));

        public Task<DocumentChunk?> GetByIdForVersionAsync(Guid tid, Guid did, Guid vid, Guid cid, CancellationToken ct = default)
            => Task.FromResult<DocumentChunk?>(null);
    }

    private sealed class FakeDocRepo : IDocumentRepository
    {
        public Task AddAsync(Document doc, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Document?> GetByIdAsync(Guid tid, Guid did, CancellationToken ct = default)
            => Task.FromResult<Document?>(null);
        public Task<Document?> GetByIdWithVersionsAsync(Guid tid, Guid did, CancellationToken ct = default)
            => Task.FromResult<Document?>(null);
        public Task<Document?> GetByIdForUpdateAsync(Guid tid, Guid did, CancellationToken ct = default)
        {
            var doc = Document.Create(did, tid, "test", "test.md", Guid.NewGuid());
            doc.MarkProcessing(); // Must be Processing for MarkReady to succeed
            return Task.FromResult<Document?>(doc);
        }
        public Task<DocumentVersion?> GetVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<DocumentVersion?>(null);
        public Task<DocumentVersion?> GetVersionForUpdateAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<DocumentVersion?>(null);
        public Task<bool> ExistsAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult(true);

        public Task<DocumentListResult> ListAsync(Guid tid, int pn, int ps, string? sf, string? s, CancellationToken ct = default)
            => Task.FromResult(new DocumentListResult(Array.Empty<DocumentListItem>(), pn, ps, 0));

        public Task DeleteAsync(Document doc, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeEmbeddingRepo : IDocumentEmbeddingRepository
    {
        private readonly bool _hasEmbeddings;
        public bool EmbeddingsAdded { get; private set; }

        public FakeEmbeddingRepo(bool hasEmbeddings = false) => _hasEmbeddings = hasEmbeddings;

        public Task AddRangeAsync(IReadOnlyCollection<DocumentEmbedding> embeddings, CancellationToken ct = default)
        {
            EmbeddingsAdded = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DocumentEmbedding>> GetByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentEmbedding>>(Array.Empty<DocumentEmbedding>());

        public Task<bool> AnyForVersionAsync(Guid tid, Guid did, Guid vid, string model, CancellationToken ct = default)
            => Task.FromResult(_hasEmbeddings);

        public Task<int> CountByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<DocumentEmbeddingMetadata?> GetMetadataByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<DocumentEmbeddingMetadata?>(null);

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
        public FakeRunRepo(DocumentProcessingRun? run) => _run = run;

        public Task AddAsync(DocumentProcessingRun r, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DocumentProcessingRun?> GetByIdAsync(Guid tid, Guid rid, CancellationToken ct = default) => Task.FromResult<DocumentProcessingRun?>(null);
        public Task<DocumentProcessingRun?> GetByIdForUpdateAsync(Guid tid, Guid rid, CancellationToken ct = default) => Task.FromResult(_run);
        public Task AddStepAsync(DocumentProcessingStep s, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DocumentProcessingStep?> GetStepAsync(Guid tid, Guid rid, DocumentProcessingStepName sn, CancellationToken ct = default) => Task.FromResult<DocumentProcessingStep?>(null);
        public Task<DocumentProcessingStep?> GetStepForUpdateAsync(Guid tid, Guid rid, DocumentProcessingStepName sn, CancellationToken ct = default) => Task.FromResult<DocumentProcessingStep?>(null);
        public Task<IReadOnlyList<DocumentProcessingRun>> GetRunsByDocumentAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentProcessingRun>>(Array.Empty<DocumentProcessingRun>());
        public Task<IReadOnlyList<DocumentProcessingStep>> GetStepsByRunAsync(Guid tid, Guid rid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentProcessingStep>>(Array.Empty<DocumentProcessingStep>());
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public int CallCount { get; set; }
        public bool ShouldThrow { get; set; }

        public Task<EmbeddingResult> GenerateEmbeddingAsync(EmbeddingRequest request, CancellationToken ct = default)
        {
            CallCount++;
            if (ShouldThrow) throw new InvalidOperationException("Simulated embedding failure");

            var vector = Enumerable.Repeat(0.125f, 8).ToArray();
            return Task.FromResult(new EmbeddingResult(
                vector, "mock", "mock-embedding-8", 8, "v1"));
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
}
