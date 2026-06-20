namespace OpenRAG.Application.Abstractions.Persistence;

public interface IUnitOfWork
{
    /// <summary>
    /// Begins an application-level transaction that wraps the underlying
    /// database and CAP outbox into a single transactional boundary.
    /// </summary>
    Task<IApplicationTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves all pending changes tracked by the unit of work.
    /// </summary>
    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
