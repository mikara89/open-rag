using System.Security.Claims;

namespace OpenRAG.Api.Security;

internal static class UserIdentity
{
    public static bool TryGetUserId(
        ClaimsPrincipal? principal,
        string userIdClaimType,
        out Guid userId)
    {
        userId = Guid.Empty;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var claims = principal.FindAll(userIdClaimType).ToArray();
        return claims.Length == 1 &&
            Guid.TryParse(claims[0].Value, out userId) &&
            userId != Guid.Empty;
    }
}
