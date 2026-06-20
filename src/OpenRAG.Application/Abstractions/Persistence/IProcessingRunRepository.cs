using OpenRAG.Domain.Processing;

namespace OpenRAG.Application.Abstractions.Persistence;

public interface IProcessingRunRepository
{
    Task AddAsync(
        DocumentProcessingRun processingRun,
        CancellationToken cancellationToken = default);

    Task<DocumentProcessingRun?> GetByIdAsync(
        Guid tenantId,
        Guid processingRunId,
        CancellationToken cancellationToken = default);

    Task<DocumentProcessingRun?> GetByIdForUpdateAsync(
        Guid tenantId,
        Guid processingRunId,
        CancellationToken cancellationToken = default);

    Task AddStepAsync(
        DocumentProcessingStep step,
        CancellationToken cancellationToken = default);

    Task<DocumentProcessingStep?> GetStepAsync(
        Guid tenantId,
        Guid processingRunId,
        DocumentProcessingStepName stepName,
        CancellationToken cancellationToken = default);

    Task<DocumentProcessingStep?> GetStepForUpdateAsync(
        Guid tenantId,
        Guid processingRunId,
        DocumentProcessingStepName stepName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentProcessingRun>> GetRunsByDocumentAsync(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentProcessingStep>> GetStepsByRunAsync(
        Guid tenantId,
        Guid processingRunId,
        CancellationToken cancellationToken = default);
}
