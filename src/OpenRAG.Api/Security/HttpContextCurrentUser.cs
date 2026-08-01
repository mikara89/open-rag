using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.Security;

namespace OpenRAG.Api.Security;

public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptions<JwtAuthenticationOptions> _options;

    public HttpContextCurrentUser(
        IHttpContextAccessor httpContextAccessor,
        IOptions<JwtAuthenticationOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options;
    }

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public Guid UserId
    {
        get
        {
            if (!UserIdentity.TryGetUserId(
                    _httpContextAccessor.HttpContext?.User,
                    _options.Value.UserIdClaimType,
                    out var userId))
            {
                throw new InvalidOperationException(
                    "A current user requires an authenticated principal with exactly one non-empty GUID user-ID claim.");
            }

            return userId;
        }
    }
}
