using Mediator;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Processing;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Common;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Domain.Common;
using OpenRAG.Domain.Processing;

namespace OpenRAG.Application.Processing.PreprocessDocument;

public sealed class PreprocessDocumentHandler : IRequestHandler<PreprocessDocumentCommand, PreprocessDocumentResponse>
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IDocumentRepository _documentRepository;
    private readonly IProcessingRunRepository _processingRunRepository;
    private readonly IDocumentPreprocessor _documentPreprocessor;
    private readonly IDocumentEventBus _eventBus;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public PreprocessDocumentHandler(
        ICurrentTenant currentTenant,
        IDocumentRepository documentRepository,
        IProcessingRunRepository processingRunRepository,
        IDocumentPreprocessor documentPreprocessor,
        IDocumentEventBus eventBus,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _currentTenant = currentTenant;
        _documentRepository = documentRepository;
        _processingRunRepository = processingRunRepository;
        _documentPreprocessor = documentPreprocessor;
        _eventBus = eventBus;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async ValueTask<PreprocessDocumentResponse> Handle(
        PreprocessDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate command
        if (command.DocumentId == Guid.Empty)
            throw new AppException("DocumentId cannot be empty.");

        if (command.VersionId == Guid.Empty)
            throw new AppException("VersionId cannot be empty.");

        if (command.ProcessingRunId == Guid.Empty)
            throw new AppException("ProcessingRunId cannot be empty.");

        if (string.IsNullOrWhiteSpace(command.CorrelationId))
            throw new AppException("CorrelationId cannot be empty.");

        var tenantId = _currentTenant.TenantId;

        // 2. Load DocumentVersion for update (tracking query)
        var version = await _documentRepository.GetVersionForUpdateAsync(
            tenantId, command.DocumentId, command.VersionId, cancellationToken);

        if (version is null)
            throw new AppException($"Document version '{command.VersionId}' not found.");

        // 2b. Load Document for update to mark it as Processing
        var document = await _documentRepository.GetByIdForUpdateAsync(
            tenantId, command.DocumentId, cancellationToken);

        if (document is null)
            throw new AppException($"Document '{command.DocumentId}' not found.");

        // 2c. Mark document as Processing if not already
        if (document.Status == Domain.Documents.DocumentStatus.Uploaded
            || document.Status == Domain.Documents.DocumentStatus.Failed)
        {
            document.MarkProcessing();
        }

        // 3. Load DocumentProcessingRun for update (tracking query)
        var run = await _processingRunRepository.GetByIdForUpdateAsync(
            tenantId, command.ProcessingRunId, cancellationToken);

        if (run is null)
            throw new AppException($"Processing run '{command.ProcessingRunId}' not found.");

        // 4. Check if preprocessing step already completed (idempotency)
        var existingStep = await _processingRunRepository.GetStepForUpdateAsync(
            tenantId, command.ProcessingRunId, DocumentProcessingStepName.Preprocess, cancellationToken);

        if (existingStep is not null && existingStep.Status == DocumentProcessingStepStatus.Completed)
        {
            // Already done — return success without re-running
            return new PreprocessDocumentResponse(
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                MarkdownObjectKey: version.DoclingMarkdownObjectKey ?? string.Empty,
                JsonObjectKey: version.DoclingJsonObjectKey ?? string.Empty,
                Status: "AlreadyPreprocessed");
        }

        // 5. Create or reuse DocumentProcessingStep for Preprocess
        var step = existingStep ?? DocumentProcessingStep.Create(
            Guid.NewGuid(),
            tenantId,
            command.DocumentId,
            command.VersionId,
            command.ProcessingRunId,
            DocumentProcessingStepName.Preprocess,
            maxAttempts: 3,
            inputHash: version.OriginalSha256,
            processorName: "MockPreprocessor",
            processorVersion: "1.0");

        if (existingStep is null)
        {
            await _processingRunRepository.AddStepAsync(step, cancellationToken);
        }

        // 6. Mark step as running
        step.Start();

        // 7. Mark version as preprocessing (if not already)
        if (version.Status != Domain.Documents.DocumentVersionStatus.Preprocessing
            && version.Status != Domain.Documents.DocumentVersionStatus.Preprocessed)
        {
            version.AttachDoclingArtifacts(
                markdownObjectKey: "pending",
                jsonObjectKey: "pending");
        }

        // 8. Build preprocessing request
        var preprocessRequest = new DocumentPreprocessingRequest(
            TenantId: tenantId,
            DocumentId: command.DocumentId,
            VersionId: command.VersionId,
            OriginalObjectKey: version.OriginalObjectKey,
            FileName: version.OriginalObjectKey.Split('/').Last(),
            MimeType: version.OriginalContentType,
            CorrelationId: command.CorrelationId);

        DocumentPreprocessingResult? preprocessResult;
        try
        {
            // 9. Call IDocumentPreprocessor (writes artifact files outside DB transaction)
            // TODO: Add cleanup/compensation for object storage files if database transaction fails.
            preprocessResult = await _documentPreprocessor.PreprocessAsync(
                preprocessRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            // Begin transaction so we can persist the failed step state
            await using var failureTransaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

            step.MarkFailed("PREPROCESS_FAILED", ex.Message);

            // Mark version as failed only if it's in a transient state
            if (version.Status != Domain.Documents.DocumentVersionStatus.Preprocessed
                && version.Status != Domain.Documents.DocumentVersionStatus.Deleted)
            {
                version.MarkFailed();
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await failureTransaction.CommitAsync(cancellationToken);

            return new PreprocessDocumentResponse(
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                MarkdownObjectKey: string.Empty,
                JsonObjectKey: string.Empty,
                Status: "Failed");
        }

        // 10. Begin database + CAP transaction
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // 11. Attach real artifact keys to DocumentVersion
        version.AttachDoclingArtifacts(
            markdownObjectKey: preprocessResult.MarkdownObjectKey,
            jsonObjectKey: preprocessResult.JsonObjectKey);

        // 12. Mark DocumentVersion as preprocessed
        version.MarkPreprocessed();

        // 13. Mark processing step as completed
        step.MarkCompleted(preprocessResult.MarkdownSha256);

        // 14. Save EF changes
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 15. Publish DocumentPreprocessedEvent
        var occurredAt = _clock.UtcNow;
        var preprocessedEvent = new DocumentPreprocessedEvent(
            TenantId: tenantId,
            DocumentId: command.DocumentId,
            VersionId: command.VersionId,
            ProcessingRunId: command.ProcessingRunId,
            MarkdownObjectKey: preprocessResult.MarkdownObjectKey,
            JsonObjectKey: preprocessResult.JsonObjectKey,
            MarkdownSha256: preprocessResult.MarkdownSha256,
            JsonSha256: preprocessResult.JsonSha256,
            CorrelationId: command.CorrelationId,
            OccurredAt: occurredAt);

        await _eventBus.PublishAsync("document.preprocessed", preprocessedEvent, cancellationToken);

        // 16. Commit transaction
        await transaction.CommitAsync(cancellationToken);

        return new PreprocessDocumentResponse(
            DocumentId: command.DocumentId,
            VersionId: command.VersionId,
            MarkdownObjectKey: preprocessResult.MarkdownObjectKey,
            JsonObjectKey: preprocessResult.JsonObjectKey,
            Status: "Preprocessed");
    }
}
