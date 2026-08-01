using Mediator;
using Microsoft.Extensions.Logging;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Application.Processing.GenerateIntelligence;
using OpenRAG.Worker.Consumers;

namespace OpenRAG.UnitTests.Application.Consumers;

public sealed class DocumentIntelligenceRequestedConsumerTests
{
    [Fact]
    public async Task Sends_command_with_event_tenant()
    {
        var sender = new FakeSender();
        var consumer = new DocumentIntelligenceRequestedConsumer(
            sender,
            new FakeLogger<DocumentIntelligenceRequestedConsumer>());
        var message = new DocumentIntelligenceRequestedEvent(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "corr", DateTimeOffset.UtcNow);

        await consumer.HandleAsync(message, CancellationToken.None);

        var command = Assert.IsType<GenerateIntelligenceCommand>(sender.LastCommand);
        Assert.Equal(message.TenantId, command.TenantId);
        Assert.Equal(message.DocumentId, command.DocumentId);
        Assert.Equal(message.VersionId, command.VersionId);
        Assert.Equal(message.ProcessingRunId, command.ProcessingRunId);
        Assert.Equal(message.CorrelationId, command.CorrelationId);
    }

    private sealed class FakeSender : ISender
    {
        public object? LastCommand { get; private set; }

        public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            LastCommand = request;
            var command = Assert.IsType<GenerateIntelligenceCommand>(request);
            var response = new GenerateIntelligenceResponse(
                command.DocumentId, command.VersionId, "Generated", "test", "test");
            return new ValueTask<TResponse>((TResponse)(object)response);
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
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
