using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;
using OpenRAG.Application.Documents.GetDocumentStatus;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

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
        var query = new GetDocumentStatusQuery(fakes.Document.Id);

        var response = (await handler.Handle(query)).Value;

        Assert.Equal(fakes.Document.Id, response.DocumentId);
        Assert.Equal("Uploaded", response.Status);
        Assert.Equal("report.pdf", response.OriginalFileName);
    }

    [Fact]
    public async Task Returns_document_status_with_versions()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id);

        var response = (await handler.Handle(query)).Value;

        Assert.NotEmpty(response.Versions);
        Assert.Equal(fakes.Version.Id, response.Versions[0].VersionId);
    }

    [Fact]
    public async Task Includes_chunk_count()
    {
        var fakes = CreateFakes(chunkCount: 5);
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id);

        var response = (await handler.Handle(query)).Value;

        Assert.Equal(5, response.Versions[0].ChunkCount);
    }

    [Fact]
    public async Task Includes_embedding_count()
    {
        var fakes = CreateFakes(embeddingCount: 3);
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id);

        var response = (await handler.Handle(query)).Value;

        Assert.Equal(3, response.Versions[0].EmbeddingCount);
    }

    [Fact]
    public async Task Includes_embedding_metadata()
    {
        var fakes = CreateFakes(embeddingCount: 2, embeddingProvider: "mock", embeddingModel: "mock-8", embeddingDimensions: 8);
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id);

        var response = (await handler.Handle(query)).Value;

        Assert.Equal("mock", response.Versions[0].EmbeddingProvider);
        Assert.Equal("mock-8", response.Versions[0].EmbeddingModel);
        Assert.Equal(8, response.Versions[0].EmbeddingDimensions);
    }

    [Fact]
    public async Task Derives_Uploaded_when_original_exists_but_no_artifacts()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id);

        var response = (await handler.Handle(query)).Value;

        Assert.Equal("Uploaded", response.Versions[0].Status);
    }

    [Fact]
    public async Task Derives_Preprocessed_when_markdown_artifact_exists()
    {
        var fakes = CreateFakes(preprocessed: true);
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id);

        var response = (await handler.Handle(query)).Value;

        Assert.Equal("Preprocessed", response.Versions[0].Status);
    }

    [Fact]
    public async Task Derives_Chunked_when_chunks_exist()
    {
        var fakes = CreateFakes(preprocessed: true, chunkCount: 3);
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id);

        var response = (await handler.Handle(query)).Value;

        Assert.Equal("Chunked", response.Versions[0].Status);
    }

    [Fact]
    public async Task Derives_Ready_when_embeddings_exist()
    {
        var fakes = CreateFakes(preprocessed: true, chunkCount: 3, embeddingCount: 3);
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id);

        var response = (await handler.Handle(query)).Value;

        Assert.Equal("Ready", response.Versions[0].Status);
    }

    [Fact]
    public async Task Includes_processing_step_status()
    {
        var fakes = CreateFakes(preprocessed: true, chunkCount: 3, embeddingCount: 3);
        var handler = CreateHandler(fakes);
        var query = new GetDocumentStatusQuery(fakes.Document.Id);

        var response = (await handler.Handle(query)).Value;

        Assert.NotEmpty(response.Versions[0].Steps);
        Assert.Contains(response.Versions[0].Steps, s => s.Name == "Preprocess");
        Assert.Contains(response.Versions[0].Steps, s => s.Name == "Chunk");
        Assert.Contains(response.Versions[0].Steps, s => s.Name == "GenerateEmbeddings");
    }

    [Fact]
    public async Task Returns_not_found_for_missing_document()
    {
        var fakes = CreateFakes(noDocument: true);
        var handler = CreateHandler(fakes);

        var query = new GetDocumentStatusQuery(Guid.NewGuid());

        var result = await handler.Handle(query);
        Assert.Equal("resource.not_found", result.PrimaryError.Code);
    }

    [Fact]
    public async Task Rejects_empty_document_id()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);

        var query = new GetDocumentStatusQuery(Guid.Empty);

        var result = await handler.Handle(query);
        Assert.Equal("request.document_id_required", result.PrimaryError.Code);
    }

    // ── Processing history tests ───────────────────────────────────

    [Fact]
    public async Task Includes_processing_runs_in_response()
    {
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var run = DocumentProcessingRun.Create(
            Guid.NewGuid(), TenantId, documentId, versionId,
            ProcessingRunReason.InitialUpload, "corr-123");
        var fakes = CreateFakes(runs: new[] { run }, documentId: documentId, versionId: versionId);
        var handler = CreateHandler(fakes);

        var response = (await handler.Handle(new GetDocumentStatusQuery(fakes.Document.Id))).Value;

        Assert.NotEmpty(response.ProcessingRuns);
        Assert.Equal(run.Id, response.ProcessingRuns[0].RunId);
        Assert.Equal("InitialUpload", response.ProcessingRuns[0].Reason);
        Assert.Equal("corr-123", response.ProcessingRuns[0].CorrelationId);
    }

    [Fact]
    public async Task Includes_step_history_in_runs()
    {
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var run = DocumentProcessingRun.Create(
            Guid.NewGuid(), TenantId, documentId, versionId,
            ProcessingRunReason.InitialUpload, "corr-456");
        var step = DocumentProcessingStep.Create(
            Guid.NewGuid(), TenantId, run.DocumentId, run.VersionId, run.Id,
            DocumentProcessingStepName.Preprocess, 3, "hash-abc", "MockPreprocessor", "1.0");
        step.Start();
        var fakes = CreateFakes(
            runs: new[] { run },
            steps: new Dictionary<Guid, IReadOnlyList<DocumentProcessingStep>>
            {
                [run.Id] = new[] { step }
            },
            documentId: documentId,
            versionId: versionId);
        var handler = CreateHandler(fakes);

        var response = (await handler.Handle(new GetDocumentStatusQuery(fakes.Document.Id))).Value;

        Assert.NotEmpty(response.ProcessingRuns);
        var runDto = response.ProcessingRuns[0];
        Assert.NotEmpty(runDto.Steps);
        Assert.Equal("Preprocess", runDto.Steps[0].Name);
    }

    [Fact]
    public async Task Returns_empty_processing_runs_when_none_exist()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);

        var response = (await handler.Handle(new GetDocumentStatusQuery(fakes.Document.Id))).Value;

        Assert.Empty(response.ProcessingRuns);
    }

    // ══ Helpers ═══════════════════════════════════════════════════

    private static GetDocumentStatusHandler CreateHandler(AllFakes fakes)
        => new(fakes.DocRepo, fakes.ChunkRepo, fakes.EmbeddingRepo, fakes.RunRepo, fakes.TenantStub);

    private static AllFakes CreateFakes(
        bool noDocument = false,
        bool preprocessed = false,
        int chunkCount = 0,
        int embeddingCount = 0,
        string? embeddingProvider = null,
        string? embeddingModel = null,
        int? embeddingDimensions = null,
        IReadOnlyList<DocumentProcessingRun>? runs = null,
        Dictionary<Guid, IReadOnlyList<DocumentProcessingStep>>? steps = null,
        Guid? documentId = null,
        Guid? versionId = null)
    {
        var doc = Document.Create(documentId ?? Guid.NewGuid(), TenantId, "report.pdf", "report.pdf", UserId);
        Document? docToReturn = noDocument ? null : doc;

        // Create version and attach to document if preprocessed
        DocumentVersion? version = null;
        if (!noDocument)
        {
            version = doc.AddVersion(versionId ?? Guid.NewGuid(), 1, "tenants/t/doc/v/orig/report.pdf", "application/pdf", 1024, "abc");
            if (preprocessed)
            {
                version.AttachDoclingArtifacts("tenants/t/doc/v/md.md", "tenants/t/doc/v/json.json");
                version.MarkPreprocessed();
            }
        }

        var docRepo = new FakeDocumentRepository(docToReturn);
        var chunkRepo = new FakeChunkRepo(chunkCount);
        var embeddingRepo = new FakeEmbeddingRepo(embeddingCount, embeddingProvider, embeddingModel, embeddingDimensions);
        var runRepo = new FakeRunRepo(runs, steps);
        var tenantStub = new StubCurrentTenant(TenantId);

        return new AllFakes(docRepo, chunkRepo, embeddingRepo, runRepo, tenantStub, docToReturn!, version!);
    }

    private sealed record AllFakes(
        FakeDocumentRepository DocRepo,
        FakeChunkRepo ChunkRepo,
        FakeEmbeddingRepo EmbeddingRepo,
        FakeRunRepo RunRepo,
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

        public Task<DocumentListResult> ListAsync(Guid tid, int pn, int ps, string? sf, string? s, CancellationToken ct = default)
            => Task.FromResult(new DocumentListResult(Array.Empty<DocumentListItem>(), pn, ps, 0));

        public Task DeleteAsync(Document doc, CancellationToken ct = default) => Task.CompletedTask;
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

        public Task DeleteByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.CompletedTask;

        public Task<ChunkListResult> ListByVersionAsync(Guid tid, Guid did, Guid vid, int pn, int ps, string? s, string? st, int? pf, CancellationToken ct = default)
            => Task.FromResult(new ChunkListResult(Array.Empty<DocumentChunk>(), pn, ps, 0));

        public Task<DocumentChunk?> GetByIdForVersionAsync(Guid tid, Guid did, Guid vid, Guid cid, CancellationToken ct = default)
            => Task.FromResult<DocumentChunk?>(null);
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

        public Task DeleteByVersionAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default) => Task.CompletedTask;

        public Task<ChunkListResult> ListByVersionAsync(Guid tid, Guid did, Guid vid, int pn, int ps, string? s, string? st, int? pf, CancellationToken ct = default)
            => Task.FromResult(new ChunkListResult(Array.Empty<DocumentChunk>(), pn, ps, 0));

        public Task<DocumentChunk?> GetByIdForVersionAsync(Guid tid, Guid did, Guid vid, Guid cid, CancellationToken ct = default)
            => Task.FromResult<DocumentChunk?>(null);
    }

    private sealed class FakeRunRepo : IProcessingRunRepository
    {
        private readonly IReadOnlyList<DocumentProcessingRun> _runs;
        private readonly Dictionary<Guid, IReadOnlyList<DocumentProcessingStep>> _steps;

        public FakeRunRepo(
            IReadOnlyList<DocumentProcessingRun>? runs = null,
            Dictionary<Guid, IReadOnlyList<DocumentProcessingStep>>? steps = null)
        {
            _runs = runs ?? Array.Empty<DocumentProcessingRun>();
            _steps = steps ?? new Dictionary<Guid, IReadOnlyList<DocumentProcessingStep>>();
        }

        public Task AddAsync(DocumentProcessingRun run, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DocumentProcessingRun?> GetByIdAsync(Guid tid, Guid rid, CancellationToken ct = default) => Task.FromResult<DocumentProcessingRun?>(null);
        public Task<DocumentProcessingRun?> GetByIdForUpdateAsync(Guid tid, Guid rid, CancellationToken ct = default) => Task.FromResult<DocumentProcessingRun?>(null);
        public Task AddStepAsync(DocumentProcessingStep s, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DocumentProcessingStep?> GetStepAsync(Guid tid, Guid rid, DocumentProcessingStepName sn, CancellationToken ct = default) => Task.FromResult<DocumentProcessingStep?>(null);
        public Task<DocumentProcessingStep?> GetStepForUpdateAsync(Guid tid, Guid rid, DocumentProcessingStepName sn, CancellationToken ct = default) => Task.FromResult<DocumentProcessingStep?>(null);
        public Task<IReadOnlyList<DocumentProcessingRun>> GetRunsByDocumentAsync(Guid tid, Guid did, Guid vid, CancellationToken ct = default)
            => Task.FromResult(_runs);
        public Task<IReadOnlyList<DocumentProcessingStep>> GetStepsByRunAsync(Guid tid, Guid rid, CancellationToken ct = default)
            => Task.FromResult(_steps.TryGetValue(rid, out var s) ? s : Array.Empty<DocumentProcessingStep>());
    }
}
