using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Common;
using OpenRAG.Application.Documents.UploadDocument;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

namespace OpenRAG.UnitTests.Application.Documents;

public sealed class UploadDocumentHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ══ Rejection tests ═══════════════════════════════════════════

    [Fact]
    public async Task Rejects_empty_file_name()
    {
        var handler = CreateHandler();
        var command = new UploadDocumentCommand("", "application/pdf", 1024, Stream.Null, "test-1");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(command).AsTask());
        Assert.Contains("File name", ex.Message);
    }

    [Fact]
    public async Task Rejects_whitespace_file_name()
    {
        var handler = CreateHandler();
        var command = new UploadDocumentCommand("   ", "application/pdf", 1024, Stream.Null, "test-2");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(command).AsTask());
        Assert.Contains("File name", ex.Message);
    }

    [Fact]
    public async Task Rejects_empty_content_type()
    {
        var handler = CreateHandler();
        var command = new UploadDocumentCommand("report.pdf", "", 1024, Stream.Null, "test-3");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(command).AsTask());
        Assert.Contains("Content type", ex.Message);
    }

    [Fact]
    public async Task Rejects_zero_size()
    {
        var handler = CreateHandler();
        var command = new UploadDocumentCommand("report.pdf", "application/pdf", 0, Stream.Null, "test-4");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(command).AsTask());
        Assert.Contains("size", ex.Message);
    }

    [Fact]
    public async Task Rejects_negative_size()
    {
        var handler = CreateHandler();
        var command = new UploadDocumentCommand("report.pdf", "application/pdf", -1, Stream.Null, "test-5");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(command).AsTask());
        Assert.Contains("size", ex.Message);
    }

    [Fact]
    public async Task Rejects_null_content_stream()
    {
        var handler = CreateHandler();
        var command = new UploadDocumentCommand("report.pdf", "application/pdf", 1024, null!, "test-6");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(command).AsTask());
        Assert.Contains("Content stream", ex.Message);
    }

    [Fact]
    public async Task Rejects_oversize_file()
    {
        var handler = CreateHandler();
        var command = new UploadDocumentCommand(
            "large.pdf", "application/pdf", 200 * 1024 * 1024, Stream.Null, "test-7");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(command).AsTask());
        Assert.Contains("maximum", ex.Message);
    }

    [Fact]
    public async Task Rejects_unauthenticated_user()
    {
        var handler = CreateHandler(isAuthenticated: false);
        var content = CreateContentStream("hello world");
        var command = new UploadDocumentCommand("report.pdf", "application/pdf", content.Length, content, "test-8");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(command).AsTask());
        Assert.Contains("authenticated", ex.Message);
    }

    [Fact]
    public async Task Rejects_empty_tenant_id()
    {
        var handler = CreateHandler(tenantId: Guid.Empty);
        var content = CreateContentStream("hello world");
        var command = new UploadDocumentCommand("report.pdf", "application/pdf", content.Length, content, "test-9");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(command).AsTask());
        Assert.Contains("tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_empty_user_id()
    {
        var handler = CreateHandler(userId: Guid.Empty);
        var content = CreateContentStream("hello world");
        var command = new UploadDocumentCommand("report.pdf", "application/pdf", content.Length, content, "test-10");

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(command).AsTask());
        Assert.Contains("user", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ══ Success tests ═════════════════════════════════════════════

    [Fact]
    public async Task Uploads_valid_document_and_returns_response()
    {
        var content = CreateContentStream("Test PDF content");
        var (handler, fakes) = CreateHandlerWithFakes();

        var command = new UploadDocumentCommand(
            "report.pdf", "application/pdf", content.Length, content, "corr-123");

        var response = await handler.Handle(command);

        Assert.NotEqual(Guid.Empty, response.DocumentId);
        Assert.NotEqual(Guid.Empty, response.VersionId);
        Assert.Equal("Uploaded", response.Status);
    }

    [Fact]
    public async Task Saves_original_file_through_IFileStorage()
    {
        var content = CreateContentStream("Test PDF content");
        var (handler, fakes) = CreateHandlerWithFakes();

        var command = new UploadDocumentCommand(
            "report.pdf", "application/pdf", content.Length, content, "corr-123");

        await handler.Handle(command);

        Assert.NotNull(fakes.FileStorage.SavedObjectKey);
        Assert.Contains("report.pdf", fakes.FileStorage.SavedObjectKey);
        Assert.Equal("application/pdf", fakes.FileStorage.SavedContentType);
    }

    [Fact]
    public async Task Persists_Document_through_repository()
    {
        var content = CreateContentStream("Test PDF content");
        var (handler, fakes) = CreateHandlerWithFakes();

        var command = new UploadDocumentCommand(
            "report.pdf", "application/pdf", content.Length, content, "corr-123");

        await handler.Handle(command);

        Assert.NotNull(fakes.DocumentRepository.AddedDocument);
        Assert.Equal("report.pdf", fakes.DocumentRepository.AddedDocument!.Title);
        Assert.Equal(TenantId, fakes.DocumentRepository.AddedDocument.TenantId);
    }

    [Fact]
    public async Task Persists_ProcessingRun_through_repository()
    {
        var content = CreateContentStream("Test PDF content");
        var (handler, fakes) = CreateHandlerWithFakes();

        var command = new UploadDocumentCommand(
            "report.pdf", "application/pdf", content.Length, content, "corr-123");

        await handler.Handle(command);

        Assert.NotNull(fakes.ProcessingRunRepository.AddedRun);
        Assert.Equal(ProcessingRunReason.InitialUpload, fakes.ProcessingRunRepository.AddedRun!.RunReason);
        Assert.Equal("corr-123", fakes.ProcessingRunRepository.AddedRun.CorrelationId);
    }

    [Fact]
    public async Task Publishes_DocumentUploadedEvent()
    {
        var content = CreateContentStream("Test PDF content");
        var (handler, fakes) = CreateHandlerWithFakes();

        var command = new UploadDocumentCommand(
            "report.pdf", "application/pdf", content.Length, content, "corr-123");

        await handler.Handle(command);

        Assert.Equal("document.uploaded", fakes.EventBus.LastTopic);
        Assert.NotNull(fakes.EventBus.LastEvent);
    }

    [Fact]
    public async Task Calls_UnitOfWork_SaveChanges()
    {
        var content = CreateContentStream("Test PDF content");
        var (handler, fakes) = CreateHandlerWithFakes();

        var command = new UploadDocumentCommand(
            "report.pdf", "application/pdf", content.Length, content, "corr-123");

        await handler.Handle(command);

        Assert.True(fakes.UnitOfWork.SaveChangesCalled);
    }

    [Fact]
    public async Task Begins_transaction_for_valid_upload()
    {
        var content = CreateContentStream("Test PDF content");
        var (handler, fakes) = CreateHandlerWithFakes();

        var command = new UploadDocumentCommand(
            "report.pdf", "application/pdf", content.Length, content, "corr-123");

        await handler.Handle(command);

        Assert.True(fakes.UnitOfWork.TransactionCommitted);
    }

    [Fact]
    public async Task Commits_transaction_after_successful_upload()
    {
        var content = CreateContentStream("Test PDF content");
        var (handler, fakes) = CreateHandlerWithFakes();

        var command = new UploadDocumentCommand(
            "report.pdf", "application/pdf", content.Length, content, "corr-123");

        await handler.Handle(command);

        Assert.True(fakes.UnitOfWork.TransactionCommitted);
        Assert.True(fakes.UnitOfWork.TransactionDisposed);
    }

    [Fact]
    public async Task Propagates_exception_if_SaveChanges_fails()
    {
        var (handler, fakes) = CreateHandlerWithFakes();
        fakes.UnitOfWork.ShouldThrowOnSaveChanges = true;
        var content = CreateContentStream("Test PDF content");

        var command = new UploadDocumentCommand(
            "report.pdf", "application/pdf", content.Length, content, "corr-123");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(command).AsTask());

        // Transaction should be rolled back (disposed without commit)
        Assert.False(fakes.UnitOfWork.TransactionCommitted);
        Assert.True(fakes.UnitOfWork.TransactionDisposed);
    }

    [Fact]
    public async Task Propagates_exception_if_commit_fails()
    {
        var (handler, fakes) = CreateHandlerWithFakes();
        fakes.UnitOfWork.ShouldThrowOnCommit = true;
        var content = CreateContentStream("Test PDF content");

        var command = new UploadDocumentCommand(
            "report.pdf", "application/pdf", content.Length, content, "corr-123");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(command).AsTask());

        Assert.False(fakes.UnitOfWork.TransactionCommitted);
    }

    [Fact]
    public async Task Propagates_exception_if_event_publish_fails()
    {
        var (handler, fakes) = CreateHandlerWithFakes();
        fakes.EventBus.ShouldThrow = true;
        var content = CreateContentStream("Test PDF content");

        var command = new UploadDocumentCommand(
            "report.pdf", "application/pdf", content.Length, content, "corr-123");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(command).AsTask());

        // Transaction should be rolled back on failure
        Assert.False(fakes.UnitOfWork.TransactionCommitted);
        Assert.True(fakes.UnitOfWork.TransactionDisposed);
    }

    // ══ Helpers ═══════════════════════════════════════════════════

    private static Stream CreateContentStream(string content)
    {
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream, leaveOpen: true);
        writer.Write(content);
        writer.Flush();
        stream.Position = 0;
        return stream;
    }

    private static UploadDocumentHandler CreateHandler(
        Guid? tenantId = null,
        Guid? userId = null,
        bool isAuthenticated = true)
    {
        var resolvedUserId = userId ?? UserId;
        var currentUser = !isAuthenticated
            ? TestCurrentUser.Unauthenticated(resolvedUserId)
            : resolvedUserId == Guid.Empty
                ? TestCurrentUser.InvalidOrEmpty()
                : TestCurrentUser.Authenticated(resolvedUserId);

        return new UploadDocumentHandler(
            new FakeFileStorage(),
            new FakeDocumentRepository(),
            new FakeProcessingRunRepository(),
            new FakeDocumentEventBus(),
            new StubCurrentTenant(tenantId ?? TenantId),
            currentUser,
            new StubClock(),
            new FakeUnitOfWork());
    }

    private static (UploadDocumentHandler Handler, FakeHolder Fakes) CreateHandlerWithFakes()
    {
        var storage = new FakeFileStorage();
        var docRepo = new FakeDocumentRepository();
        var runRepo = new FakeProcessingRunRepository();
        var eventBus = new FakeDocumentEventBus();
        var tenant = new StubCurrentTenant(TenantId);
        var user = TestCurrentUser.Authenticated(UserId);
        var clock = new StubClock();
        var uow = new FakeUnitOfWork();

        var handler = new UploadDocumentHandler(storage, docRepo, runRepo, eventBus, tenant, user, clock, uow);
        return (handler, new FakeHolder(storage, docRepo, runRepo, eventBus, uow));
    }

    private sealed record FakeHolder(
        FakeFileStorage FileStorage,
        FakeDocumentRepository DocumentRepository,
        FakeProcessingRunRepository ProcessingRunRepository,
        FakeDocumentEventBus EventBus,
        FakeUnitOfWork UnitOfWork);

    // ══ Stubs / Fakes ═════════════════════════════════════════════

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public StubCurrentTenant(Guid tenantId) => TenantId = tenantId;
        public Guid TenantId { get; }
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        private TestCurrentUser(Guid userId, bool isAuthenticated)
        {
            UserId = userId;
            IsAuthenticated = isAuthenticated;
        }

        public Guid UserId { get; }
        public bool IsAuthenticated { get; }

        public static TestCurrentUser Authenticated(Guid userId) => new(userId, true);
        public static TestCurrentUser Unauthenticated(Guid userId) => new(userId, false);
        public static TestCurrentUser InvalidOrEmpty() => new(Guid.Empty, true);
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        public string? SavedObjectKey { get; private set; }
        public string? SavedContentType { get; private set; }

        public Task<StoredObjectResult> SaveAsync(Stream content, string objectKey, string contentType, CancellationToken ct = default)
        {
            SavedObjectKey = objectKey;
            SavedContentType = contentType;
            return Task.FromResult(new StoredObjectResult("local", objectKey, contentType, content.Length, null, "abc123"));
        }

        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string objectKey, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        public Document? AddedDocument { get; private set; }

        public Task AddAsync(Document document, CancellationToken ct = default)
        {
            AddedDocument = document;
            return Task.CompletedTask;
        }

        public Task<Document?> GetByIdAsync(Guid tenantId, Guid documentId, CancellationToken ct = default)
            => Task.FromResult<Document?>(null);

        public Task<Document?> GetByIdWithVersionsAsync(Guid tenantId, Guid documentId, CancellationToken ct = default)
            => Task.FromResult<Document?>(null);

        public Task<Document?> GetByIdForUpdateAsync(Guid tenantId, Guid documentId, CancellationToken ct = default)
            => Task.FromResult<Document?>(null);

        public Task<DocumentVersion?> GetVersionAsync(Guid tenantId, Guid documentId, Guid versionId, CancellationToken ct = default)
            => Task.FromResult<DocumentVersion?>(null);

        public Task<DocumentVersion?> GetVersionForUpdateAsync(Guid tenantId, Guid documentId, Guid versionId, CancellationToken ct = default)
            => Task.FromResult<DocumentVersion?>(null);

        public Task<bool> ExistsAsync(Guid tenantId, Guid documentId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<DocumentListResult> ListAsync(Guid tid, int pn, int ps, string? sf, string? s, CancellationToken ct = default)
            => Task.FromResult(new DocumentListResult(Array.Empty<DocumentListItem>(), pn, ps, 0));

        public Task DeleteAsync(Document doc, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeProcessingRunRepository : IProcessingRunRepository
    {
        public DocumentProcessingRun? AddedRun { get; private set; }

        public Task AddAsync(DocumentProcessingRun processingRun, CancellationToken ct = default)
        {
            AddedRun = processingRun;
            return Task.CompletedTask;
        }

        public Task<DocumentProcessingRun?> GetByIdAsync(Guid tenantId, Guid processingRunId, CancellationToken ct = default)
            => Task.FromResult<DocumentProcessingRun?>(null);

        public Task<DocumentProcessingRun?> GetByIdForUpdateAsync(Guid tenantId, Guid processingRunId, CancellationToken ct = default)
            => Task.FromResult<DocumentProcessingRun?>(null);

        public Task AddStepAsync(DocumentProcessingStep step, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<DocumentProcessingStep?> GetStepAsync(Guid tenantId, Guid processingRunId, DocumentProcessingStepName stepName, CancellationToken ct = default)
            => Task.FromResult<DocumentProcessingStep?>(null);

        public Task<DocumentProcessingStep?> GetStepForUpdateAsync(Guid tenantId, Guid processingRunId, DocumentProcessingStepName stepName, CancellationToken ct = default)
            => Task.FromResult<DocumentProcessingStep?>(null);

        public Task<IReadOnlyList<DocumentProcessingRun>> GetRunsByDocumentAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentProcessingRun>>(Array.Empty<DocumentProcessingRun>());

        public Task<IReadOnlyList<DocumentProcessingStep>> GetStepsByRunAsync(Guid tid, Guid rid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentProcessingStep>>(Array.Empty<DocumentProcessingStep>());
    }

    private sealed class FakeDocumentEventBus : IDocumentEventBus
    {
        public string? LastTopic { get; private set; }
        public object? LastEvent { get; private set; }
        public bool ShouldThrow { get; set; }

        public Task PublishAsync<TEvent>(string topic, TEvent message, CancellationToken ct = default)
        {
            if (ShouldThrow)
                throw new InvalidOperationException("Simulated event publish failure");
            LastTopic = topic;
            LastEvent = message;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public bool SaveChangesCalled { get; private set; }
        public bool TransactionCommitted { get; private set; }
        public bool TransactionDisposed { get; private set; }
        public bool ShouldThrowOnCommit { get; set; }
        public bool ShouldThrowOnSaveChanges { get; set; }

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            if (ShouldThrowOnSaveChanges)
                throw new InvalidOperationException("Simulated SaveChanges failure");
            SaveChangesCalled = true;
            return Task.CompletedTask;
        }

        public Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken ct = default)
        {
            var txn = new FakeApplicationTransaction(this);
            return Task.FromResult<IApplicationTransaction>(txn);
        }

        private sealed class FakeApplicationTransaction : IApplicationTransaction
        {
            private readonly FakeUnitOfWork _uow;

            public FakeApplicationTransaction(FakeUnitOfWork uow) => _uow = uow;

            public Task CommitAsync(CancellationToken ct = default)
            {
                if (_uow.ShouldThrowOnCommit)
                    throw new InvalidOperationException("Simulated commit failure");
                _uow.TransactionCommitted = true;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                _uow.TransactionDisposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
