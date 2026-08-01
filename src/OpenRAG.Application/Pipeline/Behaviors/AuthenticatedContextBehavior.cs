using Mediator;
using Microsoft.Extensions.Logging;
using OpenRAG.Application.Abstractions.Security;

namespace OpenRAG.Application.Pipeline.Behaviors;

public sealed class AuthenticatedContextBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IOpenRagMessage, IAuthenticatedApplicationMessage
{
    private const string ContextRequiredMessage =
        "An authenticated user and tenant context is required.";

    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<AuthenticatedContextBehavior<TMessage, TResponse>> _logger;

    public AuthenticatedContextBehavior(
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        ILogger<AuthenticatedContextBehavior<TMessage, TResponse>> logger)
    {
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Guid userId;
        Guid tenantId;

        try
        {
            if (!_currentUser.IsAuthenticated)
                throw new UnauthorizedAccessException(ContextRequiredMessage);

            userId = _currentUser.UserId;
            tenantId = _currentTenant.TenantId;
        }
        catch (InvalidOperationException exception)
        {
            throw new UnauthorizedAccessException(
                ContextRequiredMessage,
                exception);
        }

        if (userId == Guid.Empty || tenantId == Guid.Empty)
        {
            throw new UnauthorizedAccessException(ContextRequiredMessage);
        }

        using var scope = _logger.BeginScope(
            new Dictionary<string, object?>
            {
                ["UserId"] = userId,
                ["TenantId"] = tenantId
            });

        return await next(message, cancellationToken);
    }
}
