using Mediator;
using Microsoft.Extensions.Logging;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Application.Processing.PreprocessDocument;
using OpenRAG.Worker.Consumers;

namespace OpenRAG.UnitTests.Application.Consumers;

public sealed class DocumentPreprocessRequestedConsumerTests
{
    [Fact]
    public async Task Sends_command_with_event_tenant()
    {
        var sender = new FakeSender();
        var consumer = new DocumentPreprocessRequestedConsumer(
            sender,
            new FakeLogger<DocumentPreprocessRequestedConsumer>());
        var message = new DocumentPreprocessRequestedEvent(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "key",
            "file.pdf",
            "application/pdf",
            "corr",
            DateTimeOffset.UtcNow);

        await consumer.HandleAsync(message, CancellationToken.None);

        var command = Assert.IsType<PreprocessDocumentCommand>(sender.LastCommand);
        Assert.Equal(message.TenantId, command.TenantId);
        Assert.Equal(message.DocumentId, command.DocumentId);
        Assert.Equal(message.VersionId, command.VersionId);
        Assert.Equal(message.ProcessingRunId, command.ProcessingRunId);
        Assert.Equal(message.CorrelationId, command.CorrelationId);
    }

    [Fact]
    public void Consumer_can_be_constructed_with_dependencies()
    {
        var sender = new FakeSender();
        var logger = new FakeLogger<DocumentPreprocessRequestedConsumer>();
        var consumer = new DocumentPreprocessRequestedConsumer(sender, logger);
        Assert.NotNull(consumer);
    }

    private sealed class FakeSender : ISender
    {
        public object? LastCommand { get; private set; }

        public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            LastCommand = request;
            if (request is PreprocessDocumentCommand command)
            {
                var response = new PreprocessDocumentResponse(
                    command.DocumentId, command.VersionId, "markdown", "json", "Preprocessed");
                return new ValueTask<TResponse>((TResponse)(object)response);
            }

            return default!;
        }
        public ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default)
            => default!;
        public ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken ct = default)
            => default!;
        public ValueTask<object?> Send(object message, CancellationToken ct = default)
            => default;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamQuery<TResponse> query, CancellationToken ct = default)
            => default!;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default)
            => default!;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamCommand<TResponse> command, CancellationToken ct = default)
            => default!;
        public IAsyncEnumerable<object?> CreateStream(object message, CancellationToken ct = default)
            => default!;
    }

    private sealed class FakeLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null!;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
