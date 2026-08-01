using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.Security;

namespace OpenRAG.Api.Security;

public sealed class HttpContextCurrentTenant : ICurrentTenant
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptions<JwtAuthenticationOptions> _options;

    public HttpContextCurrentTenant(
        IHttpContextAccessor httpContextAccessor,
        IOptions<JwtAuthenticationOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options;
    }

    public Guid TenantId
    {
        get
        {
            if (!PrincipalIdentity.TryGetSingleNonEmptyGuidClaim(
                    _httpContextAccessor.HttpContext?.User,
                    _options.Value.TenantIdClaimType,
                    out var tenantId))
            {
                throw new InvalidOperationException(
                    "A valid authenticated tenant context is required.");
            }

            return tenantId;
        }
    }
}
