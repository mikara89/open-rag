using Mediator;
using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Common;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

namespace OpenRAG.Application.Processing.GenerateEmbeddings;

public sealed class GenerateEmbeddingsHandler : IRequestHandler<GenerateEmbeddingsCommand, GenerateEmbeddingsResponse>
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IDocumentChunkRepository _documentChunkRepository;
    private readonly IDocumentEmbeddingRepository _documentEmbeddingRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IProcessingRunRepository _processingRunRepository;
    private readonly IEmbeddingService _embeddingService;
    private readonly IDocumentEventBus _eventBus;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly GenerateEmbeddingsOptions _options;

    public GenerateEmbeddingsHandler(
        ICurrentTenant currentTenant,
        IDocumentChunkRepository documentChunkRepository,
        IDocumentEmbeddingRepository documentEmbeddingRepository,
        IDocumentRepository documentRepository,
        IProcessingRunRepository processingRunRepository,
        IEmbeddingService embeddingService,
        IDocumentEventBus eventBus,
        IClock clock,
        IUnitOfWork unitOfWork,
        IOptions<GenerateEmbeddingsOptions> options)
    {
        _currentTenant = currentTenant;
        _documentChunkRepository = documentChunkRepository;
        _documentEmbeddingRepository = documentEmbeddingRepository;
        _documentRepository = documentRepository;
        _processingRunRepository = processingRunRepository;
        _embeddingService = embeddingService;
        _eventBus = eventBus;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _options = options.Value;
    }

    public async ValueTask<GenerateEmbeddingsResponse> Handle(
        GenerateEmbeddingsCommand command,
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

        // 2. Load processing run for update
        var run = await _processingRunRepository.GetByIdForUpdateAsync(
            tenantId, command.ProcessingRunId, cancellationToken);

        if (run is null)
            throw new AppException($"Processing run '{command.ProcessingRunId}' not found.");

        // 3. Check if GenerateEmbeddings step already completed
        var existingStep = await _processingRunRepository.GetStepForUpdateAsync(
            tenantId, command.ProcessingRunId, DocumentProcessingStepName.GenerateEmbeddings, cancellationToken);

        if (existingStep is not null && existingStep.Status == DocumentProcessingStepStatus.Completed)
        {
            return new GenerateEmbeddingsResponse(
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                EmbeddingCount: 0,
                EmbeddingModel: "unknown",
                EmbeddingDimensions: 0,
                Status: "AlreadyEmbedded");
        }

        // 4. Check if embeddings already exist for version/model
        var idempotencyModel = _options.Model;
        var hasEmbeddings = await _documentEmbeddingRepository.AnyForVersionAsync(
            tenantId, command.DocumentId, command.VersionId, idempotencyModel, cancellationToken);

        if (hasEmbeddings)
        {
            return new GenerateEmbeddingsResponse(
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                EmbeddingCount: 0,
                EmbeddingModel: idempotencyModel,
                EmbeddingDimensions: 0,
                Status: "AlreadyEmbedded");
        }

        // 5. Load chunks for version
        var chunks = await _documentChunkRepository.GetByVersionAsync(
            tenantId, command.DocumentId, command.VersionId, cancellationToken);

        if (chunks.Count == 0)
            throw new AppException($"No chunks found for version '{command.VersionId}'. Chunking must complete first.");

        // 6. Create or reuse processing step
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

        // 7. Mark step Running
        step.Start();

        // 8. Call IEmbeddingService for each chunk
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

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await failureTransaction.CommitAsync(cancellationToken);

            return new GenerateEmbeddingsResponse(
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                EmbeddingCount: 0,
                EmbeddingModel: actualModel,
                EmbeddingDimensions: actualDimensions,
                Status: "Failed");
        }

        // 9. Begin transaction
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // 10. Add embeddings
        await _documentEmbeddingRepository.AddRangeAsync(embeddings, cancellationToken);

        // 10b. Mark document as Ready (last pipeline step succeeded)
        var document = await _documentRepository.GetByIdForUpdateAsync(
            tenantId, command.DocumentId, cancellationToken);
        if (document is not null && document.Status == Domain.Documents.DocumentStatus.Processing)
        {
            document.MarkReady();
        }

        // 11. Mark step completed
        step.MarkCompleted(actualModel);

        // 12. SaveChanges
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 13. Publish DocumentEmbeddingsGeneratedEvent
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

        // 14. Commit transaction
        await transaction.CommitAsync(cancellationToken);

        return new GenerateEmbeddingsResponse(
            DocumentId: command.DocumentId,
            VersionId: command.VersionId,
            EmbeddingCount: embeddings.Count,
            EmbeddingModel: actualModel,
            EmbeddingDimensions: actualDimensions,
            Status: "Embedded");
    }
}
