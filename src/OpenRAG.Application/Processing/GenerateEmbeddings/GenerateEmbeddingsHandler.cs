using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Common;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

namespace OpenRAG.Application.Processing.GenerateEmbeddings;

public sealed class GenerateEmbeddingsHandler : IRequestHandler<GenerateEmbeddingsCommand, GenerateEmbeddingsResponse>
{
    private readonly IDocumentChunkRepository _documentChunkRepository;
    private readonly IDocumentEmbeddingRepository _documentEmbeddingRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IProcessingRunRepository _processingRunRepository;
    private readonly IEmbeddingService _embeddingService;
    private readonly IDocumentEventBus _eventBus;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly GenerateEmbeddingsOptions _options;
    private readonly ILogger<GenerateEmbeddingsHandler> _logger;

    public GenerateEmbeddingsHandler(
        IDocumentChunkRepository documentChunkRepository,
        IDocumentEmbeddingRepository documentEmbeddingRepository,
        IDocumentRepository documentRepository,
        IProcessingRunRepository processingRunRepository,
        IEmbeddingService embeddingService,
        IDocumentEventBus eventBus,
        IClock clock,
        IUnitOfWork unitOfWork,
        IOptions<GenerateEmbeddingsOptions> options,
        ILogger<GenerateEmbeddingsHandler> logger)
    {
        _documentChunkRepository = documentChunkRepository;
        _documentEmbeddingRepository = documentEmbeddingRepository;
        _documentRepository = documentRepository;
        _processingRunRepository = processingRunRepository;
        _embeddingService = embeddingService;
        _eventBus = eventBus;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    public async ValueTask<GenerateEmbeddingsResponse> Handle(
        GenerateEmbeddingsCommand command,
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

        // 2. Load processing run for update
        var run = await _processingRunRepository.GetByIdForUpdateAsync(
            tenantId, command.ProcessingRunId, cancellationToken);

        if (run is null)
        {
            _logger.LogWarning(
                "Embedding no-op: ProcessingRun not found. RunId={ProcessingRunId}, CorrelationId={CorrelationId}",
                command.ProcessingRunId, command.CorrelationId);
            return NoOpResult(command, "ProcessingRunNotFound");
        }

        // 3. Load document — no-op gracefully if missing or deleted
        var document = await _documentRepository.GetByIdForUpdateAsync(
            tenantId, command.DocumentId, cancellationToken);

        if (document is null)
        {
            _logger.LogWarning(
                "Embedding no-op: Document not found. DocumentId={DocumentId}, CorrelationId={CorrelationId}",
                command.DocumentId, command.CorrelationId);
            return NoOpResult(command, "DocumentNotFound");
        }

        if (document.Status == DocumentStatus.Deleted)
        {
            _logger.LogWarning(
                "Embedding no-op: Document is deleted. DocumentId={DocumentId}, CorrelationId={CorrelationId}",
                command.DocumentId, command.CorrelationId);
            return NoOpResult(command, "DocumentDeleted");
        }

        // 4. Check if GenerateEmbeddings step already completed (idempotency within same run)
        var existingStep = await _processingRunRepository.GetStepForUpdateAsync(
            tenantId, command.ProcessingRunId, DocumentProcessingStepName.GenerateEmbeddings, cancellationToken);

        if (existingStep is not null && existingStep.Status == DocumentProcessingStepStatus.Completed)
        {
            _logger.LogInformation(
                "Embeddings already generated for this run. DocumentId={DocumentId}, VersionId={VersionId}, RunId={ProcessingRunId}",
                command.DocumentId, command.VersionId, command.ProcessingRunId);
            return new GenerateEmbeddingsResponse(
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                EmbeddingCount: 0,
                EmbeddingModel: "unknown",
                EmbeddingDimensions: 0,
                Status: "AlreadyEmbedded");
        }

        // 5. Load chunks for version
        var chunks = await _documentChunkRepository.GetByVersionAsync(
            tenantId, command.DocumentId, command.VersionId, cancellationToken);

        if (chunks.Count == 0)
            throw new AppException($"No chunks found for version '{command.VersionId}'. Chunking must complete first.");

        // 6. Clean up old embeddings before recreating (safe idempotency)
        await _documentEmbeddingRepository.DeleteByVersionAsync(
            tenantId, command.DocumentId, command.VersionId, cancellationToken);

        _logger.LogInformation(
            "Deleted old embeddings for version. DocumentId={DocumentId}, VersionId={VersionId}",
            command.DocumentId, command.VersionId);

        // 7. Create or reuse processing step
        var step = existingStep ?? DocumentProcessingStep.Create(
            Guid.NewGuid(),
            tenantId,
            command.DocumentId,
            command.VersionId,
            command.ProcessingRunId,
            DocumentProcessingStepName.GenerateEmbeddings,
            maxAttempts: 3,
            inputHash: chunks.First().ContentHash,
            processorName: "MockEmbeddingService",
            processorVersion: "1.0");

        if (existingStep is null)
        {
            await _processingRunRepository.AddStepAsync(step, cancellationToken);
        }

        // 8. Mark step Running (increments attempt count)
        step.Start();

        // 9. Call IEmbeddingService for each chunk
        var embeddings = new List<DocumentEmbedding>();
        string actualModel = _options.Model;
        int actualDimensions = 0;
        try
        {
            foreach (var chunk in chunks)
            {
                var embeddingRequest = new EmbeddingRequest(
                    TenantId: tenantId,
                    Input: chunk.Content,
                    Model: _options.Model,
                    CorrelationId: command.CorrelationId);

                var result = await _embeddingService.GenerateEmbeddingAsync(embeddingRequest, cancellationToken);

                // Capture model and dimensions from the first result
                if (embeddings.Count == 0)
                {
                    actualModel = result.Model;
                    actualDimensions = result.Dimensions;
                }

                var embedding = DocumentEmbedding.Create(
                    id: Guid.NewGuid(),
                    tenantId: tenantId,
                    documentId: command.DocumentId,
                    versionId: command.VersionId,
                    chunkId: chunk.Id,
                    vector: result.Vector.ToArray(),
                    embeddingProvider: result.Provider,
                    embeddingModel: result.Model,
                    embeddingDimensions: result.Dimensions,
                    embeddingVersion: result.EmbeddingVersion);

                embeddings.Add(embedding);
            }
        }
        catch (Exception ex)
        {
            // Persist failure
            await using var failureTransaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

            step.MarkFailed("EMBEDDING_FAILED", ex.Message);

            // Mark document as Failed
            if (document.Status != DocumentStatus.Ready
                && document.Status != DocumentStatus.Deleted)
            {
                document.MarkFailed();
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await failureTransaction.CommitAsync(cancellationToken);

            _logger.LogError(ex,
                "Embedding generation failed: DocumentId={DocumentId}, VersionId={VersionId}, RunId={ProcessingRunId}",
                command.DocumentId, command.VersionId, command.ProcessingRunId);

            return new GenerateEmbeddingsResponse(
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                EmbeddingCount: 0,
                EmbeddingModel: actualModel,
                EmbeddingDimensions: actualDimensions,
                Status: "Failed");
        }

        // 10. Begin transaction
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // 11. Add embeddings
        await _documentEmbeddingRepository.AddRangeAsync(embeddings, cancellationToken);

        // 11b. Mark document as Ready (last pipeline step succeeded)
        if (document.Status == DocumentStatus.Processing)
        {
            document.MarkReady();
        }

        // 12. Mark step completed
        step.MarkCompleted(actualModel);

        // 13. SaveChanges
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 14. Publish DocumentEmbeddingsGeneratedEvent
        var occurredAt = _clock.UtcNow;
        var generatedEvent = new DocumentEmbeddingsGeneratedEvent(
            TenantId: tenantId,
            DocumentId: command.DocumentId,
            VersionId: command.VersionId,
            ProcessingRunId: command.ProcessingRunId,
            EmbeddingCount: embeddings.Count,
            EmbeddingModel: actualModel,
            EmbeddingDimensions: actualDimensions,
            CorrelationId: command.CorrelationId,
            OccurredAt: occurredAt);

        await _eventBus.PublishAsync("document.embeddings.generated", generatedEvent, cancellationToken);

        // 15. Commit transaction
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Embedding generation completed: DocumentId={DocumentId}, VersionId={VersionId}, Count={Count}, CorrelationId={CorrelationId}",
            command.DocumentId, command.VersionId, embeddings.Count, command.CorrelationId);

        return new GenerateEmbeddingsResponse(
            DocumentId: command.DocumentId,
            VersionId: command.VersionId,
            EmbeddingCount: embeddings.Count,
            EmbeddingModel: actualModel,
            EmbeddingDimensions: actualDimensions,
            Status: "Embedded");
    }

    private static GenerateEmbeddingsResponse NoOpResult(GenerateEmbeddingsCommand command, string reason)
    {
        return new GenerateEmbeddingsResponse(
            DocumentId: command.DocumentId,
            VersionId: command.VersionId,
            EmbeddingCount: 0,
            EmbeddingModel: string.Empty,
            EmbeddingDimensions: 0,
            Status: reason);
    }
}
