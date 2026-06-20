using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Common;
using OpenRAG.Application.Documents.GetMarkdownArtifact;
using OpenRAG.Domain.Documents;

namespace OpenRAG.UnitTests.Application.Documents;

public sealed class GetMarkdownArtifactHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DocId = Guid.NewGuid();
    private static readonly Guid VerId = Guid.NewGuid();

    [Fact]
    public async Task Returns_markdown_content()
    {
        var version = CreateVersion("md-key", "json-key");
        var fakes = new Fakes(version, "md-key", "# Hello Markdown");
        var handler = new GetMarkdownArtifactHandler(fakes.DocRepo, fakes.FileStorage, fakes.Tenant);

        var response = await handler.Handle(new GetMarkdownArtifactQuery(DocId, VerId));

        Assert.Equal("# Hello Markdown", response.Content);
        Assert.Equal("text/markdown", response.ContentType);
    }

    [Fact]
    public async Task Returns_404_when_version_not_found()
    {
        var fakes = new Fakes(null, null, null);
        var handler = new GetMarkdownArtifactHandler(fakes.DocRepo, fakes.FileStorage, fakes.Tenant);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            handler.Handle(new GetMarkdownArtifactQuery(DocId, VerId)).AsTask());
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Returns_404_when_artifact_missing()
    {
        var version = CreateVersion(null, null);
        var fakes = new Fakes(version, null, null);
        var handler = new GetMarkdownArtifactHandler(fakes.DocRepo, fakes.FileStorage, fakes.Tenant);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            handler.Handle(new GetMarkdownArtifactQuery(DocId, VerId)).AsTask());
        Assert.Contains("not available", ex.Message.ToLower());
    }

    [Fact]
    public async Task Does_not_expose_raw_storage_key()
    {
        var version = CreateVersion("secret/key/path", "json-key");
        var fakes = new Fakes(version, "secret/key/path", "content");
        var handler = new GetMarkdownArtifactHandler(fakes.DocRepo, fakes.FileStorage, fakes.Tenant);

        var response = await handler.Handle(new GetMarkdownArtifactQuery(DocId, VerId));

        Assert.DoesNotContain("secret/key/path", response.Content);
    }

    private static DocumentVersion CreateVersion(string? mdKey, string? jsonKey)
    {
        var version = DocumentVersion.Create(VerId, TenantId, DocId, 1, "orig-key", "text/markdown", 100, "abc");
        if (mdKey is not null && jsonKey is not null)
        {
            version.AttachDoclingArtifacts(mdKey, jsonKey);
            version.MarkPreprocessed();
        }
        return version;
    }

    private sealed class Fakes
    {
        public Fakes(DocumentVersion? version, string? expectedKey, string? storedContent)
        {
            DocRepo = new FakeDocRepo(version);
            FileStorage = new FakeFileStorage(expectedKey, storedContent);
        }

        public FakeDocRepo DocRepo { get; }
        public FakeFileStorage FileStorage { get; }
        public StubTenant Tenant => new(TenantId);
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

    private sealed class FakeFileStorage : IFileStorage
    {
        private readonly string? _expectedKey;
        private readonly string? _storedContent;
        public FakeFileStorage(string? expectedKey, string? storedContent) { _expectedKey = expectedKey; _storedContent = storedContent; }

        public Task<StoredObjectResult> SaveAsync(Stream c, string k, string ct, CancellationToken _ = default)
            => Task.FromResult(new StoredObjectResult("b", k, ct, 0, null, null));
        public Task<Stream> OpenReadAsync(string key, CancellationToken _ = default)
        {
            if (key != _expectedKey) throw new InvalidOperationException("Wrong key");
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(_storedContent ?? "");
            writer.Flush();
            stream.Position = 0;
            return Task.FromResult<Stream>(stream);
        }
        public Task DeleteAsync(string key, CancellationToken _ = default) => Task.CompletedTask;
    }

    private sealed class StubTenant : ICurrentTenant
    {
        public StubTenant(Guid tenantId) => TenantId = tenantId;
        public Guid TenantId { get; }
    }
}
