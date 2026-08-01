namespace OpenRAG.Api.Security;

public sealed class JwtAuthenticationOptions
{
    public const string SectionName = "Authentication:Jwt";
    public const int MaximumClockSkewSeconds = 300;

    public string Authority { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public bool RequireHttpsMetadata { get; set; } = true;
    public string UserIdClaimType { get; set; } = OpenRagClaimTypes.UserId;
    public string TenantIdClaimType { get; set; } = OpenRagClaimTypes.TenantId;
    public string RoleClaimType { get; set; } = OpenRagClaimTypes.Role;
    public int ClockSkewSeconds { get; set; } = 60;
}
