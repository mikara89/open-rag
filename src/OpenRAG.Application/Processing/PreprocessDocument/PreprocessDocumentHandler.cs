using Mediator;
using Microsoft.Extensions.Logging;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Processing;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Common;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Domain.Common;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

namespace OpenRAG.Application.Processing.PreprocessDocument;

public sealed class PreprocessDocumentHandler : IRequestHandler<PreprocessDocumentCommand, PreprocessDocumentResponse>
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IProcessingRunRepository _processingRunRepository;
    private readonly IDocumentPreprocessor _documentPreprocessor;
    private readonly IDocumentEventBus _eventBus;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PreprocessDocumentHandler> _logger;
    private readonly IDocumentObjectKeyPolicy _objectKeyPolicy;

    public PreprocessDocumentHandler(
        IDocumentRepository documentRepository,
        IProcessingRunRepository processingRunRepository,
        IDocumentPreprocessor documentPreprocessor,
        IDocumentEventBus eventBus,
        IClock clock,
        IUnitOfWork unitOfWork,
        ILogger<PreprocessDocumentHandler> logger,
        IDocumentObjectKeyPolicy objectKeyPolicy)
    {
        _documentRepository = documentRepository;
        _processingRunRepository = processingRunRepository;
        _documentPreprocessor = documentPreprocessor;
        _eventBus = eventBus;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _objectKeyPolicy = objectKeyPolicy;
    }

    public async ValueTask<PreprocessDocumentResponse> Handle(
        PreprocessDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate command
        if (command.TenantId == Guid.Empty)
            throw new AppException("TenantId cannot be empty.");

        if (command.DocumentId == Guid.Empty)
            throw new AppException("DocumentId cannot be empty.");

        if (command.VersionId == Guid.Empty)
            throw new AppException("VersionId cannot be empty.");

        if (command.ProcessingRunId == Guid.Empty)
            throw new AppException("ProcessingRunId cannot be empty.");

        if (string.IsNullOrWhiteSpace(command.CorrelationId))
            throw new AppException("CorrelationId cannot be empty.");

        var tenantId = command.TenantId;

        // 2. Load Document and Version — no-op gracefully if missing or deleted
        var version = await _documentRepository.GetVersionForUpdateAsync(
            tenantId, command.DocumentId, command.VersionId, cancellationToken);

        if (version is null)
        {
            _logger.LogWarning(
                "Preprocess no-op: DocumentVersion not found. DocumentId={DocumentId}, VersionId={VersionId}, CorrelationId={CorrelationId}",
                command.DocumentId, command.VersionId, command.CorrelationId);
            return NoOpResult(command, "VersionNotFound");
        }

        IsolationGuard.Equal(version.TenantId, command.TenantId, nameof(version.TenantId));
        IsolationGuard.Equal(version.DocumentId, command.DocumentId, nameof(version.DocumentId));
        IsolationGuard.Equal(version.Id, command.VersionId, nameof(version.Id));

        var document = await _documentRepository.GetByIdForUpdateAsync(
            tenantId, command.DocumentId, cancellationToken);

        if (document is null)
        {
            _logger.LogWarning(
                "Preprocess no-op: Document not found. DocumentId={DocumentId}, CorrelationId={CorrelationId}",
                command.DocumentId, command.CorrelationId);
            return NoOpResult(command, "DocumentNotFound");
        }

        IsolationGuard.Equal(document.TenantId, command.TenantId, nameof(document.TenantId));
        IsolationGuard.Equal(document.Id, command.DocumentId, nameof(document.Id));

        if (document.Status == DocumentStatus.Deleted)
        {
            _logger.LogWarning(
                "Preprocess no-op: Document is deleted. DocumentId={DocumentId}, CorrelationId={CorrelationId}",
                command.DocumentId, command.CorrelationId);
            return NoOpResult(command, "DocumentDeleted");
        }

        // 3. Load DocumentProcessingRun for update (tracking query)
        var run = await _processingRunRepository.GetByIdForUpdateAsync(
            tenantId, command.ProcessingRunId, cancellationToken);

        if (run is null)
        {
            _logger.LogWarning(
                "Preprocess no-op: ProcessingRun not found. RunId={ProcessingRunId}, CorrelationId={CorrelationId}",
                command.ProcessingRunId, command.CorrelationId);
            return NoOpResult(command, "ProcessingRunNotFound");
        }

        EnsureRunScope(run, command);

        // 4. Check if preprocessing step already completed (idempotency)
        var existingStep = await _processingRunRepository.GetStepForUpdateAsync(
            tenantId, command.ProcessingRunId, DocumentProcessingStepName.Preprocess, cancellationToken);

        if (existingStep is not null)
            EnsureStepScope(existingStep, command);

        if (existingStep is not null && existingStep.Status == DocumentProcessingStepStatus.Completed)
        {
            _logger.LogInformation(
                "Preprocess already completed for this run. DocumentId={DocumentId}, VersionId={VersionId}, RunId={ProcessingRunId}",
                command.DocumentId, command.VersionId, command.ProcessingRunId);
            return new PreprocessDocumentResponse(
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                MarkdownObjectKey: version.DoclingMarkdownObjectKey ?? string.Empty,
                JsonObjectKey: version.DoclingJsonObjectKey ?? string.Empty,
                Status: "AlreadyPreprocessed");
        }

        // Authorization and idempotency checks have completed; mutation is now safe.
        if (document.Status == DocumentStatus.Uploaded
            || document.Status == DocumentStatus.Failed)
        {
            document.MarkProcessing();
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

        // 6. Mark step as running (increments attempt count)
        step.Start();

        // 7. Mark version as preprocessing (if not already)
        if (version.Status != DocumentVersionStatus.Preprocessing
            && version.Status != DocumentVersionStatus.Preprocessed)
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

        _objectKeyPolicy.EnsureOwned(
            version.OriginalObjectKey,
            tenantId,
            command.DocumentId,
            command.VersionId,
            DocumentObjectKind.Source);

        DocumentPreprocessingResult? preprocessResult;
        try
        {
            // 9. Call IDocumentPreprocessor (writes artifact files outside DB transaction)
            preprocessResult = await _documentPreprocessor.PreprocessAsync(
                preprocessRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            // Begin transaction so we can persist the failed step state
            await using var failureTransaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

            step.MarkFailed("PREPROCESS_FAILED", ex.Message);

            // Mark version as failed only if it's in a transient state
            if (version.Status != DocumentVersionStatus.Preprocessed
                && version.Status != DocumentVersionStatus.Deleted)
            {
                version.MarkFailed();
            }

            // Also mark document as Failed
            if (document.Status != DocumentStatus.Ready
                && document.Status != DocumentStatus.Deleted)
            {
                document.MarkFailed();
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await failureTransaction.CommitAsync(cancellationToken);

            _logger.LogError(ex,
                "Preprocess failed: DocumentId={DocumentId}, VersionId={VersionId}, RunId={ProcessingRunId}",
                command.DocumentId, command.VersionId, command.ProcessingRunId);

            return new PreprocessDocumentResponse(
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                MarkdownObjectKey: string.Empty,
                JsonObjectKey: string.Empty,
                Status: "Failed");
        }

        _objectKeyPolicy.EnsureOwned(
            preprocessResult.MarkdownObjectKey,
            tenantId,
            command.DocumentId,
            command.VersionId,
            DocumentObjectKind.Markdown);
        _objectKeyPolicy.EnsureOwned(
            preprocessResult.JsonObjectKey,
            tenantId,
            command.DocumentId,
            command.VersionId,
            DocumentObjectKind.Json);

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

        _logger.LogInformation(
            "Preprocess completed: DocumentId={DocumentId}, VersionId={VersionId}, CorrelationId={CorrelationId}",
            command.DocumentId, command.VersionId, command.CorrelationId);

        return new PreprocessDocumentResponse(
            DocumentId: command.DocumentId,
            VersionId: command.VersionId,
            MarkdownObjectKey: preprocessResult.MarkdownObjectKey,
            JsonObjectKey: preprocessResult.JsonObjectKey,
            Status: "Preprocessed");
    }

    private static PreprocessDocumentResponse NoOpResult(PreprocessDocumentCommand command, string reason)
    {
        return new PreprocessDocumentResponse(
            DocumentId: command.DocumentId,
            VersionId: command.VersionId,
            MarkdownObjectKey: string.Empty,
            JsonObjectKey: string.Empty,
            Status: reason);
    }

    private static void EnsureRunScope(
        DocumentProcessingRun run,
        PreprocessDocumentCommand command)
    {
        IsolationGuard.Equal(run.TenantId, command.TenantId, nameof(run.TenantId));
        IsolationGuard.Equal(run.DocumentId, command.DocumentId, nameof(run.DocumentId));
        IsolationGuard.Equal(run.VersionId, command.VersionId, nameof(run.VersionId));
    }

    private static void EnsureStepScope(
        DocumentProcessingStep step,
        PreprocessDocumentCommand command)
    {
        IsolationGuard.Equal(step.TenantId, command.TenantId, nameof(step.TenantId));
        IsolationGuard.Equal(step.DocumentId, command.DocumentId, nameof(step.DocumentId));
        IsolationGuard.Equal(step.VersionId, command.VersionId, nameof(step.VersionId));
        IsolationGuard.Equal(
            step.ProcessingRunId,
            command.ProcessingRunId,
            nameof(step.ProcessingRunId));
    }
}
