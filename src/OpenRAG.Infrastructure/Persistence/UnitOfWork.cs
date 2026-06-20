using Microsoft.EntityFrameworkCore.Storage;
using OpenRAG.Application.Abstractions.Persistence;

namespace OpenRAG.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _dbContext;

    public UnitOfWork(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IApplicationTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        var efTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        return new EfApplicationTransaction(efTransaction);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed class EfApplicationTransaction : IApplicationTransaction
    {
        private readonly IDbContextTransaction _transaction;
        private bool _committed;

        public EfApplicationTransaction(IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            _committed = true;
            await _transaction.CommitAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_committed)
            {
                await _transaction.RollbackAsync();
            }

            await _transaction.DisposeAsync();
        }
    }
}
