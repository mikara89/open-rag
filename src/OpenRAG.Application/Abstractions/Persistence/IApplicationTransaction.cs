namespace OpenRAG.Application.Abstractions.Persistence;

/// <summary>
/// Application-level transaction abstraction.
/// Wraps an underlying database/CAP transaction without exposing
/// EF Core or CAP types to the Application layer.
/// </summary>
public interface IApplicationTransaction : IAsyncDisposable
{
    /// <summary>
    /// Commits the transaction. After commit, the transaction is no longer usable.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}
