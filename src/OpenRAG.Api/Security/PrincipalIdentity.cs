using System.Security.Claims;

namespace OpenRAG.Api.Security;

internal static class PrincipalIdentity
{
    public static bool TryGetSingleNonEmptyGuidClaim(
        ClaimsPrincipal? principal,
        string claimType,
        out Guid value)
    {
        value = Guid.Empty;

        if (principal?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(claimType))
        {
            return false;
        }

        var claims = principal.FindAll(claimType).ToArray();
        return claims.Length == 1 &&
            Guid.TryParse(claims[0].Value, out value) &&
            value != Guid.Empty;
    }
}
