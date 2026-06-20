using Mediator;
using Microsoft.Extensions.Logging;
using OpenRAG.Worker.Consumers;

namespace OpenRAG.UnitTests.Application.Consumers;

public sealed class DocumentPreprocessRequestedConsumerTests
{
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
        public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
            => default!;
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
