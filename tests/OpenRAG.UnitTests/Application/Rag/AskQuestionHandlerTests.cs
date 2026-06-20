using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Vector;
using OpenRAG.Application.Common;
using OpenRAG.Application.Processing.GenerateEmbeddings;
using OpenRAG.Application.Rag;
using OpenRAG.Application.Rag.AskQuestion;

namespace OpenRAG.UnitTests.Application.Rag;

public sealed class AskQuestionHandlerTests
{
    [Fact]
    public async Task Rejects_empty_question()
    {
        var handler = CreateHandler();
        var query = new AskQuestionQuery(
            Question: "",
            TenantId: Guid.NewGuid(),
            FilterDocumentIds: null,
            TopK: 5,
            Model: "gpt-4",
            CorrelationId: "test-1");

        var ex = await Assert.ThrowsAsync<AppException>(
            () => handler.Handle(query).AsTask());

        Assert.Contains("Question", ex.Message);
    }

    [Fact]
    public async Task Rejects_whitespace_question()
    {
        var handler = CreateHandler();
        var query = new AskQuestionQuery(
            Question: "   ",
            TenantId: Guid.NewGuid(),
            FilterDocumentIds: null,
            TopK: 5,
            Model: "gpt-4",
            CorrelationId: "test-2");

        var ex = await Assert.ThrowsAsync<AppException>(
            () => handler.Handle(query).AsTask());

        Assert.Contains("Question", ex.Message);
    }

    [Fact]
    public async Task Rejects_zero_topk()
    {
        var handler = CreateHandler();
        var query = new AskQuestionQuery(
            Question: "What is RAG?",
            TenantId: Guid.NewGuid(),
            FilterDocumentIds: null,
            TopK: 0,
            Model: "gpt-4",
            CorrelationId: "test-3");

        var ex = await Assert.ThrowsAsync<AppException>(
            () => handler.Handle(query).AsTask());

        Assert.Contains("TopK", ex.Message);
    }

    [Fact]
    public async Task Rejects_negative_topk()
    {
        var handler = CreateHandler();
        var query = new AskQuestionQuery(
            Question: "What is RAG?",
            TenantId: Guid.NewGuid(),
            FilterDocumentIds: null,
            TopK: -1,
            Model: "gpt-4",
            CorrelationId: "test-4");

        var ex = await Assert.ThrowsAsync<AppException>(
            () => handler.Handle(query).AsTask());

        Assert.Contains("TopK", ex.Message);
    }

    [Fact]
    public async Task Rejects_empty_model()
    {
        var handler = CreateHandler();
        var query = new AskQuestionQuery(
            Question: "What is RAG?",
            TenantId: Guid.NewGuid(),
            FilterDocumentIds: null,
            TopK: 5,
            Model: "",
            CorrelationId: "test-5");

        var ex = await Assert.ThrowsAsync<AppException>(
            () => handler.Handle(query).AsTask());

        Assert.Contains("Model", ex.Message);
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

        var response = await handler.Handle(query);

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

        var response = await handler.Handle(query);

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

        var response = await handler.Handle(query);

        Assert.Contains("No indexed document embeddings", response.Answer);
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
    public async Task Passes_tenant_id_to_embedding_service()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var tenantId = Guid.NewGuid();
        var query = CreateValidQuery(tenantId: tenantId);

        await handler.Handle(query);

        Assert.NotNull(fakes.EmbeddingService.LastRequest);
        Assert.Equal(tenantId, fakes.EmbeddingService.LastRequest!.TenantId);
    }

    [Fact]
    public async Task Passes_tenant_id_to_vector_search()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var tenantId = Guid.NewGuid();
        var query = CreateValidQuery(tenantId: tenantId);

        await handler.Handle(query);

        Assert.NotNull(fakes.VectorSearchService.LastRequest);
        Assert.Equal(tenantId, fakes.VectorSearchService.LastRequest!.TenantId);
    }

    [Fact]
    public async Task Returns_chat_completion_answer()
    {
        var fakes = CreateFakes();
        var handler = CreateHandler(fakes);
        var query = CreateValidQuery();

        var response = await handler.Handle(query);

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

        var response = await handler.Handle(query);

        Assert.Contains("No indexed document embeddings", response.Answer);
    }

    [Fact]
    public async Task Returns_clear_message_when_model_mismatches()
    {
        var fakes = CreateFakes(hasResults: false);
        fakes.VectorSearchService.DiagnosticMessage =
            "Indexed embeddings exist (10 total), but none match the current query embedding: model=nomic-embed-text, dimensions=768.";
        var handler = CreateHandler(fakes);
        var query = CreateValidQuery();

        var response = await handler.Handle(query);

        Assert.Contains("none match", response.Answer);
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

    // ══ Helpers ═══════════════════════════════════════════════════

    private static AskQuestionQuery CreateValidQuery(Guid? tenantId = null)
        => new(
            Question: "What is RAG?",
            TenantId: tenantId ?? Guid.NewGuid(),
            FilterDocumentIds: null,
            TopK: 5,
            Model: "mock-chat",
            CorrelationId: "test-1");

    private static AskQuestionHandler CreateHandler(AllFakes? fakes = null)
    {
        fakes ??= CreateFakes();
        var embeddingOptions = Options.Create(new GenerateEmbeddingsOptions { Model = "mock-embedding-8" });
        var ragOptions = Options.Create(new RagOptions { TopK = 5 });
        return new AskQuestionHandler(
            fakes.Tenant, fakes.EmbeddingService, fakes.VectorSearchService, fakes.ChatCompletionService,
            embeddingOptions, ragOptions);
    }

    private static AllFakes CreateFakes(bool hasResults = true)
    {
        var tenant = new StubCurrentTenant(Guid.NewGuid());
        var embeddings = new FakeEmbeddingService();
        var vectorSearch = new FakeVectorSearchService(hasResults);
        var chat = new FakeChatCompletionService();

        return new AllFakes(tenant, embeddings, vectorSearch, chat);
    }

    private sealed record AllFakes(
        StubCurrentTenant Tenant,
        FakeEmbeddingService EmbeddingService,
        FakeVectorSearchService VectorSearchService,
        FakeChatCompletionService ChatCompletionService);

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public StubCurrentTenant(Guid tenantId) => TenantId = tenantId;
        public Guid TenantId { get; }
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

            return Task.FromResult(new VectorSearchResponse(new[]
            {
                new VectorSearchResultItem(
                    ChunkId: Guid.NewGuid(), DocumentId: Guid.NewGuid(), VersionId: Guid.NewGuid(),
                    "Retrieved chunk 1 content about RAG.", 1, "Introduction", 0.85),
                new VectorSearchResultItem(
                    ChunkId: Guid.NewGuid(), DocumentId: Guid.NewGuid(), VersionId: Guid.NewGuid(),
                    "Retrieved chunk 2 content about retrieval.", 2, "Methodology", 0.72)
            }, 5, 5, null));
        }
    }

    private sealed class FakeChatCompletionService : IChatCompletionService
    {
        public bool Called { get; private set; }

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request, CancellationToken ct = default)
        {
            Called = true;
            return Task.FromResult(new ChatCompletionResult(
                Content: "Mock answer: RAG stands for Retrieval-Augmented Generation.",
                Provider: "mock",
                Model: request.Model,
                InputTokens: 100,
                OutputTokens: 50));
        }
    }
}
