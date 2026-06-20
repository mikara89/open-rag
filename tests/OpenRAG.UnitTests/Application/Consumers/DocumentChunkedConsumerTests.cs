using Microsoft.Extensions.Logging;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Worker.Consumers;

namespace OpenRAG.UnitTests.Application.Consumers;

public sealed class DocumentChunkedConsumerTests
{
    [Fact]
    public async Task Publishes_DocumentEmbeddingsRequestedEvent()
    {
        var eventBus = new FakeDocumentEventBus();
        var clock = new StubClock();
        var logger = new FakeLogger<DocumentChunkedConsumer>();
        var consumer = new DocumentChunkedConsumer(eventBus, clock, logger);

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
    public async Task Forwards_correlation_id()
    {
        var eventBus = new FakeDocumentEventBus();
        var clock = new StubClock();
        var logger = new FakeLogger<DocumentChunkedConsumer>();
        var consumer = new DocumentChunkedConsumer(eventBus, clock, logger);

        var message = new DocumentChunkedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            3, "my-correlation-id", DateTimeOffset.UtcNow);

        await consumer.HandleAsync(message, CancellationToken.None);

        var embeddingsEvent = Assert.IsType<DocumentEmbeddingsRequestedEvent>(eventBus.LastEvent);
        Assert.Equal("my-correlation-id", embeddingsEvent.CorrelationId);
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
