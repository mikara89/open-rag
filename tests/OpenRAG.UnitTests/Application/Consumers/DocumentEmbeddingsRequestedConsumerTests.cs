using Mediator;
using Microsoft.Extensions.Logging;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Application.Processing.GenerateEmbeddings;
using OpenRAG.Worker.Consumers;

namespace OpenRAG.UnitTests.Application.Consumers;

public sealed class DocumentEmbeddingsRequestedConsumerTests
{
    [Fact]
    public async Task Sends_GenerateEmbeddingsCommand_via_Mediator()
    {
        var sender = new FakeSender();
        var logger = new FakeLogger<DocumentEmbeddingsRequestedConsumer>();
        var consumer = new DocumentEmbeddingsRequestedConsumer(sender, logger);

        var message = new DocumentEmbeddingsRequestedEvent(
            TenantId: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            VersionId: Guid.NewGuid(),
            ProcessingRunId: Guid.NewGuid(),
            CorrelationId: "corr-1",
            OccurredAt: DateTimeOffset.UtcNow);

        await consumer.HandleAsync(message, CancellationToken.None);

        Assert.True(sender.Called);
        Assert.NotNull(sender.LastCommand);

        var cmd = Assert.IsType<GenerateEmbeddingsCommand>(sender.LastCommand);
        Assert.Equal(message.DocumentId, cmd.DocumentId);
        Assert.Equal(message.VersionId, cmd.VersionId);
        Assert.Equal(message.ProcessingRunId, cmd.ProcessingRunId);
        Assert.Equal(message.CorrelationId, cmd.CorrelationId);
    }

    [Fact]
    public void Consumer_can_be_constructed_with_dependencies()
    {
        var sender = new FakeSender();
        var logger = new FakeLogger<DocumentEmbeddingsRequestedConsumer>();
        var consumer = new DocumentEmbeddingsRequestedConsumer(sender, logger);
        Assert.NotNull(consumer);
    }

    private sealed class FakeSender : ISender
    {
        public bool Called { get; private set; }
        public object? LastCommand { get; private set; }

        public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            Called = true;
            LastCommand = request;
            if (request is GenerateEmbeddingsCommand cmd)
            {
                var response = new GenerateEmbeddingsResponse(
                    cmd.DocumentId, cmd.VersionId, 3, "mock-embedding-8", 8, "Embedded");
                return new ValueTask<TResponse>((TResponse)(object)response);
            }
            return default!;
        }

        public ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default) => default!;
        public ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken ct = default) => default!;
        public ValueTask<object?> Send(object message, CancellationToken ct = default) => default;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamQuery<TResponse> query, CancellationToken ct = default) => default!;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => default!;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamCommand<TResponse> command, CancellationToken ct = default) => default!;
        public IAsyncEnumerable<object?> CreateStream(object message, CancellationToken ct = default) => default!;
    }

    private sealed class FakeLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null!;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
