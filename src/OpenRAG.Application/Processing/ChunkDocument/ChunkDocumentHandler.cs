using System.Text;
using Mediator;
using Microsoft.Extensions.Logging;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Processing;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Common;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

namespace OpenRAG.Application.Processing.ChunkDocument;

public sealed class ChunkDocumentHandler : IRequestHandler<ChunkDocumentCommand, ChunkDocumentResponse>
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _documentChunkRepository;
    private readonly IDocumentEmbeddingRepository _documentEmbeddingRepository;
    private readonly IProcessingRunRepository _processingRunRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IDocumentChunker _documentChunker;
    private readonly IDocumentEventBus _eventBus;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ChunkDocumentHandler> _logger;
    private readonly IDocumentObjectKeyPolicy _objectKeyPolicy;

    public ChunkDocumentHandler(
        IDocumentRepository documentRepository,
        IDocumentChunkRepository documentChunkRepository,
        IDocumentEmbeddingRepository documentEmbeddingRepository,
        IProcessingRunRepository processingRunRepository,
        IFileStorage fileStorage,
        IDocumentChunker documentChunker,
        IDocumentEventBus eventBus,
        IClock clock,
        IUnitOfWork unitOfWork,
        ILogger<ChunkDocumentHandler> logger,
        IDocumentObjectKeyPolicy objectKeyPolicy)
    {
        _documentRepository = documentRepository;
        _documentChunkRepository = documentChunkRepository;
        _documentEmbeddingRepository = documentEmbeddingRepository;
        _processingRunRepository = processingRunRepository;
        _fileStorage = fileStorage;
        _documentChunker = documentChunker;
        _eventBus = eventBus;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _objectKeyPolicy = objectKeyPolicy;
    }

    public async ValueTask<ChunkDocumentResponse> Handle(
        ChunkDocumentCommand command,
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
                "Chunk no-op: DocumentVersion not found. DocumentId={DocumentId}, VersionId={VersionId}, CorrelationId={CorrelationId}",
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
                "Chunk no-op: Document not found. DocumentId={DocumentId}, CorrelationId={CorrelationId}",
                command.DocumentId, command.CorrelationId);
            return NoOpResult(command, "DocumentNotFound");
        }

        IsolationGuard.Equal(document.TenantId, command.TenantId, nameof(document.TenantId));
        IsolationGuard.Equal(document.Id, command.DocumentId, nameof(document.Id));

        if (document.Status == DocumentStatus.Deleted)
        {
            _logger.LogWarning(
                "Chunk no-op: Document is deleted. DocumentId={DocumentId}, CorrelationId={CorrelationId}",
                command.DocumentId, command.CorrelationId);
            return NoOpResult(command, "DocumentDeleted");
        }

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
        {
            _logger.LogWarning(
                "Chunk no-op: ProcessingRun not found. RunId={ProcessingRunId}, CorrelationId={CorrelationId}",
                command.ProcessingRunId, command.CorrelationId);
            return NoOpResult(command, "ProcessingRunNotFound");
        }

        EnsureRunScope(run, command);

        // 5. Check if Chunk step already completed (idempotency within same run)
        var existingStep = await _processingRunRepository.GetStepForUpdateAsync(
            tenantId, command.ProcessingRunId, DocumentProcessingStepName.Chunk, cancellationToken);

        if (existingStep is not null)
            EnsureStepScope(existingStep, command);

        if (existingStep is not null && existingStep.Status == DocumentProcessingStepStatus.Completed)
        {
            _logger.LogInformation(
                "Chunk already completed for this run. DocumentId={DocumentId}, VersionId={VersionId}, RunId={ProcessingRunId}",
                command.DocumentId, command.VersionId, command.ProcessingRunId);

            var existingChunks = await _documentChunkRepository.GetByVersionAsync(
                tenantId, command.DocumentId, command.VersionId, cancellationToken);

            return new ChunkDocumentResponse(
                DocumentId: command.DocumentId,
                VersionId: command.VersionId,
                ChunkCount: existingChunks.Count,
                Status: "AlreadyChunked");
        }

        _objectKeyPolicy.EnsureOwned(
            version.DoclingMarkdownObjectKey,
            tenantId,
            command.DocumentId,
            command.VersionId,
            DocumentObjectKind.Markdown);

        if (!string.IsNullOrWhiteSpace(version.DoclingJsonObjectKey)
            && version.DoclingJsonObjectKey != "pending")
        {
            _objectKeyPolicy.EnsureOwned(
                version.DoclingJsonObjectKey,
                tenantId,
                command.DocumentId,
                command.VersionId,
                DocumentObjectKind.Json);
        }

        // 6. Clean up old chunks and embeddings before recreating (safe idempotency)
        await _documentEmbeddingRepository.DeleteByVersionAsync(
            tenantId, command.DocumentId, command.VersionId, cancellationToken);
        await _documentChunkRepository.DeleteByVersionAsync(
            tenantId, command.DocumentId, command.VersionId, cancellationToken);

        _logger.LogInformation(
            "Deleted old chunks/embeddings for version. DocumentId={DocumentId}, VersionId={VersionId}",
            command.DocumentId, command.VersionId);

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

        // 8. Mark step as running (increments attempt count)
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
            // Persist failed step state
            await using var failureTransaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

            step.MarkFailed("CHUNKING_FAILED", ex.Message);

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
                "Chunking failed: DocumentId={DocumentId}, VersionId={VersionId}, RunId={ProcessingRunId}",
                command.DocumentId, command.VersionId, command.ProcessingRunId);

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

        _logger.LogInformation(
            "Chunking completed: DocumentId={DocumentId}, VersionId={VersionId}, ChunkCount={ChunkCount}, CorrelationId={CorrelationId}",
            command.DocumentId, command.VersionId, documentChunks.Count, command.CorrelationId);

        return new ChunkDocumentResponse(
            DocumentId: command.DocumentId,
            VersionId: command.VersionId,
            ChunkCount: documentChunks.Count,
            Status: "Chunked");
    }

    private static ChunkDocumentResponse NoOpResult(ChunkDocumentCommand command, string reason)
    {
        return new ChunkDocumentResponse(
            DocumentId: command.DocumentId,
            VersionId: command.VersionId,
            ChunkCount: 0,
            Status: reason);
    }

    private static void EnsureRunScope(DocumentProcessingRun run, ChunkDocumentCommand command)
    {
        IsolationGuard.Equal(run.TenantId, command.TenantId, nameof(run.TenantId));
        IsolationGuard.Equal(run.DocumentId, command.DocumentId, nameof(run.DocumentId));
        IsolationGuard.Equal(run.VersionId, command.VersionId, nameof(run.VersionId));
    }

    private static void EnsureStepScope(DocumentProcessingStep step, ChunkDocumentCommand command)
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
