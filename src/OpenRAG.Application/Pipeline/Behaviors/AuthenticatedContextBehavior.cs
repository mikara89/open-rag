using Mediator;
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

    public AuthenticatedContextBehavior(
        ICurrentUser currentUser,
        ICurrentTenant currentTenant)
    {
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    public ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_currentUser.IsAuthenticated
            || _currentUser.UserId == Guid.Empty
            || _currentTenant.TenantId == Guid.Empty)
        {
            throw new UnauthorizedAccessException(ContextRequiredMessage);
        }

        return next(message, cancellationToken);
    }
}
