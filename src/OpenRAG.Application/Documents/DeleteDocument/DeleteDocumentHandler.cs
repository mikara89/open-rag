using Mediator;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;
using OpenRAG.Domain.Documents;

namespace OpenRAG.Application.Documents.DeleteDocument;

public sealed class DeleteDocumentHandler : IRequestHandler<DeleteDocumentCommand, DeleteDocumentResponse>
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentEmbeddingRepository _embeddingRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteDocumentHandler(
        IDocumentRepository documentRepository,
        IDocumentChunkRepository chunkRepository,
        IDocumentEmbeddingRepository embeddingRepository,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork)
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _embeddingRepository = embeddingRepository;
        _currentTenant = currentTenant;
        _unitOfWork = unitOfWork;
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

        return new DeleteDocumentResponse(command.DocumentId, true);
    }
}
