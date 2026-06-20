using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Processing;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Common;
using OpenRAG.Application.Processing.PreprocessDocument;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

namespace OpenRAG.UnitTests.Application.Processing;

public sealed class PreprocessDocumentHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DocId = Guid.NewGuid();
    private static readonly Guid VerId = Guid.NewGuid();
    private static readonly Guid RunId = Guid.NewGuid();

    [Fact]
    public async Task Rejects_empty_DocumentId()
    {
        var handler = CreateHandler();
        var cmd = new PreprocessDocumentCommand(Guid.Empty, VerId, RunId, "corr");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(cmd).AsTask());
        Assert.Contains("DocumentId", ex.Message);
    }

    [Fact]
    public async Task Rejects_empty_VersionId()
    {
        var handler = CreateHandler();
        var cmd = new PreprocessDocumentCommand(DocId, Guid.Empty, RunId, "corr");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(cmd).AsTask());
        Assert.Contains("VersionId", ex.Message);
    }

    [Fact]
    public async Task Rejects_empty_ProcessingRunId()
    {
        var handler = CreateHandler();
        var cmd = new PreprocessDocumentCommand(DocId, VerId, Guid.Empty, "corr");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(cmd).AsTask());
        Assert.Contains("ProcessingRunId", ex.Message);
    }

    [Fact]
    public async Task Throws_when_version_is_missing()
    {
        var fakes = CreateFakes(versionMissing: true);
        var handler = CreateHandler(fakes);
        var cmd = new PreprocessDocumentCommand(DocId, VerId, RunId, "corr");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(cmd).AsTask());
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Throws_when_processing_run_is_missing()
    {
        var fakes = CreateFakes(runMissing: true);
        var handler = CreateHandler(fakes);
        var cmd = new PreprocessDocumentCommand(DocId, VerId, RunId, "corr");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(cmd).AsTask());
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Invokes_IDocumentPreprocessor()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var cmd = new PreprocessDocumentCommand(DocId, VerId, RunId, "corr");

        await handler.Handle(cmd);

        Assert.True(fakes.Preprocessor.Called);
    }

    [Fact]
    public async Task Attaches_Markdown_and_JSON_keys_to_DocumentVersion()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var cmd = new PreprocessDocumentCommand(DocId, VerId, RunId, "corr");

        var response = await handler.Handle(cmd);

        Assert.Equal("md-key", response.MarkdownObjectKey);
        Assert.Equal("json-key", response.JsonObjectKey);
        Assert.Equal("Preprocessed", response.Status);
    }

    [Fact]
    public async Task Publishes_DocumentPreprocessedEvent()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var cmd = new PreprocessDocumentCommand(DocId, VerId, RunId, "corr");

        await handler.Handle(cmd);

        Assert.Equal("document.preprocessed", fakes.EventBus.LastTopic);
        Assert.NotNull(fakes.EventBus.LastEvent);
    }

    [Fact]
    public async Task Commits_transaction_on_success()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var cmd = new PreprocessDocumentCommand(DocId, VerId, RunId, "corr");

        await handler.Handle(cmd);

        Assert.True(fakes.UnitOfWork.TransactionCommitted);
    }

    [Fact]
    public async Task Persists_failed_step_when_preprocessor_fails()
    {
        var fakes = CreateFakes();
        fakes.Preprocessor.ShouldThrow = true;
        var handler = CreateHandler(fakes);
        var cmd = new PreprocessDocumentCommand(DocId, VerId, RunId, "corr");

        var response = await handler.Handle(cmd);

        Assert.Equal("Failed", response.Status);
        // Failure state should be persisted (transaction committed, SaveChanges called)
        Assert.True(fakes.UnitOfWork.SaveChangesCalled);
        Assert.True(fakes.UnitOfWork.TransactionCommitted);
    }

    [Fact]
    public async Task Does_not_publish_DocumentPreprocessedEvent_when_preprocessor_fails()
    {
        var fakes = CreateFakes();
        fakes.Preprocessor.ShouldThrow = true;
        var handler = CreateHandler(fakes);
        var cmd = new PreprocessDocumentCommand(DocId, VerId, RunId, "corr");

        await handler.Handle(cmd);

        // Event bus should not have published document.preprocessed
        Assert.NotEqual("document.preprocessed", fakes.EventBus.LastTopic);
    }

    [Fact]
    public async Task Is_idempotent_when_step_already_completed()
    {
        var fakes = CreateFakes(stepStatus: DocumentProcessingStepStatus.Completed);
        var handler = CreateHandler(fakes);
        var cmd = new PreprocessDocumentCommand(DocId, VerId, RunId, "corr");

        var response = await handler.Handle(cmd);

        Assert.Equal("AlreadyPreprocessed", response.Status);
        Assert.False(fakes.Preprocessor.Called); // Preprocessor not re-invoked
    }

    // ══ Helpers ═══════════════════════════════════════════════════

    private static PreprocessDocumentHandler CreateHandler(AllFakes? fakes = null)
    {
        fakes ??= CreateFakes();
        return new PreprocessDocumentHandler(
            fakes.Tenant, fakes.DocRepo, fakes.RunRepo, fakes.Preprocessor,
            fakes.EventBus, fakes.Clock, fakes.UnitOfWork,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PreprocessDocumentHandler>.Instance);
    }

    private static AllFakes CreateFakes(
        bool versionMissing = false,
        bool runMissing = false,
        DocumentProcessingStepStatus? stepStatus = null)
    {
        var tenant = new StubTenant(TenantId);

        DocumentProcessingStep? step = null;
        if (stepStatus.HasValue)
        {
            step = DocumentProcessingStep.Create(
                Guid.NewGuid(), TenantId, DocId, VerId, RunId,
                DocumentProcessingStepName.Preprocess, 3,
                "hash", "MockPreprocessor", "1.0");
            if (stepStatus == DocumentProcessingStepStatus.Completed)
            {
                step.Start();
                step.MarkCompleted("output-hash");
            }
        }

        var versionEntity = versionMissing ? null : CreateVersion();
        var runEntity = runMissing ? null : CreateRun();

        var docRepo = new FakeDocRepo(versionEntity);
        var runRepo = new FakeRunRepo(runEntity, step);
        var preprocessor = new FakePreprocessor();
        var eventBus = new FakeEventBus();
        var clock = new StubClock();
        var uow = new FakeUoW();

        return new AllFakes(tenant, docRepo, runRepo, preprocessor, eventBus, clock, uow);
    }

    private static DocumentVersion CreateVersion()
        => DocumentVersion.Create(VerId, TenantId, DocId, 1,
            "key", "application/pdf", 1024, "abc123");

    private static DocumentProcessingRun CreateRun()
        => DocumentProcessingRun.Create(RunId, TenantId, DocId, VerId,
            ProcessingRunReason.InitialUpload, "corr-123");

    private sealed record AllFakes(
        StubTenant Tenant,
        FakeDocRepo DocRepo,
        FakeRunRepo RunRepo,
        FakePreprocessor Preprocessor,
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

    private sealed class FakeDocRepo : IDocumentRepository
    {
        private readonly DocumentVersion? _version;
        public FakeDocRepo(DocumentVersion? version) => _version = version;

        public Task AddAsync(Document doc, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Document?> GetByIdAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult<Document?>(null);
        public Task<Document?> GetByIdWithVersionsAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult<Document?>(null);
        public Task<Document?> GetByIdForUpdateAsync(Guid tid, Guid did, CancellationToken ct = default)
            => Task.FromResult<Document?>(Document.Create(did, tid, "test", "test.md", Guid.NewGuid()));
        public Task<DocumentVersion?> GetVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult<DocumentVersion?>(null);
        public Task<DocumentVersion?> GetVersionForUpdateAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(_version);
        public Task<bool> ExistsAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult(true);

        public Task<DocumentListResult> ListAsync(Guid tid, int pn, int ps, string? sf, string? s, CancellationToken ct = default)
            => Task.FromResult(new DocumentListResult(Array.Empty<DocumentListItem>(), pn, ps, 0));

        public Task DeleteAsync(Document doc, CancellationToken ct = default) => Task.CompletedTask;
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

    private sealed class FakePreprocessor : IDocumentPreprocessor
    {
        public bool Called { get; private set; }
        public bool ShouldThrow { get; set; }

        public Task<DocumentPreprocessingResult> PreprocessAsync(DocumentPreprocessingRequest req, CancellationToken ct = default)
        {
            Called = true;
            if (ShouldThrow) throw new InvalidOperationException("Simulated preprocessor failure");
            return Task.FromResult(new DocumentPreprocessingResult("md-key", "json-key", "md-hash", "json-hash"));
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
