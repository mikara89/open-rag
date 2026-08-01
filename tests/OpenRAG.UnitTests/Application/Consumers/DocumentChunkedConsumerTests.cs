using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Application.Processing.GenerateIntelligence;
using OpenRAG.Worker.Consumers;

namespace OpenRAG.UnitTests.Application.Consumers;

public sealed class DocumentChunkedConsumerTests
{
    private static readonly GenerateIntelligenceOptions DefaultOptions = new()
    {
        Enabled = false // Default to disabled so existing tests expect embeddings.requested
    };

    [Fact]
    public async Task Publishes_DocumentEmbeddingsRequestedEvent_when_intelligence_disabled()
    {
        var eventBus = new FakeDocumentEventBus();
        var clock = new StubClock();
        var options = Options.Create(DefaultOptions);
        var logger = new FakeLogger<DocumentChunkedConsumer>();
        var consumer = new DocumentChunkedConsumer(eventBus, clock, options, logger);

        var message = new DocumentChunkedEvent(
            TenantId: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            VersionId: Guid.NewGuid(),
            ProcessingRunId: Guid.NewGuid(),
            ChunkCount: 5,
            CorrelationId: "corr-1",
            OccurredAt: DateTimeOffset.UtcNow);

        await consumer.HandleAsync(message, CancellationToken.None);

        Assert.Equal("document.embeddings.requested", eventBus.LastTopic);
        Assert.NotNull(eventBus.LastEvent);

        var embeddingsEvent = Assert.IsType<DocumentEmbeddingsRequestedEvent>(eventBus.LastEvent);
        Assert.Equal(message.DocumentId, embeddingsEvent.DocumentId);
        Assert.Equal(message.VersionId, embeddingsEvent.VersionId);
        Assert.Equal(message.ProcessingRunId, embeddingsEvent.ProcessingRunId);
    }

    [Fact]
    public async Task Forwards_correlation_id_when_intelligence_disabled()
    {
        var eventBus = new FakeDocumentEventBus();
        var clock = new StubClock();
        var options = Options.Create(DefaultOptions);
        var logger = new FakeLogger<DocumentChunkedConsumer>();
        var consumer = new DocumentChunkedConsumer(eventBus, clock, options, logger);

        var message = new DocumentChunkedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            3, "my-correlation-id", DateTimeOffset.UtcNow);

        await consumer.HandleAsync(message, CancellationToken.None);

        var embeddingsEvent = Assert.IsType<DocumentEmbeddingsRequestedEvent>(eventBus.LastEvent);
        Assert.Equal("my-correlation-id", embeddingsEvent.CorrelationId);
    }

    [Fact]
    public async Task Publishes_IntelligenceRequestedEvent_when_intelligence_enabled()
    {
        var eventBus = new FakeDocumentEventBus();
        var clock = new StubClock();
        var options = Options.Create(new GenerateIntelligenceOptions { Enabled = true });
        var logger = new FakeLogger<DocumentChunkedConsumer>();
        var consumer = new DocumentChunkedConsumer(eventBus, clock, options, logger);

        var message = new DocumentChunkedEvent(
            TenantId: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            VersionId: Guid.NewGuid(),
            ProcessingRunId: Guid.NewGuid(),
            ChunkCount: 5,
            CorrelationId: "corr-1",
            OccurredAt: DateTimeOffset.UtcNow);

        await consumer.HandleAsync(message, CancellationToken.None);

        Assert.Equal("document.intelligence.requested", eventBus.LastTopic);
        Assert.IsType<DocumentIntelligenceRequestedEvent>(eventBus.LastEvent);
    }

    private sealed class FakeDocumentEventBus : IDocumentEventBus
    {
        public string? LastTopic { get; private set; }
        public object? LastEvent { get; private set; }

        public Task PublishAsync<TEvent>(string topic, TEvent message, CancellationToken ct = default)
        {
            LastTopic = topic;
            LastEvent = message;
            return Task.CompletedTask;
        }
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class FakeLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null!;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
