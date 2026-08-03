using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;
using OpenRAG.Application.Documents.GetDocumentIntelligence;
using OpenRAG.Domain.Documents;

namespace OpenRAG.UnitTests.Application.Documents;

public sealed class GetDocumentIntelligenceHandlerTests
{
    private static readonly Guid TenantId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DocumentId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid VersionId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UserId = new("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task Existing_intelligence_returns_success()
    {
        var document = CreateDocument();
        var intelligence = CreateIntelligence();
        var handler = new GetDocumentIntelligenceHandler(
            new StubDocumentRepository(document),
            new StubIntelligenceRepository(intelligence),
            new StubTenant());

        var result = await handler.Handle(
            new GetDocumentIntelligenceQuery(DocumentId, VersionId));

        Assert.True(result.IsSuccess);
        Assert.Equal("summary", result.Value.Summary);
        Assert.Equal(["one", "two"], result.Value.Keywords);
    }

    [Theory]
    [InlineData("document")]
    [InlineData("version")]
    [InlineData("intelligence")]
    public async Task Missing_or_invalid_nested_resource_returns_same_not_found(string scenario)
    {
        var document = scenario == "document" ? null : CreateDocument();
        if (scenario == "version")
            document = Document.Create(DocumentId, TenantId, "file.md", "file.md", UserId);
        var intelligence = scenario == "intelligence" ? null : CreateIntelligence();
        var handler = new GetDocumentIntelligenceHandler(
            new StubDocumentRepository(document),
            new StubIntelligenceRepository(intelligence),
            new StubTenant());

        var result = await handler.Handle(
            new GetDocumentIntelligenceQuery(DocumentId, VersionId));

        Assert.True(result.IsFailure);
        Assert.Equal("resource.not_found", result.PrimaryError.Code);
        Assert.Equal("The requested resource was not found.", result.PrimaryError.Message);
    }

    [Fact]
    public async Task Foreign_document_from_repository_remains_isolation_exception()
    {
        var foreignDocument = Document.Create(
            DocumentId,
            Guid.NewGuid(),
            "file.md",
            "file.md",
            UserId);
        var handler = new GetDocumentIntelligenceHandler(
            new StubDocumentRepository(foreignDocument),
            new StubIntelligenceRepository(null),
            new StubTenant());

        await Assert.ThrowsAsync<IsolationViolationException>(() =>
            handler.Handle(new GetDocumentIntelligenceQuery(DocumentId, VersionId)).AsTask());
    }

    [Fact]
    public async Task Infrastructure_failure_is_not_converted_to_result()
    {
        var failure = new InvalidOperationException("database unavailable");
        var handler = new GetDocumentIntelligenceHandler(
            new StubDocumentRepository(null, failure),
            new StubIntelligenceRepository(null),
            new StubTenant());

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new GetDocumentIntelligenceQuery(DocumentId, VersionId)).AsTask());

        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new GetDocumentIntelligenceHandler(
            new StubDocumentRepository(null),
            new StubIntelligenceRepository(null),
            new StubTenant());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.Handle(
                new GetDocumentIntelligenceQuery(DocumentId, VersionId),
                cancellation.Token).AsTask());
    }

    private static Document CreateDocument()
    {
        var document = Document.Create(DocumentId, TenantId, "file.md", "file.md", UserId);
        document.AddVersion(VersionId, 1, "source", "text/markdown", 10, "hash");
        return document;
    }

    private static DocumentIntelligence CreateIntelligence() =>
        DocumentIntelligence.Create(
            Guid.NewGuid(),
            TenantId,
            DocumentId,
            VersionId,
            "class",
            "summary",
            "[\"one\",\"two\"]",
            "[]",
            "{}",
            "provider",
            "model");

    private sealed class StubTenant : ICurrentTenant
    {
        public Guid TenantId => GetDocumentIntelligenceHandlerTests.TenantId;
    }

    private sealed class StubDocumentRepository : IDocumentRepository
    {
        private readonly Document? _document;
        private readonly Exception? _failure;

        public StubDocumentRepository(Document? document, Exception? failure = null)
        {
            _document = document;
            _failure = failure;
        }

        public Task<Document?> GetByIdWithVersionsAsync(
            Guid tenantId,
            Guid documentId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_failure is not null)
                return Task.FromException<Document?>(_failure);
            return Task.FromResult(_document);
        }

        public Task AddAsync(Document document, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Document?> GetByIdAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken = default) => Task.FromResult(_document);
        public Task<Document?> GetByIdForUpdateAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken = default) => Task.FromResult(_document);
        public Task<DocumentVersion?> GetVersionAsync(Guid tenantId, Guid documentId, Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult<DocumentVersion?>(null);
        public Task<DocumentVersion?> GetVersionForUpdateAsync(Guid tenantId, Guid documentId, Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult<DocumentVersion?>(null);
        public Task<bool> ExistsAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken = default) => Task.FromResult(_document is not null);
        public Task<DocumentListResult> ListAsync(Guid tenantId, int pageNumber, int pageSize, string? statusFilter, string? search, CancellationToken cancellationToken = default) => Task.FromResult(new DocumentListResult([], pageNumber, pageSize, 0));
        public Task DeleteAsync(Document document, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubIntelligenceRepository : IDocumentIntelligenceRepository
    {
        private readonly DocumentIntelligence? _intelligence;

        public StubIntelligenceRepository(DocumentIntelligence? intelligence)
        {
            _intelligence = intelligence;
        }

        public Task<DocumentIntelligence?> GetByVersionAsync(Guid tenantId, Guid documentId, Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult(_intelligence);
        public Task AddAsync(DocumentIntelligence intelligence, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteByVersionAsync(Guid tenantId, Guid documentId, Guid versionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
