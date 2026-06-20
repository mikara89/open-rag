namespace OpenRAG.Application.Abstractions.Security;

public interface ICurrentUser
{
    Guid UserId { get; }
    bool IsAuthenticated { get; }
}
