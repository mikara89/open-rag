using DotNetCore.CAP;
using OpenRAG.Application.Abstractions.Messaging;

namespace OpenRAG.Infrastructure.Messaging;

/// <summary>
/// CAP-backed event bus. Publishes events through CAP's outbox to RabbitMQ.
/// </summary>
public sealed class CapDocumentEventBus : IDocumentEventBus
{
    private readonly ICapPublisher _capPublisher;

    public CapDocumentEventBus(ICapPublisher capPublisher)
    {
        _capPublisher = capPublisher;
    }

    public Task PublishAsync<TEvent>(
        string topic,
        TEvent message,
        CancellationToken cancellationToken = default)
    {
        // CAP publish participates in the active transaction set up by UnitOfWork.BeginTransactionAsync.
        return _capPublisher.PublishAsync(
            topic,
            message,
            cancellationToken: cancellationToken);
    }
}
