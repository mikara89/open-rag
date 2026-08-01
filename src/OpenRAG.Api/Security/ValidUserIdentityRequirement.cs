using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace OpenRAG.Api.Security;

public sealed class ValidUserIdentityRequirement : IAuthorizationRequirement;

public sealed class ValidUserIdentityHandler : AuthorizationHandler<ValidUserIdentityRequirement>
{
    private readonly IOptions<JwtAuthenticationOptions> _options;

    public ValidUserIdentityHandler(IOptions<JwtAuthenticationOptions> options)
    {
        _options = options;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ValidUserIdentityRequirement requirement)
    {
        if (UserIdentity.TryGetUserId(
                context.User,
                _options.Value.UserIdClaimType,
                out _))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
