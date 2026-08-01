using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenRAG.Application.Abstractions.Security;

namespace OpenRAG.Application.Pipeline.Behaviors;

public sealed class LoggingScopeBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IOpenRagMessage
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LoggingScopeBehavior<TMessage, TResponse>> _logger;

    public LoggingScopeBehavior(
        IServiceProvider serviceProvider,
        ILogger<LoggingScopeBehavior<TMessage, TResponse>> logger)
    {
        _serviceProvider = serviceProvider;
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

        if (message is IExplicitTenantMessage explicitTenant
            && explicitTenant.TenantId != Guid.Empty)
        {
            scopeValues["TenantId"] = explicitTenant.TenantId;
        }

        if (message is IAuthenticatedApplicationMessage)
        {
            var currentUser = _serviceProvider.GetRequiredService<ICurrentUser>();
            var currentTenant = _serviceProvider.GetRequiredService<ICurrentTenant>();

            if (currentUser.IsAuthenticated && currentUser.UserId != Guid.Empty)
                scopeValues["UserId"] = currentUser.UserId;

            if (currentTenant.TenantId != Guid.Empty)
                scopeValues["TenantId"] = currentTenant.TenantId;
        }

        using var scope = _logger.BeginScope(scopeValues);
        return await next(message, cancellationToken);
    }
}
