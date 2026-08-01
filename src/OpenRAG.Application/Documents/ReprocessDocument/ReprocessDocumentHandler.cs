using Mediator;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Common;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

namespace OpenRAG.Application.Documents.ReprocessDocument;

public sealed class ReprocessDocumentHandler : IRequestHandler<ReprocessDocumentCommand, ReprocessDocumentResponse>
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentEmbeddingRepository _embeddingRepository;
    private readonly IDocumentIntelligenceRepository _intelligenceRepository;
    private readonly IProcessingRunRepository _processingRunRepository;
    private readonly IDocumentEventBus _eventBus;
    private readonly ICurrentTenant _currentTenant;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ReprocessDocumentHandler(
        IDocumentRepository documentRepository,
        IDocumentChunkRepository chunkRepository,
        IDocumentEmbeddingRepository embeddingRepository,
        IDocumentIntelligenceRepository intelligenceRepository,
        IProcessingRunRepository processingRunRepository,
        IDocumentEventBus eventBus,
        ICurrentTenant currentTenant,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _embeddingRepository = embeddingRepository;
        _intelligenceRepository = intelligenceRepository;
        _processingRunRepository = processingRunRepository;
        _eventBus = eventBus;
        _currentTenant = currentTenant;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async ValueTask<ReprocessDocumentResponse> Handle(
        ReprocessDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate
        if (command.DocumentId == Guid.Empty)
            throw new AppException("DocumentId cannot be empty.");

        if (string.IsNullOrWhiteSpace(command.CorrelationId))
            throw new AppException("CorrelationId cannot be empty.");

        var tenantId = _currentTenant.TenantId;
        if (tenantId == Guid.Empty)
            throw new AppException("Current tenant ID cannot be empty.");

        // 2. Load document for update (tracking query)
        var document = await _documentRepository.GetByIdForUpdateAsync(
            tenantId, command.DocumentId, cancellationToken);

        if (document is null)
            throw new AppException($"Document '{command.DocumentId}' not found.");

        // 3. Validate tenant ownership
        if (document.TenantId != tenantId)
            throw new AppException($"Document '{command.DocumentId}' does not belong to tenant '{tenantId}'.");

        // 4. Check document can be reprocessed
        if (document.Status == DocumentStatus.Deleted)
            throw new AppException($"Cannot reprocess deleted document '{command.DocumentId}'.");

        if (document.Status == DocumentStatus.Processing)
            throw new AppException(
                $"Document '{command.DocumentId}' is already processing. Wait for it to complete or fail before reprocessing.");

        if (document.CurrentVersionId is null)
            throw new AppException($"Document '{command.DocumentId}' has no version.");

        var versionId = document.CurrentVersionId.Value;

        // 5. Load version for event fields
        var version = await _documentRepository.GetVersionForUpdateAsync(
            tenantId, command.DocumentId, versionId, cancellationToken);

        if (version is null)
            throw new AppException($"Version '{versionId}' not found.");

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

        // 10. Delete old chunks if requested
        if (command.ForceChunk)
        {
            await _chunkRepository.DeleteByVersionAsync(
                tenantId, command.DocumentId, versionId, cancellationToken);
        }

        // 11. Delete old intelligence if requested
        if (command.ForceIntelligence)
        {
            await _intelligenceRepository.DeleteByVersionAsync(
                tenantId, command.DocumentId, versionId, cancellationToken);
        }

        // 12. Delete old embeddings if requested
        if (command.ForceEmbeddings)
        {
            await _embeddingRepository.DeleteByVersionAsync(
                tenantId, command.DocumentId, versionId, cancellationToken);
        }

        // 13. Save EF changes
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 14. Publish the correct starting event based on flags
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

        // 14. Commit transaction
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
