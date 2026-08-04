using System.Collections.Concurrent;
using System.Text;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Processing;
using OpenRAG.Application.Abstractions.Storage;

namespace OpenRAG.LiveIntegrationTests.Infrastructure;

internal sealed class LiveProviderProbe
{
    private readonly ConcurrentQueue<EmbeddingRequest> _embeddingRequests = new();
    private readonly ConcurrentQueue<ChatCompletionRequest> _chatRequests = new();
    private readonly ConcurrentQueue<DocumentIntelligenceRequest> _intelligenceRequests = new();
    private readonly ConcurrentQueue<DocumentPreprocessingRequest> _preprocessingRequests = new();

    public IReadOnlyCollection<EmbeddingRequest> EmbeddingRequests => _embeddingRequests.ToArray();
    public IReadOnlyCollection<ChatCompletionRequest> ChatRequests => _chatRequests.ToArray();
    public IReadOnlyCollection<DocumentIntelligenceRequest> IntelligenceRequests =>
        _intelligenceRequests.ToArray();
    public IReadOnlyCollection<DocumentPreprocessingRequest> PreprocessingRequests =>
        _preprocessingRequests.ToArray();

    public Exception? EmbeddingFailure { get; set; }
    public Exception? ChatFailure { get; set; }
    public Exception? IntelligenceFailure { get; set; }
    public Exception? PreprocessingFailure { get; set; }

    public void Record(EmbeddingRequest request) => _embeddingRequests.Enqueue(request);
    public void Record(ChatCompletionRequest request) => _chatRequests.Enqueue(request);
    public void Record(DocumentIntelligenceRequest request) => _intelligenceRequests.Enqueue(request);
    public void Record(DocumentPreprocessingRequest request) => _preprocessingRequests.Enqueue(request);

    public void Reset()
    {
        _embeddingRequests.Clear();
        _chatRequests.Clear();
        _intelligenceRequests.Clear();
        _preprocessingRequests.Clear();
        EmbeddingFailure = null;
        ChatFailure = null;
        IntelligenceFailure = null;
        PreprocessingFailure = null;
    }
}

internal sealed class DeterministicEmbeddingService : IEmbeddingService
{
    private readonly LiveProviderProbe _probe;

    public DeterministicEmbeddingService(LiveProviderProbe probe)
    {
        _probe = probe;
    }

    public Task<EmbeddingResult> GenerateEmbeddingAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _probe.Record(request);
        if (_probe.EmbeddingFailure is not null)
            return Task.FromException<EmbeddingResult>(_probe.EmbeddingFailure);

        var vector = request.TenantId == LiveTestConstants.TenantA
            ? new[] { 1f, 0f, 0f }
            : new[] { 0f, 1f, 0f };
        return Task.FromResult(new EmbeddingResult(
            vector,
            LiveTestConstants.EmbeddingProvider,
            LiveTestConstants.EmbeddingModel,
            LiveTestConstants.EmbeddingDimensions,
            LiveTestConstants.EmbeddingVersion));
    }
}

internal sealed class DeterministicChatCompletionService : IChatCompletionService
{
    private readonly LiveProviderProbe _probe;

    public DeterministicChatCompletionService(LiveProviderProbe probe)
    {
        _probe = probe;
    }

    public Task<ChatCompletionResult> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _probe.Record(request);
        if (_probe.ChatFailure is not null)
            return Task.FromException<ChatCompletionResult>(_probe.ChatFailure);

        var tenantMarker = request.TenantId == LiveTestConstants.TenantA
            ? LiveTestConstants.TenantAMarker
            : LiveTestConstants.TenantBMarker;
        return Task.FromResult(new ChatCompletionResult(
            $"Grounded answer for {tenantMarker}",
            "live-deterministic",
            request.Model,
            1,
            1));
    }
}

internal sealed class DeterministicIntelligenceService : IDocumentIntelligenceService
{
    private readonly LiveProviderProbe _probe;

    public DeterministicIntelligenceService(LiveProviderProbe probe)
    {
        _probe = probe;
    }

    public Task<DocumentIntelligenceResult> GenerateAsync(
        DocumentIntelligenceRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _probe.Record(request);
        if (_probe.IntelligenceFailure is not null)
            return Task.FromException<DocumentIntelligenceResult>(_probe.IntelligenceFailure);

        var marker = request.TenantId == LiveTestConstants.TenantA
            ? LiveTestConstants.TenantAMarker
            : LiveTestConstants.TenantBMarker;
        return Task.FromResult(new DocumentIntelligenceResult(
            "live-test",
            $"Summary for {marker}",
            ["deterministic"],
            [],
            new Dictionary<string, string>(),
            "live-deterministic",
            "live-intelligence"));
    }
}

internal sealed class DeterministicDocumentPreprocessor : IDocumentPreprocessor
{
    private readonly IFileStorage _storage;
    private readonly IDocumentObjectKeyPolicy _objectKeys;
    private readonly LiveProviderProbe _probe;

    public DeterministicDocumentPreprocessor(
        IFileStorage storage,
        IDocumentObjectKeyPolicy objectKeys,
        LiveProviderProbe probe)
    {
        _storage = storage;
        _objectKeys = objectKeys;
        _probe = probe;
    }

    public async Task<DocumentPreprocessingResult> PreprocessAsync(
        DocumentPreprocessingRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _probe.Record(request);
        if (_probe.PreprocessingFailure is not null)
            throw _probe.PreprocessingFailure;

        await using (var source = await _storage.OpenReadAsync(
                         request.OriginalObjectKey,
                         cancellationToken))
        {
            _ = source.Length;
        }

        var marker = request.TenantId == LiveTestConstants.TenantA
            ? LiveTestConstants.TenantAMarker
            : LiveTestConstants.TenantBMarker;
        var markdownKey = _objectKeys.BuildArtifactKey(
            request.TenantId,
            request.DocumentId,
            request.VersionId,
            DocumentObjectKind.Markdown);
        var jsonKey = _objectKeys.BuildArtifactKey(
            request.TenantId,
            request.DocumentId,
            request.VersionId,
            DocumentObjectKind.Json);

        var markdown = Encoding.UTF8.GetBytes($"# Deterministic artifact\n\n{marker}");
        var json = Encoding.UTF8.GetBytes($"{{\"marker\":\"{marker}\"}}");
        await using var markdownStream = new MemoryStream(markdown);
        await using var jsonStream = new MemoryStream(json);
        var markdownResult = await _storage.SaveAsync(
            markdownStream,
            markdownKey,
            "text/markdown",
            cancellationToken);
        var jsonResult = await _storage.SaveAsync(
            jsonStream,
            jsonKey,
            "application/json",
            cancellationToken);

        return new DocumentPreprocessingResult(
            markdownKey,
            jsonKey,
            markdownResult.Sha256 ?? throw new InvalidOperationException("Markdown artifact hash was not returned."),
            jsonResult.Sha256 ?? throw new InvalidOperationException("JSON artifact hash was not returned."));
    }
}

internal sealed record PublishedDocumentEvent(string Topic, object Message);

internal sealed class CapturingDocumentEventBus : IDocumentEventBus
{
    private readonly ConcurrentQueue<PublishedDocumentEvent> _events = new();

    public IReadOnlyCollection<PublishedDocumentEvent> Events => _events.ToArray();
    public Exception? Failure { get; set; }

    public Task PublishAsync<TEvent>(
        string topic,
        TEvent message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is not null)
            return Task.FromException(Failure);

        _events.Enqueue(new PublishedDocumentEvent(topic, message!));
        return Task.CompletedTask;
    }

    public void Reset()
    {
        _events.Clear();
        Failure = null;
    }
}
