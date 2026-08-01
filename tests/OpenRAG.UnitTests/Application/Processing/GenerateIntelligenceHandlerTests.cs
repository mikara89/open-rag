using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Common;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Application.Processing.GenerateIntelligence;
using OpenRAG.Domain.Common;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

namespace OpenRAG.UnitTests.Application.Processing;

public sealed class GenerateIntelligenceHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid VersionId = Guid.NewGuid();
    private static readonly Guid RunId = Guid.NewGuid();

    [Fact]
    public async Task Rejects_empty_TenantId()
    {
        var handler = CreateHandler(new Fakes());
        var command = new GenerateIntelligenceCommand(
            Guid.Empty, DocumentId, VersionId, RunId, "corr");

        var exception = await Assert.ThrowsAsync<AppException>(() => handler.Handle(command).AsTask());

        Assert.Contains("TenantId", exception.Message);
    }

    [Fact]
    public async Task Uses_command_tenant_for_all_downstream_work()
    {
        var fakes = new Fakes();
        var handler = CreateHandler(fakes);
        var command = new GenerateIntelligenceCommand(
            TenantId, DocumentId, VersionId, RunId, "corr");

        var response = await handler.Handle(command);

        Assert.Equal("Generated", response.Status);
        Assert.Equal(TenantId, fakes.DocumentRepository.LastTenantId);
        Assert.Equal(TenantId, fakes.RunRepository.LastTenantId);
        Assert.Equal(TenantId, fakes.IntelligenceRepository.LastTenantId);
        Assert.Equal(TenantId, fakes.IntelligenceService.LastRequest?.TenantId);
        Assert.Equal(TenantId, fakes.IntelligenceRepository.Added?.TenantId);
        var published = Assert.IsType<DocumentIntelligenceGeneratedEvent>(fakes.EventBus.LastEvent);
        Assert.Equal(TenantId, published.TenantId);
    }

    [Fact]
    public async Task Foreign_tenant_document_scope_performs_no_secondary_work()
    {
        var fakes = new Fakes();
        fakes.DocumentRepository.ReturnMissing = true;

        var response = await CreateHandler(fakes).Handle(new GenerateIntelligenceCommand(
            TenantId, DocumentId, VersionId, RunId, "foreign-scope"));

        Assert.Equal("DocumentNotFound", response.Status);
        Assert.False(fakes.FileStorage.ReadCalled);
        Assert.Null(fakes.IntelligenceService.LastRequest);
        Assert.Null(fakes.IntelligenceRepository.Added);
        Assert.Null(fakes.EventBus.LastEvent);
    }

    private static GenerateIntelligenceHandler CreateHandler(Fakes fakes)
        => new(
            fakes.DocumentRepository,
            fakes.IntelligenceRepository,
            fakes.RunRepository,
            fakes.FileStorage,
            fakes.IntelligenceService,
            fakes.EventBus,
            new StubClock(),
            new FakeUnitOfWork(),
            Options.Create(new GenerateIntelligenceOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GenerateIntelligenceHandler>.Instance,
            new OpenRAG.Application.Storage.DocumentObjectKeyPolicy());

    private sealed class Fakes
    {
        public FakeDocumentRepository DocumentRepository { get; } = new();
        public FakeIntelligenceRepository IntelligenceRepository { get; } = new();
        public FakeRunRepository RunRepository { get; } = new();
        public FakeIntelligenceService IntelligenceService { get; } = new();
        public FakeEventBus EventBus { get; } = new();
        public FakeFileStorage FileStorage { get; } = new();
    }

    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        public Guid LastTenantId { get; private set; }
        public bool ReturnMissing { get; set; }

        public Task<Document?> GetByIdForUpdateAsync(Guid tenantId, Guid documentId, CancellationToken ct = default)
        {
            LastTenantId = tenantId;
            return Task.FromResult<Document?>(ReturnMissing
                ? null
                : Document.Create(documentId, tenantId, "title", "file.pdf", Guid.NewGuid()));
        }

        public Task<DocumentVersion?> GetVersionForUpdateAsync(Guid tenantId, Guid documentId, Guid versionId, CancellationToken ct = default)
        {
            LastTenantId = tenantId;
            var version = DocumentVersion.Create(
                versionId,
                tenantId,
                documentId,
                1,
                $"tenants/{tenantId:D}/documents/{documentId:D}/versions/{versionId:D}/original/source.pdf",
                "application/pdf",
                10,
                "hash");
            version.AttachDoclingArtifacts(
                $"tenants/{tenantId:D}/documents/{documentId:D}/versions/{versionId:D}/docling/document.md",
                $"tenants/{tenantId:D}/documents/{documentId:D}/versions/{versionId:D}/docling/document.json");
            return Task.FromResult<DocumentVersion?>(version);
        }

        public Task AddAsync(Document document, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Document?> GetByIdAsync(Guid tenantId, Guid documentId, CancellationToken ct = default) => Task.FromResult<Document?>(null);
        public Task<Document?> GetByIdWithVersionsAsync(Guid tenantId, Guid documentId, CancellationToken ct = default) => Task.FromResult<Document?>(null);
        public Task<DocumentVersion?> GetVersionAsync(Guid tenantId, Guid documentId, Guid versionId, CancellationToken ct = default) => Task.FromResult<DocumentVersion?>(null);
        public Task<bool> ExistsAsync(Guid tenantId, Guid documentId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<DocumentListResult> ListAsync(Guid tenantId, int pageNumber, int pageSize, string? statusFilter, string? search, CancellationToken ct = default)
            => Task.FromResult(new DocumentListResult([], pageNumber, pageSize, 0));
        public Task DeleteAsync(Document document, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeIntelligenceRepository : IDocumentIntelligenceRepository
    {
        public Guid LastTenantId { get; private set; }
        public DocumentIntelligence? Added { get; private set; }
        public Task<DocumentIntelligence?> GetByVersionAsync(Guid tenantId, Guid documentId, Guid versionId, CancellationToken ct = default)
            => Task.FromResult<DocumentIntelligence?>(null);
        public Task AddAsync(DocumentIntelligence intelligence, CancellationToken ct = default)
        {
            Added = intelligence;
            return Task.CompletedTask;
        }
        public Task DeleteByVersionAsync(Guid tenantId, Guid documentId, Guid versionId, CancellationToken ct = default)
        {
            LastTenantId = tenantId;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRunRepository : IProcessingRunRepository
    {
        public Guid LastTenantId { get; private set; }
        public Task<DocumentProcessingRun?> GetByIdForUpdateAsync(Guid tenantId, Guid runId, CancellationToken ct = default)
        {
            LastTenantId = tenantId;
            return Task.FromResult<DocumentProcessingRun?>(DocumentProcessingRun.Create(
                runId, tenantId, DocumentId, VersionId, ProcessingRunReason.InitialUpload, "corr"));
        }
        public Task<DocumentProcessingStep?> GetStepForUpdateAsync(Guid tenantId, Guid runId, DocumentProcessingStepName stepName, CancellationToken ct = default)
        {
            LastTenantId = tenantId;
            return Task.FromResult<DocumentProcessingStep?>(null);
        }
        public Task AddAsync(DocumentProcessingRun run, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DocumentProcessingRun?> GetByIdAsync(Guid tenantId, Guid runId, CancellationToken ct = default) => Task.FromResult<DocumentProcessingRun?>(null);
        public Task AddStepAsync(DocumentProcessingStep step, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DocumentProcessingStep?> GetStepAsync(Guid tenantId, Guid runId, DocumentProcessingStepName stepName, CancellationToken ct = default) => Task.FromResult<DocumentProcessingStep?>(null);
        public Task<IReadOnlyList<DocumentProcessingRun>> GetRunsByDocumentAsync(Guid tenantId, Guid documentId, Guid versionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentProcessingRun>>([]);
        public Task<IReadOnlyList<DocumentProcessingStep>> GetStepsByRunAsync(Guid tenantId, Guid runId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentProcessingStep>>([]);
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        public bool ReadCalled { get; private set; }
        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct = default)
        {
            ReadCalled = true;
            return Task.FromResult<Stream>(new MemoryStream(global::System.Text.Encoding.UTF8.GetBytes("document content")));
        }
        public Task<StoredObjectResult> SaveAsync(Stream content, string objectKey, string contentType, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task DeleteAsync(string objectKey, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeIntelligenceService : IDocumentIntelligenceService
    {
        public DocumentIntelligenceRequest? LastRequest { get; private set; }
        public Task<DocumentIntelligenceResult> GenerateAsync(DocumentIntelligenceRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new DocumentIntelligenceResult(
                "report", "summary", ["key"], [], new Dictionary<string, string>(), "test", "test"));
        }
    }

    private sealed class FakeEventBus : IDocumentEventBus
    {
        public object? LastEvent { get; private set; }
        public Task PublishAsync<TEvent>(string topic, TEvent message, CancellationToken ct = default)
        {
            LastEvent = message;
            return Task.CompletedTask;
        }
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Task.FromResult<IApplicationTransaction>(new FakeTransaction());

        private sealed class FakeTransaction : IApplicationTransaction
        {
            public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
