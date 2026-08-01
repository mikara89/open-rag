using Microsoft.Extensions.Logging;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Worker.Consumers;

namespace OpenRAG.UnitTests.Application.Consumers;

public sealed class DocumentUploadedConsumerTests
{
    [Fact]
    public async Task Publishes_DocumentPreprocessRequestedEvent()
    {
        var eventBus = new FakeDocumentEventBus();
        var clock = new StubClock();
        var logger = new FakeLogger<DocumentUploadedConsumer>();
        var consumer = new DocumentUploadedConsumer(eventBus, clock, logger);

        var message = new DocumentUploadedEvent(
            TenantId: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            VersionId: Guid.NewGuid(),
            ProcessingRunId: Guid.NewGuid(),
            OriginalObjectKey: "tenants/t1/documents/d1/versions/v1/original/report.pdf",
            FileName: "report.pdf",
            MimeType: "application/pdf",
            ContentHash: "abc123",
            CorrelationId: "corr-1",
            OccurredAt: DateTimeOffset.UtcNow);

        await consumer.HandleAsync(message, CancellationToken.None);

        Assert.Equal("document.preprocess.requested", eventBus.LastTopic);
        Assert.NotNull(eventBus.LastEvent);

        var preprocessEvent = Assert.IsType<DocumentPreprocessRequestedEvent>(eventBus.LastEvent);
        Assert.Equal(message.TenantId, preprocessEvent.TenantId);
        Assert.Equal(message.DocumentId, preprocessEvent.DocumentId);
        Assert.Equal(message.VersionId, preprocessEvent.VersionId);
        Assert.Equal(message.OriginalObjectKey, preprocessEvent.OriginalObjectKey);
    }

    [Fact]
    public async Task Forwards_correlation_id_to_preprocess_event()
    {
        var eventBus = new FakeDocumentEventBus();
        var clock = new StubClock();
        var logger = new FakeLogger<DocumentUploadedConsumer>();
        var consumer = new DocumentUploadedConsumer(eventBus, clock, logger);

        var message = new DocumentUploadedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "key", "f.pdf", "text/plain", "hash", "my-correlation-id",
            DateTimeOffset.UtcNow);

        await consumer.HandleAsync(message, CancellationToken.None);

        var preprocessEvent = Assert.IsType<DocumentPreprocessRequestedEvent>(eventBus.LastEvent);
        Assert.Equal("my-correlation-id", preprocessEvent.CorrelationId);
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
