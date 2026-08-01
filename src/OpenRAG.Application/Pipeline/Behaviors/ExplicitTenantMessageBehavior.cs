using Mediator;
using Microsoft.Extensions.Logging;
using OpenRAG.Application.Common;

namespace OpenRAG.Application.Pipeline.Behaviors;

public sealed class ExplicitTenantMessageBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IOpenRagMessage, IExplicitTenantMessage
{
    private readonly ILogger<ExplicitTenantMessageBehavior<TMessage, TResponse>> _logger;

    public ExplicitTenantMessageBehavior(
        ILogger<ExplicitTenantMessageBehavior<TMessage, TResponse>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (message.TenantId == Guid.Empty)
            throw new RequestValidationException("TenantId cannot be empty.");

        using var scope = _logger.BeginScope(
            new Dictionary<string, object?>
            {
                ["TenantId"] = message.TenantId
            });

        return await next(message, cancellationToken);
    }
}
