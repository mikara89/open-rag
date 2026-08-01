namespace OpenRAG.Application.Abstractions.Persistence;

public interface IDocumentAuthorizationRepository
{
    Task<IReadOnlySet<Guid>> GetExistingIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken cancellationToken = default);
}
