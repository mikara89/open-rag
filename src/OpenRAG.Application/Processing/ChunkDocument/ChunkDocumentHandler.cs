using System.Text;
using Mediator;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Processing;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Common;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

namespace OpenRAG.Application.Processing.ChunkDocument;

public sealed class ChunkDocumentHandler : IRequestHandler<ChunkDocumentCommand, ChunkDocumentResponse>
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _documentChunkRepository;
    private readonly IProcessingRunRepository _processingRunRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IDocumentChunker _documentChunker;
    private readonly IDocumentEventBus _eventBus;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ChunkDocumentHandler(
        ICurrentTenant currentTenant,
        IDocumentRepository documentRepository,
        IDocumentChunkRepository documentChunkRepository,
        IProcessingRunRepository processingRunRepository,
        IFileStorage fileStorage,
        IDocumentChunker documentChunker,
        IDocumentEventBus eventBus,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _currentTenant = currentTenant;
        _documentRepository = documentRepository;
        _documentChunkRepository = documentChunkRepository;
        _processingRunRepository = processingRunRepository;
        _fileStorage = fileStorage;
        _documentChunker = documentChunker;
        _eventBus = eventBus;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async ValueTask<ChunkDocumentResponse> Handle(
        ChunkDocumentCommand command,
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

        // 3. Ensure Markdown object key exists
        if (string.IsNullOrWhiteSpace(version.DoclingMarkdownObjectKey)
            || version.DoclingMarkdownObjectKey == "pending")
        {
            throw new AppException(
                $"Document version '{command.VersionId}' has no Markdown artifact. Preprocessing must complete first.");
        }

        // 4. Load ProcessingRun for update (tracking query)
        var run = await _processingRunRepository.GetByIdForUpdateAsync(
            tenantId, command.ProcessingRunId, cancellationToken);

        if (run is null)
            throw new AppException($"Processing run '{command.ProcessingRunId}' not found.");

        // 5. Check if Chunk step already completed (idempotency)
        var existingStep = await _processingRunRepository.GetStepForUpdateAsync(
            tenantId, command.ProcessingRunId, DocumentProcessingStepName.Chunk, cancellationToken);

        if (existingStep is not null && existingStep.Status == DocumentProcessingStepStatus.Completed)
        {
            var existingChunks = await _documentChunkRepository.GetByVersionAsync(
                tenantId, command.DocumentId, command.VersionId, cancellationToken);

            return new ChunkDocumentResponse(
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                ChunkCount: existingChunks.Count,
                Status: "AlreadyChunked");
        }

        // 6. If chunks already exist for this version, return AlreadyChunked
        var hasChunks = await _documentChunkRepository.AnyForVersionAsync(
            tenantId, command.DocumentId, command.VersionId, cancellationToken);

        if (hasChunks)
        {
            var chunks = await _documentChunkRepository.GetByVersionAsync(
                tenantId, command.DocumentId, command.VersionId, cancellationToken);

            return new ChunkDocumentResponse(
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                ChunkCount: chunks.Count,
                Status: "AlreadyChunked");
        }

        // 7. Create or reuse DocumentProcessingStep for Chunk
        var step = existingStep ?? DocumentProcessingStep.Create(
            Guid.NewGuid(),
            tenantId,
            command.DocumentId,
            command.VersionId,
            command.ProcessingRunId,
            DocumentProcessingStepName.Chunk,
            maxAttempts: 3,
            inputHash: version.DoclingMarkdownObjectKey,
            processorName: "SimpleMarkdownChunker",
            processorVersion: "1.0");

        if (existingStep is null)
        {
            await _processingRunRepository.AddStepAsync(step, cancellationToken);
        }

        // 8. Mark step as running
        step.Start();

        // 9. Read Markdown from IFileStorage
        string markdown;
        await using (var stream = await _fileStorage.OpenReadAsync(
            version.DoclingMarkdownObjectKey, cancellationToken))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            markdown = await reader.ReadToEndAsync(cancellationToken);
        }

        // 9b. Read Docling JSON artifact if available
        string? doclingJson = null;
        if (!string.IsNullOrWhiteSpace(version.DoclingJsonObjectKey)
            && version.DoclingJsonObjectKey != "pending")
        {
            try
            {
                await using var jsonStream = await _fileStorage.OpenReadAsync(
                    version.DoclingJsonObjectKey, cancellationToken);
                using var jsonReader = new StreamReader(jsonStream, Encoding.UTF8);
                doclingJson = await jsonReader.ReadToEndAsync(cancellationToken);
            }
            catch
            {
                // Continue with Markdown only if JSON read fails
            }
        }

        // 10. Call IDocumentChunker
        IReadOnlyList<DocumentChunkingResultItem> chunkResults;
        try
        {
            var chunkingRequest = new DocumentChunkingRequest(
                TenantId: tenantId,
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                Markdown: markdown,
                DoclingJson: doclingJson,
                CorrelationId: command.CorrelationId);

            chunkResults = await _documentChunker.ChunkAsync(chunkingRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            // Begin transaction so we can persist the failed step state
            await using var failureTransaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

            step.MarkFailed("CHUNKING_FAILED", ex.Message);

            if (version.Status != DocumentVersionStatus.Preprocessed
                && version.Status != DocumentVersionStatus.Deleted)
            {
                version.MarkFailed();
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await failureTransaction.CommitAsync(cancellationToken);

            return new ChunkDocumentResponse(
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                ChunkCount: 0,
                Status: "Failed");
        }

        // 11. Convert chunk results to DocumentChunk entities
        var documentChunks = chunkResults.Select(r => DocumentChunk.Create(
            id: Guid.NewGuid(),
            tenantId: tenantId,
            documentId: command.DocumentId,
            versionId: command.VersionId,
            chunkIndex: r.ChunkIndex,
            content: r.Content,
            contentHash: r.ContentHash,
            tokenCount: r.TokenCount,
            pageNumber: r.PageNumber,
            sectionTitle: r.SectionTitle))
            .ToList();

        // 12. Begin transaction
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // 13. Add chunks
        await _documentChunkRepository.AddRangeAsync(documentChunks, cancellationToken);

        // 14. Mark step as completed
        step.MarkCompleted(chunkResults.LastOrDefault()?.ContentHash ?? "no-chunks");

        // 15. SaveChanges
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 16. Publish DocumentChunkedEvent
        var occurredAt = _clock.UtcNow;
        var chunkedEvent = new DocumentChunkedEvent(
            TenantId: tenantId,
            DocumentId: command.DocumentId,
            VersionId: command.VersionId,
            ProcessingRunId: command.ProcessingRunId,
            ChunkCount: documentChunks.Count,
            CorrelationId: command.CorrelationId,
            OccurredAt: occurredAt);

        await _eventBus.PublishAsync("document.chunked", chunkedEvent, cancellationToken);

        // 17. Commit transaction
        await transaction.CommitAsync(cancellationToken);

        return new ChunkDocumentResponse(
            DocumentId: command.DocumentId,
            VersionId: command.VersionId,
            ChunkCount: documentChunks.Count,
            Status: "Chunked");
    }
}
