using Mediator;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Common;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

namespace OpenRAG.Application.Documents.ReprocessDocument;

public sealed class ReprocessDocumentHandler : IRequestHandler<ReprocessDocumentCommand, ReprocessDocumentResponse>
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IProcessingRunRepository _processingRunRepository;
    private readonly IDocumentEventBus _eventBus;
    private readonly ICurrentTenant _currentTenant;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDocumentObjectKeyPolicy _objectKeyPolicy;

    public ReprocessDocumentHandler(
        IDocumentRepository documentRepository,
        IProcessingRunRepository processingRunRepository,
        IDocumentEventBus eventBus,
        ICurrentTenant currentTenant,
        IClock clock,
        IUnitOfWork unitOfWork,
        IDocumentObjectKeyPolicy objectKeyPolicy)
    {
        _documentRepository = documentRepository;
        _processingRunRepository = processingRunRepository;
        _eventBus = eventBus;
        _currentTenant = currentTenant;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _objectKeyPolicy = objectKeyPolicy;
    }

    public async ValueTask<ReprocessDocumentResponse> Handle(
        ReprocessDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate
        if (command.DocumentId == Guid.Empty)
            throw new RequestValidationException("DocumentId cannot be empty.");

        if (string.IsNullOrWhiteSpace(command.CorrelationId))
            throw new RequestValidationException("CorrelationId cannot be empty.");

        if (!command.ForcePreprocess
            && !command.ForceChunk
            && !command.ForceIntelligence
            && !command.ForceEmbeddings)
        {
            throw new RequestValidationException(
                "At least one reprocessing stage must be selected.");
        }

        var tenantId = _currentTenant.TenantId;
        if (tenantId == Guid.Empty)
            throw new IsolationViolationException("The trusted tenant context is empty.");

        // 2. Load document for update (tracking query)
        var document = await _documentRepository.GetByIdForUpdateAsync(
            tenantId, command.DocumentId, cancellationToken);

        if (document is null)
            throw new ResourceNotFoundException();

        IsolationGuard.Equal(document.TenantId, tenantId, nameof(document.TenantId));
        IsolationGuard.Equal(document.Id, command.DocumentId, nameof(document.Id));

        // 4. Check document can be reprocessed
        if (document.Status == DocumentStatus.Deleted)
            throw new ResourceConflictException("A deleted document cannot be reprocessed.");

        if (document.Status == DocumentStatus.Processing)
            throw new ResourceConflictException("The document is already processing.");

        if (document.CurrentVersionId is null)
            throw new ResourceConflictException("The document has no current version.");

        var versionId = document.CurrentVersionId.Value;

        // 5. Load version for event fields
        var version = await _documentRepository.GetVersionForUpdateAsync(
            tenantId, command.DocumentId, versionId, cancellationToken);

        if (version is null)
            throw new ResourceNotFoundException();

        IsolationGuard.Equal(version.TenantId, tenantId, nameof(version.TenantId));
        IsolationGuard.Equal(version.DocumentId, command.DocumentId, nameof(version.DocumentId));
        IsolationGuard.Equal(version.Id, versionId, nameof(version.Id));

        if (command.ForcePreprocess)
        {
            _objectKeyPolicy.EnsureOwned(
                version.OriginalObjectKey,
                tenantId,
                command.DocumentId,
                versionId,
                DocumentObjectKind.Source);
        }

        if (command.ForceChunk)
        {
            if (string.IsNullOrWhiteSpace(version.DoclingMarkdownObjectKey))
                throw new ResourceConflictException("The Markdown artifact is not available.");

            _objectKeyPolicy.EnsureOwned(
                version.DoclingMarkdownObjectKey,
                tenantId,
                command.DocumentId,
                versionId,
                DocumentObjectKind.Markdown);
        }

        // 5. Generate processing run ID
        var processingRunId = Guid.NewGuid();

        // 6. Determine processing reason
        var reason = DetermineReason(command);

        // 7. Begin transaction
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // 8. Transition document to Processing
        document.MarkReprocessing();

        // 9. Create new processing run
        var processingRun = DocumentProcessingRun.Create(
            processingRunId,
            tenantId,
            command.DocumentId,
            versionId,
            reason,
            command.CorrelationId);

        await _processingRunRepository.AddAsync(processingRun, cancellationToken);

        // 10. Keep existing generated data available until each worker has produced
        // a complete replacement and can swap it inside its persistence transaction.

        // 11. Save EF changes
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 12. Publish the correct starting event based on flags
        var occurredAt = _clock.UtcNow;

        if (command.ForcePreprocess)
        {
            var preprocessRequested = new DocumentPreprocessRequestedEvent(
                TenantId: tenantId,
                DocumentId: command.DocumentId,
                VersionId: versionId,
                ProcessingRunId: processingRunId,
                OriginalObjectKey: version.OriginalObjectKey,
                FileName: version.OriginalObjectKey.Split('/').Last(),
                MimeType: version.OriginalContentType,
                CorrelationId: command.CorrelationId,
                OccurredAt: occurredAt);

            await _eventBus.PublishAsync("document.preprocess.requested", preprocessRequested, cancellationToken);
        }
        else if (command.ForceChunk)
        {
            var chunkingRequested = new DocumentChunkingRequestedEvent(
                TenantId: tenantId,
                DocumentId: command.DocumentId,
                VersionId: versionId,
                ProcessingRunId: processingRunId,
                MarkdownObjectKey: version.DoclingMarkdownObjectKey ?? string.Empty,
                CorrelationId: command.CorrelationId,
                OccurredAt: occurredAt);

            await _eventBus.PublishAsync("document.chunking.requested", chunkingRequested, cancellationToken);
        }
        else if (command.ForceIntelligence)
        {
            var intelligenceRequested = new DocumentIntelligenceRequestedEvent(
                TenantId: tenantId,
                DocumentId: command.DocumentId,
                VersionId: versionId,
                ProcessingRunId: processingRunId,
                CorrelationId: command.CorrelationId,
                OccurredAt: occurredAt);

            await _eventBus.PublishAsync("document.intelligence.requested", intelligenceRequested, cancellationToken);
        }
        else if (command.ForceEmbeddings)
        {
            var embeddingsRequested = new DocumentEmbeddingsRequestedEvent(
                TenantId: tenantId,
                DocumentId: command.DocumentId,
                VersionId: versionId,
                ProcessingRunId: processingRunId,
                CorrelationId: command.CorrelationId,
                OccurredAt: occurredAt);

            await _eventBus.PublishAsync("document.embeddings.requested", embeddingsRequested, cancellationToken);
        }

        // 13. Commit transaction
        await transaction.CommitAsync(cancellationToken);

        return new ReprocessDocumentResponse(
            DocumentId: command.DocumentId,
            VersionId: versionId,
            Status: "Processing",
            CorrelationId: command.CorrelationId);
    }

    private static ProcessingRunReason DetermineReason(ReprocessDocumentCommand command)
    {
        if (command.ForcePreprocess)
            return ProcessingRunReason.ReprocessWithNewPreprocessor;

        if (command.ForceIntelligence)
            return ProcessingRunReason.ReprocessWithNewIntelligenceModel;

        if (command.ForceEmbeddings)
            return ProcessingRunReason.ReprocessWithNewEmbeddingModel;

        return ProcessingRunReason.ManualRetry;
    }
}
