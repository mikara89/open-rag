using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace OpenRAG.Api.Security;

public sealed class ValidTenantIdentityRequirement : IAuthorizationRequirement;

public sealed class ValidTenantIdentityHandler : AuthorizationHandler<ValidTenantIdentityRequirement>
{
    private readonly IOptions<JwtAuthenticationOptions> _options;

    public ValidTenantIdentityHandler(IOptions<JwtAuthenticationOptions> options)
    {
        _options = options;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ValidTenantIdentityRequirement requirement)
    {
        if (PrincipalIdentity.TryGetSingleNonEmptyGuidClaim(
                context.User,
                _options.Value.TenantIdClaimType,
                out _))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
