using Microsoft.Extensions.Logging;
using OpenRAG.Application.Abstractions.Messaging;

namespace OpenRAG.Infrastructure.Messaging;

/// <summary>
/// No-op event bus for local development.
/// Logs published events but does not dispatch them.
/// Will be replaced by CAP in production.
/// </summary>
public sealed class NoOpDocumentEventBus : IDocumentEventBus
{
    private readonly ILogger<NoOpDocumentEventBus> _logger;

    public NoOpDocumentEventBus(ILogger<NoOpDocumentEventBus> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync<TEvent>(
        string topic,
        TEvent message,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[NoOpEventBus] Topic: {Topic}, EventType: {EventType}, Data: {@Message}",
            topic, typeof(TEvent).Name, message);

        return Task.CompletedTask;
    }
}
