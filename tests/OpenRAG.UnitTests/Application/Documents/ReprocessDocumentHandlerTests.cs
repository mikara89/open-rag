using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Common;
using OpenRAG.Application.Documents.ReprocessDocument;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

namespace OpenRAG.UnitTests.Application.Documents;

public sealed class ReprocessDocumentHandlerTests
{
    private static readonly Guid TenantId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DocumentId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid VersionId = new("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

    private static ReprocessDocumentHandler CreateHandler(Fakes fakes)
    {
        return new ReprocessDocumentHandler(
            fakes.DocRepo,
            fakes.ChunkRepo,
            fakes.EmbeddingRepo,
            fakes.IntelligenceRepo,
            fakes.RunRepo,
            fakes.EventBus,
            fakes.Tenant,
            fakes.Clock,
            fakes.Uow);
    }

    private static ReprocessDocumentCommand CreateCommand(
        bool forcePreprocess = true,
        bool forceChunk = true,
        bool forceIntelligence = true,
        bool forceEmbeddings = true)
    {
        return new ReprocessDocumentCommand(
            TenantId,
            DocumentId,
            forcePreprocess,
            forceChunk,
            forceIntelligence,
            forceEmbeddings,
            Guid.NewGuid().ToString("N"));
    }

    private static Document CreateReadyDocument()
    {
        var doc = Document.Create(DocumentId, TenantId, "test.md", "test.md", UserId);
        doc.MarkProcessing();
        var version = doc.AddVersion(VersionId, 1, "tenants/tid/docs/did/versions/vid/original/test.md", "text/markdown", 1024, "abc123");
        version.AttachDoclingArtifacts("markdown-key", "json-key");
        version.MarkPreprocessed();
        doc.MarkReady();
        return doc;
    }

    // ── 404 / not found tests ───────────────────────────────────────

    [Fact]
    public async Task Returns_error_when_document_not_found()
    {
        var fakes = new Fakes { Doc = null };

        var handler = CreateHandler(fakes);
        var command = CreateCommand();

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(command).AsTask());
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Returns_error_when_document_belongs_to_different_tenant()
    {
        var doc = CreateReadyDocument();
        var otherTenantId = Guid.NewGuid();
        var fakes = new Fakes { Doc = doc };

        var handler = CreateHandler(fakes);
        var command = new ReprocessDocumentCommand(otherTenantId, DocumentId, true, true, true, true, "corr-1");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(command).AsTask());
        Assert.Contains("does not belong to tenant", ex.Message);
    }

    [Fact]
    public async Task Returns_error_when_document_is_deleted()
    {
        var doc = CreateReadyDocument();
        // Simulate deleted via EF parameterless ctor approach — set Status via reflection
        // Actually, we create a deleted state by bypassing domain rules in the mock
        var fakes = new Fakes
        {
            Doc = Document.Create(DocumentId, TenantId, "deleted.md", "deleted.md", UserId)
        };
        // Use reflection to set status to Deleted (the domain model prevents this transition)
        typeof(Document).GetProperty("Status")!.SetValue(fakes.Doc, DocumentStatus.Deleted);

        var handler = CreateHandler(fakes);
        var command = CreateCommand();

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(command).AsTask());
        Assert.Contains("deleted", ex.Message.ToLower());
    }

    [Fact]
    public async Task Returns_error_when_document_already_processing()
    {
        var doc = CreateReadyDocument();
        typeof(Document).GetProperty("Status")!.SetValue(doc, DocumentStatus.Processing);
        var fakes = new Fakes { Doc = doc };

        var handler = CreateHandler(fakes);
        var command = CreateCommand();

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(command).AsTask());
        Assert.Contains("already processing", ex.Message.ToLower());
    }

    [Fact]
    public async Task Returns_error_when_document_has_no_version()
    {
        var doc = Document.Create(DocumentId, TenantId, "noversion.md", "noversion.md", UserId);
        doc.MarkProcessing();
        doc.MarkReady();
        // CurrentVersionId remains null because no version was added
        var fakes = new Fakes { Doc = doc };

        var handler = CreateHandler(fakes);
        var command = CreateCommand();

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(command).AsTask());
        Assert.Contains("no version", ex.Message.ToLower());
    }

    // ── Status transition tests ─────────────────────────────────────

    [Fact]
    public async Task Sets_document_status_to_processing()
    {
        var doc = CreateReadyDocument();
        var fakes = new Fakes { Doc = doc, Version = GetCurrentVersion(doc) };

        var handler = CreateHandler(fakes);
        var command = CreateCommand();

        var response = await handler.Handle(command);

        Assert.Equal("Processing", response.Status);
    }

    // ── Event publishing tests ──────────────────────────────────────

    [Fact]
    public async Task Publishes_preprocess_requested_when_forcePreprocess_is_true()
    {
        var doc = CreateReadyDocument();
        var fakes = new Fakes { Doc = doc, Version = GetCurrentVersion(doc) };

        var handler = CreateHandler(fakes);
        var command = CreateCommand(forcePreprocess: true, forceChunk: false, forceEmbeddings: false);

        var response = await handler.Handle(command);

        Assert.NotEmpty(fakes.EventBus.PublishedTopics);
        Assert.Contains("document.preprocess.requested", fakes.EventBus.PublishedTopics);
    }

    [Fact]
    public async Task Publishes_chunking_requested_when_only_forceChunk_is_true()
    {
        var doc = CreateReadyDocument();
        var version = GetCurrentVersion(doc);
        var fakes = new Fakes { Doc = doc, Version = version };

        var handler = CreateHandler(fakes);
        var command = CreateCommand(forcePreprocess: false, forceChunk: true, forceIntelligence: false, forceEmbeddings: false);

        var response = await handler.Handle(command);

        Assert.NotEmpty(fakes.EventBus.PublishedTopics);
        Assert.Contains("document.chunking.requested", fakes.EventBus.PublishedTopics);
    }

    [Fact]
    public async Task Publishes_intelligence_requested_when_only_forceIntelligence_is_true()
    {
        var doc = CreateReadyDocument();
        var version = GetCurrentVersion(doc);
        var fakes = new Fakes { Doc = doc, Version = version };

        var handler = CreateHandler(fakes);
        var command = CreateCommand(forcePreprocess: false, forceChunk: false, forceIntelligence: true, forceEmbeddings: false);

        var response = await handler.Handle(command);

        Assert.NotEmpty(fakes.EventBus.PublishedTopics);
        Assert.Contains("document.intelligence.requested", fakes.EventBus.PublishedTopics);
    }

    [Fact]
    public async Task Publishes_embeddings_requested_when_only_forceEmbeddings_is_true()
    {
        var doc = CreateReadyDocument();
        var version = GetCurrentVersion(doc);
        var fakes = new Fakes { Doc = doc, Version = version };

        var handler = CreateHandler(fakes);
        var command = CreateCommand(forcePreprocess: false, forceChunk: false, forceIntelligence: false, forceEmbeddings: true);

        var response = await handler.Handle(command);

        Assert.NotEmpty(fakes.EventBus.PublishedTopics);
        Assert.Contains("document.embeddings.requested", fakes.EventBus.PublishedTopics);
    }

    [Fact]
    public async Task Publishes_preprocess_requested_when_all_flags_are_true()
    {
        var doc = CreateReadyDocument();
        var version = GetCurrentVersion(doc);
        var fakes = new Fakes { Doc = doc, Version = version };

        var handler = CreateHandler(fakes);
        var command = CreateCommand(forcePreprocess: true, forceChunk: true, forceEmbeddings: true);

        await handler.Handle(command);

        Assert.Contains("document.preprocess.requested", fakes.EventBus.PublishedTopics);
        // Only preprocess is published first; chunk and embed come via the event chain
        Assert.DoesNotContain("document.chunking.requested", fakes.EventBus.PublishedTopics);
        Assert.DoesNotContain("document.embeddings.requested", fakes.EventBus.PublishedTopics);
    }

    // ── Data cleanup tests ──────────────────────────────────────────

    [Fact]
    public async Task Deletes_chunks_when_forceChunk_is_true()
    {
        var doc = CreateReadyDocument();
        var version = GetCurrentVersion(doc);
        var fakes = new Fakes { Doc = doc, Version = version };

        var handler = CreateHandler(fakes);
        var command = CreateCommand(forcePreprocess: false, forceChunk: true, forceEmbeddings: false);

        await handler.Handle(command);

        Assert.True(fakes.ChunkRepo.DeleteCalled);
        Assert.Equal(version.Id, fakes.ChunkRepo.DeletedVersionId);
    }

    [Fact]
    public async Task Deletes_embeddings_when_forceEmbeddings_is_true()
    {
        var doc = CreateReadyDocument();
        var version = GetCurrentVersion(doc);
        var fakes = new Fakes { Doc = doc, Version = version };

        var handler = CreateHandler(fakes);
        var command = CreateCommand(forcePreprocess: false, forceChunk: false, forceEmbeddings: true);

        await handler.Handle(command);

        Assert.True(fakes.EmbeddingRepo.DeleteCalled);
        Assert.Equal(version.Id, fakes.EmbeddingRepo.DeletedVersionId);
    }

    [Fact]
    public async Task Does_not_delete_chunks_when_forceChunk_is_false()
    {
        var doc = CreateReadyDocument();
        var version = GetCurrentVersion(doc);
        var fakes = new Fakes { Doc = doc, Version = version };

        var handler = CreateHandler(fakes);
        var command = CreateCommand(forcePreprocess: false, forceChunk: false, forceEmbeddings: true);

        await handler.Handle(command);

        Assert.False(fakes.ChunkRepo.DeleteCalled);
    }

    [Fact]
    public async Task Does_not_delete_embeddings_when_forceEmbeddings_is_false()
    {
        var doc = CreateReadyDocument();
        var version = GetCurrentVersion(doc);
        var fakes = new Fakes { Doc = doc, Version = version };

        var handler = CreateHandler(fakes);
        var command = CreateCommand(forcePreprocess: false, forceChunk: true, forceEmbeddings: false);

        await handler.Handle(command);

        Assert.False(fakes.EmbeddingRepo.DeleteCalled);
    }

    // ── Idempotency / no-data tests ─────────────────────────────────

    [Fact]
    public async Task Works_when_no_chunks_or_embeddings_exist()
    {
        var doc = CreateReadyDocument();
        var version = GetCurrentVersion(doc);
        var fakes = new Fakes { Doc = doc, Version = version };

        var handler = CreateHandler(fakes);
        var command = CreateCommand(forcePreprocess: false, forceChunk: true, forceEmbeddings: true);

        // Should not throw when deleting non-existent chunks/embeddings
        var response = await handler.Handle(command);

        Assert.Equal("Processing", response.Status);
    }

    [Fact]
    public async Task Does_not_delete_original_source_file()
    {
        var doc = CreateReadyDocument();
        var version = GetCurrentVersion(doc);
        var originalKey = version.OriginalObjectKey;
        var fakes = new Fakes { Doc = doc, Version = version };

        var handler = CreateHandler(fakes);
        var command = CreateCommand(forcePreprocess: false, forceChunk: true, forceEmbeddings: true);

        await handler.Handle(command);

        // OriginalObjectKey should remain unchanged
        Assert.Equal(originalKey, version.OriginalObjectKey);
    }

    // ── Response shape tests ────────────────────────────────────────

    [Fact]
    public async Task Returns_correct_response_shape()
    {
        var doc = CreateReadyDocument();
        var version = GetCurrentVersion(doc);
        var fakes = new Fakes { Doc = doc, Version = version };

        var handler = CreateHandler(fakes);
        var command = CreateCommand();

        var response = await handler.Handle(command);

        Assert.Equal(DocumentId, response.DocumentId);
        Assert.Equal(version.Id, response.VersionId);
        Assert.Equal("Processing", response.Status);
        Assert.NotNull(response.CorrelationId);
        Assert.NotEmpty(response.CorrelationId);
    }

    // ── Processing run reason tests ─────────────────────────────────

    [Fact]
    public async Task Uses_ReprocessWithNewPreprocessor_reason_when_forcePreprocess_true()
    {
        var doc = CreateReadyDocument();
        var version = GetCurrentVersion(doc);
        var fakes = new Fakes { Doc = doc, Version = version };

        var handler = CreateHandler(fakes);
        var command = CreateCommand(forcePreprocess: true, forceChunk: false, forceIntelligence: false, forceEmbeddings: false);

        await handler.Handle(command);

        Assert.NotNull(fakes.RunRepo.AddedRun);
        Assert.Equal(ProcessingRunReason.ReprocessWithNewPreprocessor, fakes.RunRepo.AddedRun!.RunReason);
    }

    [Fact]
    public async Task Uses_ReprocessWithNewIntelligenceModel_reason_when_only_forceIntelligence_true()
    {
        var doc = CreateReadyDocument();
        var version = GetCurrentVersion(doc);
        var fakes = new Fakes { Doc = doc, Version = version };

        var handler = CreateHandler(fakes);
        var command = CreateCommand(forcePreprocess: false, forceChunk: false, forceIntelligence: true, forceEmbeddings: false);

        await handler.Handle(command);

        Assert.NotNull(fakes.RunRepo.AddedRun);
        Assert.Equal(ProcessingRunReason.ReprocessWithNewIntelligenceModel, fakes.RunRepo.AddedRun!.RunReason);
    }

    [Fact]
    public async Task Uses_ReprocessWithNewEmbeddingModel_reason_when_only_forceEmbeddings_true()
    {
        var doc = CreateReadyDocument();
        var version = GetCurrentVersion(doc);
        var fakes = new Fakes { Doc = doc, Version = version };

        var handler = CreateHandler(fakes);
        var command = CreateCommand(forcePreprocess: false, forceChunk: false, forceIntelligence: false, forceEmbeddings: true);

        await handler.Handle(command);

        Assert.NotNull(fakes.RunRepo.AddedRun);
        Assert.Equal(ProcessingRunReason.ReprocessWithNewEmbeddingModel, fakes.RunRepo.AddedRun!.RunReason);
    }

    [Fact]
    public async Task Uses_ManualRetry_reason_when_only_forceChunk_true()
    {
        var doc = CreateReadyDocument();
        var version = GetCurrentVersion(doc);
        var fakes = new Fakes { Doc = doc, Version = version };

        var handler = CreateHandler(fakes);
        var command = CreateCommand(forcePreprocess: false, forceChunk: true, forceIntelligence: false, forceEmbeddings: false);

        await handler.Handle(command);

        Assert.NotNull(fakes.RunRepo.AddedRun);
        Assert.Equal(ProcessingRunReason.ManualRetry, fakes.RunRepo.AddedRun!.RunReason);
    }

    // ── Helper ──────────────────────────────────────────────────────

    private static DocumentVersion GetCurrentVersion(Document doc)
    {
        return doc.Versions.First(v => v.Id == doc.CurrentVersionId);
    }

    // ── Fakes ───────────────────────────────────────────────────────

    private sealed class Fakes
    {
        public Document? Doc { get; set; }
        public DocumentVersion? Version { get; set; }
        public FakeDocRepo DocRepo => new(Doc, Version);
        public FakeChunkRepo ChunkRepo { get; } = new();
        public FakeEmbeddingRepo EmbeddingRepo { get; } = new();
        public FakeIntelligenceRepo IntelligenceRepo { get; } = new();
        public FakeRunRepo RunRepo { get; } = new();
        public FakeEventBus EventBus { get; } = new();
        public StubTenant Tenant => new(TenantId);
        public StubClock Clock => new(Now);
        public StubUow Uow => new();
    }

    private sealed class FakeDocRepo : IDocumentRepository
    {
        private readonly Document? _doc;
        private readonly DocumentVersion? _version;

        public FakeDocRepo(Document? doc, DocumentVersion? version)
        {
            _doc = doc;
            _version = version;
        }

        public Task AddAsync(Document document, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Document?> GetByIdAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult(_doc);
        public Task<Document?> GetByIdWithVersionsAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult(_doc);
        public Task<Document?> GetByIdForUpdateAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult(_doc);
        public Task<DocumentVersion?> GetVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(_version);
        public Task<DocumentVersion?> GetVersionForUpdateAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(_version);
        public Task<bool> ExistsAsync(Guid tid, Guid did, CancellationToken ct = default) => Task.FromResult(_doc is not null);

        public Task<DocumentListResult> ListAsync(Guid tid, int pn, int ps, string? sf, string? s, CancellationToken ct = default)
            => Task.FromResult(new DocumentListResult(Array.Empty<DocumentListItem>(), pn, ps, 0));

        public Task DeleteAsync(Document doc, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeChunkRepo : IDocumentChunkRepository
    {
        public bool DeleteCalled { get; private set; }
        public Guid DeletedVersionId { get; private set; }

        public Task AddRangeAsync(IReadOnlyCollection<DocumentChunk> chunks, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentChunk>> GetByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentChunk>>(Array.Empty<DocumentChunk>());
        public Task<bool> AnyForVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(false);
        public Task<int> CountByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(0);
        public Task DeleteByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
        {
            DeleteCalled = true;
            DeletedVersionId = vid;
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
        public Guid DeletedVersionId { get; private set; }

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
            DeletedVersionId = vid;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIntelligenceRepo : IDocumentIntelligenceRepository
    {
        public bool DeleteCalled { get; private set; }
        public Guid DeletedVersionId { get; private set; }

        public Task<DocumentIntelligence?> GetByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<DocumentIntelligence?>(null);

        public Task AddAsync(DocumentIntelligence intelligence, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
        {
            DeleteCalled = true;
            DeletedVersionId = vid;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRunRepo : IProcessingRunRepository
    {
        public DocumentProcessingRun? AddedRun { get; private set; }

        public Task AddAsync(DocumentProcessingRun run, CancellationToken ct = default)
        {
            AddedRun = run;
            return Task.CompletedTask;
        }

        public Task<DocumentProcessingRun?> GetByIdAsync(Guid tid, Guid rid, CancellationToken ct = default) => Task.FromResult<DocumentProcessingRun?>(null);
        public Task<DocumentProcessingRun?> GetByIdForUpdateAsync(Guid tid, Guid rid, CancellationToken ct = default) => Task.FromResult<DocumentProcessingRun?>(null);
        public Task AddStepAsync(DocumentProcessingStep step, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DocumentProcessingStep?> GetStepAsync(Guid tid, Guid rid, DocumentProcessingStepName sn, CancellationToken ct = default) => Task.FromResult<DocumentProcessingStep?>(null);
        public Task<DocumentProcessingStep?> GetStepForUpdateAsync(Guid tid, Guid rid, DocumentProcessingStepName sn, CancellationToken ct = default) => Task.FromResult<DocumentProcessingStep?>(null);
        public Task<IReadOnlyList<DocumentProcessingRun>> GetRunsByDocumentAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentProcessingRun>>(Array.Empty<DocumentProcessingRun>());
        public Task<IReadOnlyList<DocumentProcessingStep>> GetStepsByRunAsync(Guid tid, Guid rid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentProcessingStep>>(Array.Empty<DocumentProcessingStep>());
    }

    private sealed class FakeEventBus : IDocumentEventBus
    {
        public List<string> PublishedTopics { get; } = [];

        public Task PublishAsync<TEvent>(string topic, TEvent message, CancellationToken ct = default)
        {
            PublishedTopics.Add(topic);
            return Task.CompletedTask;
        }
    }

    private sealed class StubTenant : ICurrentTenant
    {
        public StubTenant(Guid tenantId) => TenantId = tenantId;
        public Guid TenantId { get; }
    }

    private sealed class StubClock : IClock
    {
        private readonly DateTimeOffset _now;
        public StubClock(DateTimeOffset now) => _now = now;
        public DateTimeOffset UtcNow => _now;
    }

    private sealed class StubUow : IUnitOfWork
    {
        public Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IApplicationTransaction>(new StubTransaction());
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubTransaction : IApplicationTransaction
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
