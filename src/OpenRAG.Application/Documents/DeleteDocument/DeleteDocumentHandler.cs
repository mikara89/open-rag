using Mediator;
using Microsoft.Extensions.Logging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Common;
using OpenRAG.Domain.Documents;

namespace OpenRAG.Application.Documents.DeleteDocument;

public sealed class DeleteDocumentHandler : IRequestHandler<DeleteDocumentCommand, DeleteDocumentResponse>
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentEmbeddingRepository _embeddingRepository;
    private readonly IFileStorage _fileStorage;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteDocumentHandler> _logger;

    public DeleteDocumentHandler(
        IDocumentRepository documentRepository,
        IDocumentChunkRepository chunkRepository,
        IDocumentEmbeddingRepository embeddingRepository,
        IFileStorage fileStorage,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork,
        ILogger<DeleteDocumentHandler> logger)
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _embeddingRepository = embeddingRepository;
        _fileStorage = fileStorage;
        _currentTenant = currentTenant;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async ValueTask<DeleteDocumentResponse> Handle(
        DeleteDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.DocumentId == Guid.Empty)
            throw new AppException("DocumentId cannot be empty.");

        var tenantId = _currentTenant.TenantId;

        // Load document for update (tracking query)
        var document = await _documentRepository.GetByIdForUpdateAsync(
            tenantId, command.DocumentId, cancellationToken);

        if (document is null)
            throw new AppException($"Document '{command.DocumentId}' not found.");

        // Reject if processing — avoid partial state
        if (document.Status == DocumentStatus.Processing)
            throw new AppException(
                $"Cannot delete document '{command.DocumentId}' while it is processing. Wait for it to complete or fail first.");

        // Begin transaction
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // Delete embeddings for all versions
        foreach (var version in document.Versions)
        {
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
        await _documentRepository.DeleteAsync(document, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Best-effort physical storage cleanup for generated artifacts
        // Source files are preserved; only Docling artifacts are cleaned up.
        await TryCleanupStorageArtifacts(document, cancellationToken);

        return new DeleteDocumentResponse(command.DocumentId, true);
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
            if (!string.IsNullOrWhiteSpace(version.DoclingMarkdownObjectKey)
                && version.DoclingMarkdownObjectKey != "pending")
            {
                try
                {
                    await _fileStorage.DeleteAsync(version.DoclingMarkdownObjectKey, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Best-effort cleanup: failed to delete Markdown artifact. Key={ObjectKey}",
                        version.DoclingMarkdownObjectKey);
                }
            }

            if (!string.IsNullOrWhiteSpace(version.DoclingJsonObjectKey)
                && version.DoclingJsonObjectKey != "pending")
            {
                try
                {
                    await _fileStorage.DeleteAsync(version.DoclingJsonObjectKey, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Best-effort cleanup: failed to delete JSON artifact. Key={ObjectKey}",
                        version.DoclingJsonObjectKey);
                }
            }
        }
    }
}
