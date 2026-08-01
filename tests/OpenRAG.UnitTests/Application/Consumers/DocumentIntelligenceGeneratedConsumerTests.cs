using Microsoft.Extensions.Logging;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Worker.Consumers;

namespace OpenRAG.UnitTests.Application.Consumers;

public sealed class DocumentIntelligenceGeneratedConsumerTests
{
    [Fact]
    public async Task Forwards_event_tenant_to_embeddings_request()
    {
        var eventBus = new FakeDocumentEventBus();
        var consumer = new DocumentIntelligenceGeneratedConsumer(
            eventBus,
            new StubClock(),
            new FakeLogger<DocumentIntelligenceGeneratedConsumer>());
        var message = new DocumentIntelligenceGeneratedEvent(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "test", "test", "corr", DateTimeOffset.UtcNow);

        await consumer.HandleAsync(message, CancellationToken.None);

        Assert.Equal("document.embeddings.requested", eventBus.LastTopic);
        var forwarded = Assert.IsType<DocumentEmbeddingsRequestedEvent>(eventBus.LastEvent);
        Assert.Equal(message.TenantId, forwarded.TenantId);
        Assert.Equal(message.DocumentId, forwarded.DocumentId);
        Assert.Equal(message.VersionId, forwarded.VersionId);
        Assert.Equal(message.ProcessingRunId, forwarded.ProcessingRunId);
        Assert.Equal(message.CorrelationId, forwarded.CorrelationId);
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
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
