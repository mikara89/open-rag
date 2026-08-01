namespace OpenRAG.Api.Security;

public static class OpenRagClaimTypes
{
    public const string UserId = "sub";
    public const string TenantId = "tenant_id";
    public const string Role = "role";
}

public static class OpenRagRoles
{
    public const string Administrator = "admin";
}

public static class OpenRagPolicies
{
    public const string AuthenticatedUser = "AuthenticatedUser";
    public const string Administrator = "Administrator";
}
