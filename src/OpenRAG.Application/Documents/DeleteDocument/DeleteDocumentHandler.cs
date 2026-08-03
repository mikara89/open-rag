using Mediator;
using Microsoft.Extensions.Logging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Common;
using OpenRAG.Application.Common.Results;
using OpenRAG.Domain.Documents;

namespace OpenRAG.Application.Documents.DeleteDocument;

public sealed class DeleteDocumentHandler
    : IRequestHandler<DeleteDocumentCommand, Result<DeleteDocumentResponse>>
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentEmbeddingRepository _embeddingRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IDocumentObjectKeyPolicy _objectKeyPolicy;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteDocumentHandler> _logger;

    public DeleteDocumentHandler(
        IDocumentRepository documentRepository,
        IDocumentChunkRepository chunkRepository,
        IDocumentEmbeddingRepository embeddingRepository,
        IFileStorage fileStorage,
        IDocumentObjectKeyPolicy objectKeyPolicy,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork,
        ILogger<DeleteDocumentHandler> logger)
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _embeddingRepository = embeddingRepository;
        _fileStorage = fileStorage;
        _objectKeyPolicy = objectKeyPolicy;
        _currentTenant = currentTenant;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async ValueTask<Result<DeleteDocumentResponse>> Handle(
        DeleteDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.DocumentId == Guid.Empty)
        {
            return Result<DeleteDocumentResponse>.Failure(
                ApplicationErrors.InvalidRequest(
                    "request.document_id_required",
                    "DocumentId cannot be empty.",
                    "documentId"));
        }

        var tenantId = _currentTenant.TenantId;

        // Load document for update (tracking query)
        var document = await _documentRepository.GetByIdForUpdateAsync(
            tenantId, command.DocumentId, cancellationToken);

        if (document is null)
            return Result<DeleteDocumentResponse>.Failure(ApplicationErrors.ResourceNotFound());

        IsolationGuard.Equal(document.TenantId, tenantId, nameof(document.TenantId));
        IsolationGuard.Equal(document.Id, command.DocumentId, nameof(document.Id));

        // Reject if processing — avoid partial state
        if (document.Status == DocumentStatus.Processing)
        {
            return Result<DeleteDocumentResponse>.Failure(
                ApplicationErrors.ResourceConflict(
                    "document.processing",
                    "The document cannot be deleted while it is processing."));
        }

        ValidateStorageKeys(document, tenantId);

        // Begin transaction
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // Delete embeddings for all versions
        foreach (var version in document.Versions)
        {
            IsolationGuard.Equal(version.TenantId, tenantId, nameof(version.TenantId));
            IsolationGuard.Equal(version.DocumentId, document.Id, nameof(version.DocumentId));
            IsolationGuard.NonEmpty(version.Id, nameof(version.Id));

            await _embeddingRepository.DeleteByVersionAsync(
                tenantId, document.Id, version.Id, cancellationToken);
        }

        // Delete chunks for all versions
        foreach (var version in document.Versions)
        {
            await _chunkRepository.DeleteByVersionAsync(
                tenantId, document.Id, version.Id, cancellationToken);
        }

        // Delete the document (cascade-deletes versions via EF owned relationship)
        IsolationGuard.Equal(document.TenantId, tenantId, nameof(document.TenantId));
        await _documentRepository.DeleteAsync(document, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Best-effort physical storage cleanup after authorization and key validation.
        await TryCleanupStorageArtifacts(document, cancellationToken);

        return Result<DeleteDocumentResponse>.Success(
            new DeleteDocumentResponse(command.DocumentId, true));
    }

    /// <summary>
    /// Best-effort cleanup of generated Docling artifacts from physical storage.
    /// Does not fail the operation if cleanup fails.
    /// </summary>
    private async Task TryCleanupStorageArtifacts(
        Document document,
        CancellationToken cancellationToken)
    {
        foreach (var version in document.Versions)
        {
            await TryDelete(version.OriginalObjectKey, cancellationToken);

            if (!string.IsNullOrWhiteSpace(version.DoclingMarkdownObjectKey)
                && version.DoclingMarkdownObjectKey != "pending")
            {
                await TryDelete(version.DoclingMarkdownObjectKey, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(version.DoclingJsonObjectKey)
                && version.DoclingJsonObjectKey != "pending")
            {
                await TryDelete(version.DoclingJsonObjectKey, cancellationToken);
            }
        }
    }

    private void ValidateStorageKeys(Document document, Guid tenantId)
    {
        foreach (var version in document.Versions)
        {
            _objectKeyPolicy.EnsureOwned(
                version.OriginalObjectKey,
                tenantId,
                document.Id,
                version.Id,
                DocumentObjectKind.Source);

            if (!string.IsNullOrWhiteSpace(version.DoclingMarkdownObjectKey)
                && version.DoclingMarkdownObjectKey != "pending")
            {
                _objectKeyPolicy.EnsureOwned(
                    version.DoclingMarkdownObjectKey,
                    tenantId,
                    document.Id,
                    version.Id,
                    DocumentObjectKind.Markdown);
            }

            if (!string.IsNullOrWhiteSpace(version.DoclingJsonObjectKey)
                && version.DoclingJsonObjectKey != "pending")
            {
                _objectKeyPolicy.EnsureOwned(
                    version.DoclingJsonObjectKey,
                    tenantId,
                    document.Id,
                    version.Id,
                    DocumentObjectKind.Json);
            }
        }
    }

    private async Task TryDelete(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            await _fileStorage.DeleteAsync(objectKey, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Best-effort cleanup failed for a validated document object.");
        }
    }
}
