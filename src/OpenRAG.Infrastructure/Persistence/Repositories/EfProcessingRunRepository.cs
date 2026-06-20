using Microsoft.EntityFrameworkCore;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Domain.Processing;

namespace OpenRAG.Infrastructure.Persistence.Repositories;

public sealed class EfProcessingRunRepository : IProcessingRunRepository
{
    private readonly AppDbContext _dbContext;

    public EfProcessingRunRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(
        DocumentProcessingRun processingRun,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.DocumentProcessingRuns.AddAsync(processingRun, cancellationToken);
    }

    public async Task<DocumentProcessingRun?> GetByIdAsync(
        Guid tenantId,
        Guid processingRunId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentProcessingRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId && r.Id == processingRunId,
                cancellationToken);
    }

    public async Task<DocumentProcessingRun?> GetByIdForUpdateAsync(
        Guid tenantId,
        Guid processingRunId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentProcessingRuns
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId && r.Id == processingRunId,
                cancellationToken);
    }

    public async Task AddStepAsync(
        DocumentProcessingStep step,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.DocumentProcessingSteps.AddAsync(step, cancellationToken);
    }

    public async Task<DocumentProcessingStep?> GetStepAsync(
        Guid tenantId,
        Guid processingRunId,
        DocumentProcessingStepName stepName,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentProcessingSteps
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.TenantId == tenantId
                     && s.ProcessingRunId == processingRunId
                     && s.StepName == stepName,
                cancellationToken);
    }

    public async Task<DocumentProcessingStep?> GetStepForUpdateAsync(
        Guid tenantId,
        Guid processingRunId,
        DocumentProcessingStepName stepName,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentProcessingSteps
            .FirstOrDefaultAsync(
                s => s.TenantId == tenantId
                     && s.ProcessingRunId == processingRunId
                     && s.StepName == stepName,
                cancellationToken);
    }
}
