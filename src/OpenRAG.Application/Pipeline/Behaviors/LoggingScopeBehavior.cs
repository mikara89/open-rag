using Mediator;
using Microsoft.Extensions.Logging;

namespace OpenRAG.Application.Pipeline.Behaviors;

public sealed class LoggingScopeBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IOpenRagMessage
{
    private readonly ILogger<LoggingScopeBehavior<TMessage, TResponse>> _logger;

    public LoggingScopeBehavior(
        ILogger<LoggingScopeBehavior<TMessage, TResponse>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var scopeValues = new Dictionary<string, object?>
        {
            ["MessageType"] = OpenRagMessageMetadata.Name<TMessage>(),
            ["MessageCategory"] = OpenRagMessageMetadata.Category<TMessage>()
        };

        if (message is ICorrelatedMessage correlated
            && !string.IsNullOrWhiteSpace(correlated.CorrelationId))
        {
            scopeValues["CorrelationId"] = correlated.CorrelationId;
        }

        using var scope = _logger.BeginScope(scopeValues);
        return await next(message, cancellationToken);
    }
}
