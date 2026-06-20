using OpenRAG.Application.Abstractions.Security;

namespace OpenRAG.Infrastructure.Security;

/// <summary>
/// Development-only user resolver. Returns a deterministic user ID.
/// Will be replaced by auth/claims-based resolver in production.
/// </summary>
public sealed class DevelopmentCurrentUser : ICurrentUser
{
    public Guid UserId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public bool IsAuthenticated => true;
}
