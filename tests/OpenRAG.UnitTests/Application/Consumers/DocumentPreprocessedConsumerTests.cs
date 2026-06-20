using Mediator;
using Microsoft.Extensions.Logging;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Worker.Consumers;

namespace OpenRAG.UnitTests.Application.Consumers;

public sealed class DocumentPreprocessedConsumerTests
{
    [Fact]
    public async Task Publishes_DocumentChunkingRequestedEvent()
    {
        var eventBus = new FakeDocumentEventBus();
        var clock = new StubClock();
        var logger = new FakeLogger<DocumentPreprocessedConsumer>();
        var consumer = new DocumentPreprocessedConsumer(eventBus, clock, logger);

        var message = new DocumentPreprocessedEvent(
            TenantId: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            VersionId: Guid.NewGuid(),
            ProcessingRunId: Guid.NewGuid(),
            MarkdownObjectKey: "tenants/t/doc/v/md.md",
            JsonObjectKey: "tenants/t/doc/v/json.json",
            MarkdownSha256: "abc123",
            JsonSha256: "def456",
            CorrelationId: "corr-1",
            OccurredAt: DateTimeOffset.UtcNow);

        await consumer.HandleAsync(message, CancellationToken.None);

        Assert.Equal("document.chunking.requested", eventBus.LastTopic);
        Assert.NotNull(eventBus.LastEvent);

        var chunkingEvent = Assert.IsType<DocumentChunkingRequestedEvent>(eventBus.LastEvent);
        Assert.Equal(message.DocumentId, chunkingEvent.DocumentId);
        Assert.Equal(message.VersionId, chunkingEvent.VersionId);
        Assert.Equal(message.MarkdownObjectKey, chunkingEvent.MarkdownObjectKey);
    }

    [Fact]
    public async Task Forwards_correlation_id_to_chunking_event()
    {
        var eventBus = new FakeDocumentEventBus();
        var clock = new StubClock();
        var logger = new FakeLogger<DocumentPreprocessedConsumer>();
        var consumer = new DocumentPreprocessedConsumer(eventBus, clock, logger);

        var message = new DocumentPreprocessedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "key", "json-key", "md-hash", "json-hash",
            "my-correlation-id", DateTimeOffset.UtcNow);

        await consumer.HandleAsync(message, CancellationToken.None);

        var chunkingEvent = Assert.IsType<DocumentChunkingRequestedEvent>(eventBus.LastEvent);
        Assert.Equal("my-correlation-id", chunkingEvent.CorrelationId);
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
