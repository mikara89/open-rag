using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Vector;
using OpenRAG.Application.Common;
using OpenRAG.Application.Processing.GenerateEmbeddings;
using OpenRAG.Application.Rag;
using OpenRAG.Application.Rag.AskQuestion;

namespace OpenRAG.UnitTests.Application.Rag;

public sealed class AskQuestionHandlerTests
{
    private static readonly Guid TenantA = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Rejects_empty_question()
    {
        var handler = CreateHandler();
        var query = new AskQuestionQuery(
            Question: "",
            FilterDocumentIds: null,
            TopK: 5,
            Model: "gpt-4",
            CorrelationId: "test-1");

        var result = await handler.Handle(query);

        Assert.Equal("request.question_required", result.PrimaryError.Code);
    }

    [Fact]
    public async Task Rejects_whitespace_question()
    {
        var handler = CreateHandler();
        var query = new AskQuestionQuery(
            Question: "   ",
            FilterDocumentIds: null,
            TopK: 5,
            Model: "gpt-4",
            CorrelationId: "test-2");

        var result = await handler.Handle(query);

        Assert.Equal("request.question_required", result.PrimaryError.Code);
    }

    [Fact]
    public async Task Rejects_zero_topk()
    {
        var handler = CreateHandler();
        var query = new AskQuestionQuery(
            Question: "What is RAG?",
            FilterDocumentIds: null,
            TopK: 0,
            Model: "gpt-4",
            CorrelationId: "test-3");

        var result = await handler.Handle(query);

        Assert.Equal("request.top_k_invalid", result.PrimaryError.Code);
    }

    [Fact]
    public async Task Rejects_negative_topk()
    {
        var handler = CreateHandler();
        var query = new AskQuestionQuery(
            Question: "What is RAG?",
            FilterDocumentIds: null,
            TopK: -1,
            Model: "gpt-4",
            CorrelationId: "test-4");

        var result = await handler.Handle(query);

        Assert.Equal("request.top_k_invalid", result.PrimaryError.Code);
    }

    [Fact]
    public async Task Rejects_empty_model()
    {
        var handler = CreateHandler();
        var query = new AskQuestionQuery(
            Question: "What is RAG?",
            FilterDocumentIds: null,
            TopK: 5,
            Model: "",
            CorrelationId: "test-5");

        var result = await handler.Handle(query);

        Assert.Equal("request.model_required", result.PrimaryError.Code);
    }

    [Fact]
    public async Task Calls_embedding_service()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var query = CreateValidQuery();

        await handler.Handle(query);

        Assert.True(fakes.EmbeddingService.Called);
    }

    [Fact]
    public async Task Calls_vector_search()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var query = CreateValidQuery();

        await handler.Handle(query);

        Assert.True(fakes.VectorSearchService.Called);
    }

    [Fact]
    public async Task Calls_chat_completion_when_chunks_found()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var query = CreateValidQuery();

        await handler.Handle(query);

        Assert.True(fakes.ChatCompletionService.Called);
    }

    [Fact]
    public async Task Returns_citations()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var query = CreateValidQuery();

        var response = (await handler.Handle(query)).Value;

        Assert.NotEmpty(response.Citations);
        Assert.All(response.Citations, c =>
        {
            Assert.True(c.Index > 0);
            Assert.NotEqual(Guid.Empty, c.DocumentId);
            Assert.NotEqual(Guid.Empty, c.ChunkId);
            Assert.False(string.IsNullOrWhiteSpace(c.Excerpt));
        });
    }

    [Fact]
    public async Task Returns_retrieved_chunks()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var query = CreateValidQuery();

        var response = (await handler.Handle(query)).Value;

        Assert.NotEmpty(response.RetrievedChunks);
        Assert.All(response.RetrievedChunks, c =>
        {
            Assert.NotEqual(Guid.Empty, c.ChunkId);
            Assert.NotEqual(Guid.Empty, c.DocumentId);
            Assert.True(c.Score >= 0);
        });
    }

    [Fact]
    public async Task Returns_safe_no_results_answer_when_no_chunks_found()
    {
        var fakes = CreateFakes(hasResults: false);
        fakes.VectorSearchService.DiagnosticMessage = "No indexed document embeddings were found for this tenant.";
        var handler = CreateHandler(fakes);
        var query = CreateValidQuery();

        var response = (await handler.Handle(query)).Value;

        Assert.Equal("I could not find relevant information in the indexed documents.", response.Answer);
        Assert.Empty(response.Citations);
        Assert.Empty(response.RetrievedChunks);
    }

    [Fact]
    public async Task Does_not_call_chat_completion_when_no_chunks_found()
    {
        var fakes = CreateFakes(hasResults: false);
        var handler = CreateHandler(fakes);
        var query = CreateValidQuery();

        await handler.Handle(query);

        Assert.False(fakes.ChatCompletionService.Called);
    }

    [Fact]
    public async Task Passes_current_tenant_to_all_rag_dependencies()
    {
        var fakes = CreateFakes(tenantId: TenantA);
        var handler = CreateHandler(fakes);
        var query = CreateValidQuery();

        await handler.Handle(query);

        Assert.NotNull(fakes.EmbeddingService.LastRequest);
        Assert.NotNull(fakes.VectorSearchService.LastRequest);
        Assert.NotNull(fakes.ChatCompletionService.LastRequest);
        Assert.Equal(TenantA, fakes.EmbeddingService.LastRequest!.TenantId);
        Assert.Equal(TenantA, fakes.VectorSearchService.LastRequest!.TenantId);
        Assert.Equal(TenantA, fakes.ChatCompletionService.LastRequest!.TenantId);
    }

    [Fact]
    public async Task Returns_chat_completion_answer()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var query = CreateValidQuery();

        var response = (await handler.Handle(query)).Value;

        Assert.False(string.IsNullOrWhiteSpace(response.Answer));
        Assert.Equal("mock-chat", response.Model);
    }

    [Fact]
    public async Task Returns_clear_message_when_no_embeddings_exist()
    {
        var fakes = CreateFakes(hasResults: false);
        // Set a specific diagnostic message
        fakes.VectorSearchService.DiagnosticMessage = "No indexed document embeddings were found for this tenant.";
        var handler = CreateHandler(fakes);
        var query = CreateValidQuery();

        var response = (await handler.Handle(query)).Value;

        Assert.Equal("I could not find relevant information in the indexed documents.", response.Answer);
    }

    [Fact]
    public async Task Returns_clear_message_when_model_mismatches()
    {
        var fakes = CreateFakes(hasResults: false);
        fakes.VectorSearchService.DiagnosticMessage =
            "Indexed embeddings exist (10 total), but none match the current query embedding: model=nomic-embed-text, dimensions=768.";
        var handler = CreateHandler(fakes);
        var query = CreateValidQuery();

        var response = (await handler.Handle(query)).Value;

        Assert.Equal("I could not find relevant information in the indexed documents.", response.Answer);
    }

    [Fact]
    public async Task Passes_embedding_provider_model_dimensions_to_vector_search()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var query = CreateValidQuery();

        await handler.Handle(query);

        Assert.NotNull(fakes.VectorSearchService.LastRequest);
        Assert.Equal("mock", fakes.VectorSearchService.LastRequest!.EmbeddingProvider);
        Assert.Equal("mock-embedding-8", fakes.VectorSearchService.LastRequest!.EmbeddingModel);
        Assert.Equal(8, fakes.VectorSearchService.LastRequest!.EmbeddingDimensions);
        Assert.Equal("v1", fakes.VectorSearchService.LastRequest!.EmbeddingVersion);
    }

    [Fact]
    public async Task Does_not_call_chat_completion_when_no_compatible_embeddings()
    {
        var fakes = CreateFakes(hasResults: false);
        var handler = CreateHandler(fakes);
        var query = CreateValidQuery();

        await handler.Handle(query);

        Assert.False(fakes.ChatCompletionService.Called);
    }

    [Fact]
    public async Task Authorizes_normalized_document_filter_before_embedding()
    {
        var documentId = Guid.NewGuid();
        var fakes = CreateFakes();
        fakes.VectorSearchService.Results = [CreateResult(documentId: documentId)];
        var query = CreateValidQuery() with { FilterDocumentIds = [documentId, documentId] };

        await CreateHandler(fakes).Handle(query);

        Assert.True(fakes.DocumentAuthorizationRepository.Called);
        Assert.Equal(TenantA, fakes.DocumentAuthorizationRepository.LastTenantId);
        Assert.Equal([documentId], fakes.DocumentAuthorizationRepository.LastDocumentIds);
        Assert.Equal([documentId], fakes.VectorSearchService.LastRequest?.DocumentIds);
        Assert.True(fakes.EmbeddingService.Called);
    }

    [Fact]
    public async Task Missing_or_foreign_filter_fails_identically_before_AI_work()
    {
        var requestedId = Guid.NewGuid();
        var missing = CreateFakes();
        missing.DocumentAuthorizationRepository.ExistingIds = new HashSet<Guid>();
        var foreign = CreateFakes();
        foreign.DocumentAuthorizationRepository.ExistingIds = new HashSet<Guid>();
        var query = CreateValidQuery() with { FilterDocumentIds = [requestedId] };

        var missingResult = await CreateHandler(missing).Handle(query);
        var foreignResult = await CreateHandler(foreign).Handle(query);

        Assert.True(missingResult.IsFailure);
        Assert.True(foreignResult.IsFailure);
        Assert.Equal(missingResult.Errors, foreignResult.Errors);
        Assert.Equal("resource.not_found", missingResult.PrimaryError.Code);
        Assert.False(missing.EmbeddingService.Called);
        Assert.False(missing.VectorSearchService.Called);
        Assert.False(missing.ChatCompletionService.Called);
        Assert.False(foreign.EmbeddingService.Called);
        Assert.False(foreign.VectorSearchService.Called);
        Assert.False(foreign.ChatCompletionService.Called);
    }

    [Fact]
    public async Task Rejects_empty_or_oversized_filter_before_AI_work()
    {
        var emptyFakes = CreateFakes();
        var oversizedFakes = CreateFakes();
        var oversized = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();

        var emptyResult = await CreateHandler(emptyFakes).Handle(
            CreateValidQuery() with { FilterDocumentIds = [Guid.Empty] });
        var oversizedResult = await CreateHandler(oversizedFakes).Handle(
            CreateValidQuery() with { FilterDocumentIds = oversized });

        Assert.Equal("request.document_filter_invalid", emptyResult.PrimaryError.Code);
        Assert.Equal("request.document_filter_invalid", oversizedResult.PrimaryError.Code);
        Assert.False(emptyFakes.EmbeddingService.Called);
        Assert.False(oversizedFakes.EmbeddingService.Called);
    }

    [Theory]
    [InlineData("foreign-tenant")]
    [InlineData("outside-filter")]
    [InlineData("empty-id")]
    [InlineData("duplicate")]
    public async Task Invalid_vector_results_fail_closed_before_chat(string scenario)
    {
        var authorizedDocumentId = Guid.NewGuid();
        var fakes = CreateFakes();
        var valid = CreateResult(documentId: authorizedDocumentId);
        fakes.VectorSearchService.Results = scenario switch
        {
            "foreign-tenant" => [valid with { TenantId = Guid.NewGuid() }],
            "outside-filter" => [valid with { DocumentId = Guid.NewGuid() }],
            "empty-id" => [valid with { ChunkId = Guid.Empty }],
            "duplicate" => [valid, valid],
            _ => throw new InvalidOperationException()
        };
        var query = CreateValidQuery() with { FilterDocumentIds = [authorizedDocumentId] };

        await Assert.ThrowsAsync<IsolationViolationException>(() =>
            CreateHandler(fakes).Handle(query).AsTask());

        Assert.False(fakes.ChatCompletionService.Called);
    }

    [Fact]
    public async Task Only_validated_chunk_content_reaches_prompt_and_public_results()
    {
        var documentId = Guid.NewGuid();
        var result = CreateResult(documentId: documentId, content: "authorized context only");
        var fakes = CreateFakes();
        fakes.VectorSearchService.Results = [result];

        var response = (await CreateHandler(fakes).Handle(
            CreateValidQuery() with { FilterDocumentIds = [documentId] })).Value;

        var prompt = Assert.Single(fakes.ChatCompletionService.LastRequest!.Messages, m => m.Role == "user");
        Assert.Contains("authorized context only", prompt.Content, StringComparison.Ordinal);
        Assert.Equal("authorized context only", Assert.Single(response.RetrievedChunks).Content);
        Assert.Equal("authorized context only", Assert.Single(response.Citations).Excerpt);
    }

    // ══ Helpers ═══════════════════════════════════════════════════

    private static AskQuestionQuery CreateValidQuery()
        => new(
            Question: "What is RAG?",
            FilterDocumentIds: null,
            TopK: 5,
            Model: "mock-chat",
            CorrelationId: "test-1");

    private static VectorSearchResultItem CreateResult(Guid? documentId = null, string content = "validated") =>
        new(
            TenantId: TenantA,
            ChunkId: Guid.NewGuid(),
            DocumentId: documentId ?? Guid.NewGuid(),
            VersionId: Guid.NewGuid(),
            Content: content,
            PageNumber: 1,
            SectionTitle: "Section",
            Score: 0.9);

    private static AskQuestionHandler CreateHandler(AllFakes? fakes = null)
    {
        fakes ??= CreateFakes();
        var embeddingOptions = Options.Create(new GenerateEmbeddingsOptions { Model = "mock-embedding-8" });
        var ragOptions = Options.Create(new RagOptions { TopK = 5 });
        return new AskQuestionHandler(
            fakes.Tenant,
            fakes.DocumentAuthorizationRepository,
            fakes.EmbeddingService,
            fakes.VectorSearchService,
            fakes.ChatCompletionService,
            embeddingOptions,
            ragOptions,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AskQuestionHandler>.Instance);
    }

    private static AllFakes CreateFakes(bool hasResults = true, Guid? tenantId = null)
    {
        var tenant = new StubCurrentTenant(tenantId ?? TenantA);
        var authorization = new FakeDocumentAuthorizationRepository();
        var embeddings = new FakeEmbeddingService();
        var vectorSearch = new FakeVectorSearchService(hasResults);
        var chat = new FakeChatCompletionService();

        return new AllFakes(tenant, authorization, embeddings, vectorSearch, chat);
    }

    private sealed record AllFakes(
        StubCurrentTenant Tenant,
        FakeDocumentAuthorizationRepository DocumentAuthorizationRepository,
        FakeEmbeddingService EmbeddingService,
        FakeVectorSearchService VectorSearchService,
        FakeChatCompletionService ChatCompletionService);

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public StubCurrentTenant(Guid tenantId) => TenantId = tenantId;
        public Guid TenantId { get; }
    }

    private sealed class FakeDocumentAuthorizationRepository : IDocumentAuthorizationRepository
    {
        public bool Called { get; private set; }
        public Guid? LastTenantId { get; private set; }
        public IReadOnlyCollection<Guid>? LastDocumentIds { get; private set; }
        public IReadOnlySet<Guid>? ExistingIds { get; set; }

        public Task<IReadOnlySet<Guid>> GetExistingIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> documentIds,
            CancellationToken cancellationToken = default)
        {
            Called = true;
            LastTenantId = tenantId;
            LastDocumentIds = documentIds;
            return Task.FromResult(
                ExistingIds ?? (IReadOnlySet<Guid>)documentIds.ToHashSet());
        }
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public bool Called { get; private set; }
        public EmbeddingRequest? LastRequest { get; private set; }

        public Task<EmbeddingResult> GenerateEmbeddingAsync(
            EmbeddingRequest request, CancellationToken ct = default)
        {
            Called = true;
            LastRequest = request;
            var vec = Enumerable.Repeat(0.125f, 8).ToList().AsReadOnly();
            return Task.FromResult(new EmbeddingResult(vec, "mock", "mock-embedding-8", 8, "v1"));
        }
    }

    private sealed class FakeVectorSearchService : IVectorSearchService
    {
        private readonly bool _hasResults;
        public bool Called { get; private set; }
        public VectorSearchRequest? LastRequest { get; private set; }
        public string? DiagnosticMessage { get; set; }
        public IReadOnlyList<VectorSearchResultItem>? Results { get; set; }

        public FakeVectorSearchService(bool hasResults = true) => _hasResults = hasResults;

        public Task<VectorSearchResponse> SearchAsync(
            VectorSearchRequest request, CancellationToken ct = default)
        {
            Called = true;
            LastRequest = request;

            if (!_hasResults)
                return Task.FromResult(new VectorSearchResponse(
                    Array.Empty<VectorSearchResultItem>(), 0, 0,
                    DiagnosticMessage ?? "No indexed document embeddings were found for this tenant."));

            return Task.FromResult(new VectorSearchResponse(Results ?? new[]
            {
                new VectorSearchResultItem(
                    TenantId: TenantA,
                    ChunkId: Guid.NewGuid(),
                    DocumentId: Guid.NewGuid(),
                    VersionId: Guid.NewGuid(),
                    Content: "Retrieved chunk 1 content about RAG.",
                    PageNumber: 1,
                    SectionTitle: "Introduction",
                    Score: 0.85),
                new VectorSearchResultItem(
                    TenantId: TenantA,
                    ChunkId: Guid.NewGuid(),
                    DocumentId: Guid.NewGuid(),
                    VersionId: Guid.NewGuid(),
                    Content: "Retrieved chunk 2 content about retrieval.",
                    PageNumber: 2,
                    SectionTitle: "Methodology",
                    Score: 0.72)
            }, 5, 5, null));
        }
    }

    private sealed class FakeChatCompletionService : IChatCompletionService
    {
        public bool Called { get; private set; }
        public ChatCompletionRequest? LastRequest { get; private set; }

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken ct = default)
        {
            Called = true;
            LastRequest = request;
            return Task.FromResult(new ChatCompletionResult(
                Content: "Mock answer: RAG stands for Retrieval-Augmented Generation.",
                Provider: "mock",
                Model: request.Model,
                InputTokens: 100,
                OutputTokens: 50));
        }
    }
}
