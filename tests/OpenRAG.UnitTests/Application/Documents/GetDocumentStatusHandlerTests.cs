using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;
using OpenRAG.Application.Documents.GetDocumentStatus;
using OpenRAG.Domain.Documents;

namespace OpenRAG.UnitTests.Application.Documents;

public sealed class GetDocumentStatusHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Returns_status_for_existing_document()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id, TenantId);

        var response = await handler.Handle(query);

        Assert.Equal(fakes.Document.Id, response.DocumentId);
        Assert.Equal("Uploaded", response.Status);
        Assert.Equal("report.pdf", response.OriginalFileName);
    }

    [Fact]
    public async Task Returns_document_status_with_versions()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id, TenantId);

        var response = await handler.Handle(query);

        Assert.NotEmpty(response.Versions);
        Assert.Equal(fakes.Version.Id, response.Versions[0].VersionId);
    }

    [Fact]
    public async Task Includes_chunk_count()
    {
        var fakes = CreateFakes(chunkCount: 5);
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id, TenantId);

        var response = await handler.Handle(query);

        Assert.Equal(5, response.Versions[0].ChunkCount);
    }

    [Fact]
    public async Task Includes_embedding_count()
    {
        var fakes = CreateFakes(embeddingCount: 3);
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id, TenantId);

        var response = await handler.Handle(query);

        Assert.Equal(3, response.Versions[0].EmbeddingCount);
    }

    [Fact]
    public async Task Includes_embedding_metadata()
    {
        var fakes = CreateFakes(embeddingCount: 2, embeddingProvider: "mock", embeddingModel: "mock-8", embeddingDimensions: 8);
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id, TenantId);

        var response = await handler.Handle(query);

        Assert.Equal("mock", response.Versions[0].EmbeddingProvider);
        Assert.Equal("mock-8", response.Versions[0].EmbeddingModel);
        Assert.Equal(8, response.Versions[0].EmbeddingDimensions);
    }

    [Fact]
    public async Task Derives_Uploaded_when_original_exists_but_no_artifacts()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id, TenantId);

        var response = await handler.Handle(query);

        Assert.Equal("Uploaded", response.Versions[0].Status);
    }

    [Fact]
    public async Task Derives_Preprocessed_when_markdown_artifact_exists()
    {
        var fakes = CreateFakes(preprocessed: true);
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id, TenantId);

        var response = await handler.Handle(query);

        Assert.Equal("Preprocessed", response.Versions[0].Status);
    }

    [Fact]
    public async Task Derives_Chunked_when_chunks_exist()
    {
        var fakes = CreateFakes(preprocessed: true, chunkCount: 3);
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id, TenantId);

        var response = await handler.Handle(query);

        Assert.Equal("Chunked", response.Versions[0].Status);
    }

    [Fact]
    public async Task Derives_Ready_when_embeddings_exist()
    {
        var fakes = CreateFakes(preprocessed: true, chunkCount: 3, embeddingCount: 3);
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id, TenantId);

        var response = await handler.Handle(query);

        Assert.Equal("Ready", response.Versions[0].Status);
    }

    [Fact]
    public async Task Includes_processing_step_status()
    {
        var fakes = CreateFakes(preprocessed: true, chunkCount: 3, embeddingCount: 3);
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id, TenantId);

        var response = await handler.Handle(query);

        Assert.NotEmpty(response.Versions[0].Steps);
        Assert.Contains(response.Versions[0].Steps, s => s.Name == "Preprocess");
        Assert.Contains(response.Versions[0].Steps, s => s.Name == "Chunk");
        Assert.Contains(response.Versions[0].Steps, s => s.Name == "GenerateEmbeddings");
    }

    [Fact]
    public async Task Throws_not_found_for_missing_document()
    {
        var fakes = CreateFakes(noDocument: true);
        var handler = CreateHandler(fakes);

        var query = new GetDocumentStatusQuery(Guid.NewGuid(), TenantId);

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(query).AsTask());
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Rejects_empty_document_id()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);

        var query = new GetDocumentStatusQuery(Guid.Empty, TenantId);

        var ex = await Assert.ThrowsAsync<AppException>(() => handler.Handle(query).AsTask());
        Assert.Contains("DocumentId", ex.Message);
    }

    // ══ Helpers ═══════════════════════════════════════════════════

    private static GetDocumentStatusHandler CreateHandler(AllFakes fakes)
        => new(fakes.DocRepo, fakes.ChunkRepo, fakes.EmbeddingRepo, fakes.TenantStub);

    private static AllFakes CreateFakes(
        bool noDocument = false,
        bool preprocessed = false,
        int chunkCount = 0,
        int embeddingCount = 0,
        string? embeddingProvider = null,
        string? embeddingModel = null,
        int? embeddingDimensions = null)
    {
        var doc = Document.Create(Guid.NewGuid(), TenantId, "report.pdf", "report.pdf", UserId);
        Document? docToReturn = noDocument ? null : doc;

        // Create version and attach to document if preprocessed
        DocumentVersion? version = null;
        if (!noDocument)
        {
            version = doc.AddVersion(Guid.NewGuid(), 1, "tenants/t/doc/v/orig/report.pdf", "application/pdf", 1024, "abc");
            if (preprocessed)
            {
                version.AttachDoclingArtifacts("tenants/t/doc/v/md.md", "tenants/t/doc/v/json.json");
                version.MarkPreprocessed();
            }
        }

        var docRepo = new FakeDocumentRepository(docToReturn);
        var chunkRepo = new FakeChunkRepo(chunkCount);
        var embeddingRepo = new FakeEmbeddingRepo(embeddingCount, embeddingProvider, embeddingModel, embeddingDimensions);
        var tenantStub = new StubCurrentTenant(TenantId);

        return new AllFakes(docRepo, chunkRepo, embeddingRepo, tenantStub, docToReturn!, version!);
    }

    private sealed record AllFakes(
        FakeDocumentRepository DocRepo,
        FakeChunkRepo ChunkRepo,
        FakeEmbeddingRepo EmbeddingRepo,
        StubCurrentTenant TenantStub,
        Document Document,
        DocumentVersion Version);

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public Guid TenantId { get; }
        public StubCurrentTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        private readonly Document? _document;
        public FakeDocumentRepository(Document? document) => _document = document;

        public Task AddAsync(Document document, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Document?> GetByIdAsync(Guid tenantId, Guid documentId, CancellationToken ct = default) => Task.FromResult(_document);
        public Task<Document?> GetByIdWithVersionsAsync(Guid tenantId, Guid documentId, CancellationToken ct = default) => Task.FromResult(_document);
        public Task<Document?> GetByIdForUpdateAsync(Guid tenantId, Guid documentId, CancellationToken ct = default) => Task.FromResult(_document);
        public Task<DocumentVersion?> GetVersionAsync(Guid tenantId, Guid documentId, Guid versionId, CancellationToken ct = default) => Task.FromResult<DocumentVersion?>(null);
        public Task<DocumentVersion?> GetVersionForUpdateAsync(Guid tenantId, Guid documentId, Guid versionId, CancellationToken ct = default) => Task.FromResult<DocumentVersion?>(null);
        public Task<bool> ExistsAsync(Guid tenantId, Guid documentId, CancellationToken ct = default) => Task.FromResult(_document is not null);
    }

    private sealed class FakeChunkRepo : IDocumentChunkRepository
    {
        private readonly int _count;
        public FakeChunkRepo(int count) => _count = count;

        public Task AddRangeAsync(IReadOnlyCollection<DocumentChunk> chunks, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentChunk>> GetByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentChunk>>(Array.Empty<DocumentChunk>());
        public Task<bool> AnyForVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(_count > 0);
        public Task<int> CountByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.FromResult(_count);
    }

    private sealed class FakeEmbeddingRepo : IDocumentEmbeddingRepository
    {
        private readonly int _count;
        private readonly string? _provider;
        private readonly string? _model;
        private readonly int? _dimensions;

        public FakeEmbeddingRepo(int count, string? provider, string? model, int? dimensions)
        {
            _count = count; _provider = provider; _model = model; _dimensions = dimensions;
        }

        public Task AddRangeAsync(IReadOnlyCollection<DocumentEmbedding> embeddings, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentEmbedding>> GetByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentEmbedding>>(Array.Empty<DocumentEmbedding>());
        public Task<bool> AnyForVersionAsync(Guid tid, Guid did, Guid vid, string model, CancellationToken ct = default)
            => Task.FromResult(_count > 0);
        public Task<int> CountByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult(_count);
        public Task<DocumentEmbeddingMetadata?> GetMetadataByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
        {
            if (_count == 0 || _provider is null) return Task.FromResult<DocumentEmbeddingMetadata?>(null);
            return Task.FromResult<DocumentEmbeddingMetadata?>(
                new DocumentEmbeddingMetadata(_provider, _model!, _dimensions!.Value, "v1", _count));
        }
    }
}
