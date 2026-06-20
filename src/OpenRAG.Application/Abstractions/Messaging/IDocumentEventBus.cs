namespace OpenRAG.Application.Abstractions.Messaging;

public interface IDocumentEventBus
{
    Task PublishAsync<TEvent>(
        string topic,
        TEvent message,
        CancellationToken cancellationToken = default);
}
