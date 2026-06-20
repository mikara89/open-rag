using OpenRAG.Application.Abstractions.Security;

namespace OpenRAG.Infrastructure.Security;

/// <summary>
/// Development-only tenant resolver. Returns a deterministic tenant ID.
/// Will be replaced by auth/claims-based resolver in production.
/// </summary>
public sealed class DevelopmentCurrentTenant : ICurrentTenant
{
    public Guid TenantId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
}
