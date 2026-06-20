namespace OpenRAG.Application.Abstractions.Security;

public interface ICurrentTenant
{
    Guid TenantId { get; }
}
