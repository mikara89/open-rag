using System.Security.Cryptography;
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

namespace OpenRAG.Application.Documents.UploadDocument;

public sealed class UploadDocumentHandler : IRequestHandler<UploadDocumentCommand, UploadDocumentResponse>
{
    private const long MaxFileSizeBytes = 100 * 1024 * 1024; // 100 MB

    private readonly IFileStorage _fileStorage;
    private readonly IDocumentRepository _documentRepository;
    private readonly IProcessingRunRepository _processingRunRepository;
    private readonly IDocumentEventBus _eventBus;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public UploadDocumentHandler(
        IFileStorage fileStorage,
        IDocumentRepository documentRepository,
        IProcessingRunRepository processingRunRepository,
        IDocumentEventBus eventBus,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _fileStorage = fileStorage;
        _documentRepository = documentRepository;
        _processingRunRepository = processingRunRepository;
        _eventBus = eventBus;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async ValueTask<UploadDocumentResponse> Handle(
        UploadDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);

        var tenantId = _currentTenant.TenantId;
        var userId = _currentUser.UserId;

        if (tenantId == Guid.Empty)
        {
            throw new AppException("Current tenant ID is empty.");
        }

        if (userId == Guid.Empty)
        {
            throw new AppException("Current user ID is empty.");
        }

        if (!_currentUser.IsAuthenticated)
        {
            throw new AppException("User must be authenticated to upload documents.");
        }

        // 1. Generate IDs
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var processingRunId = Guid.NewGuid();

        // 2. Build safe object key
        var safeFileName = Path.GetFileName(command.FileName);
        var objectKey = $"tenants/{tenantId}/documents/{documentId}/versions/{versionId}/original/{safeFileName}";

        // 3. Compute SHA-256 and save file
        string contentHash;
        if (command.Content.CanSeek)
        {
            command.Content.Position = 0;
        }

        using var sha256 = SHA256.Create();

        // Copy to a buffer for dual use (hash + upload)
        using var memoryStream = new MemoryStream();
        await command.Content.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        var hashBytes = await sha256.ComputeHashAsync(memoryStream, cancellationToken);
        contentHash = Convert.ToHexStringLower(hashBytes);
        memoryStream.Position = 0;

        // 4. Save original file via IFileStorage (outside the DB transaction)
        // TODO: Add compensation/cleanup for object storage file if database transaction fails.
        var storedResult = await _fileStorage.SaveAsync(
            memoryStream, objectKey, command.ContentType, cancellationToken);

        // 5. Begin database + CAP transaction
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // 6. Create Document aggregate
        var document = Document.Create(
            documentId,
            tenantId,
            safeFileName,
            safeFileName,
            userId);

        // 7. Create and attach DocumentVersion
        document.AddVersion(
            versionId,
            versionNumber: 1,
            originalObjectKey: objectKey,
            originalContentType: command.ContentType,
            originalSizeBytes: storedResult.SizeBytes,
            originalSha256: contentHash);

        // 8. Create DocumentProcessingRun
        var processingRun = DocumentProcessingRun.Create(
            processingRunId,
            tenantId,
            documentId,
            versionId,
            ProcessingRunReason.InitialUpload,
            command.CorrelationId);

        // 9. Persist through repositories
        await _documentRepository.AddAsync(document, cancellationToken);
        await _processingRunRepository.AddAsync(processingRun, cancellationToken);

        // 10. Save EF changes (flushes tracked entities to the transaction)
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 11. Publish DocumentUploadedEvent through CAP (persisted to outbox in same transaction)
        var occurredAt = _clock.UtcNow;
        var uploadEvent = new DocumentUploadedEvent(
            TenantId: tenantId,
            DocumentId: documentId,
            VersionId: versionId,
            ProcessingRunId: processingRunId,
            OriginalObjectKey: objectKey,
            FileName: safeFileName,
            MimeType: command.ContentType,
            ContentHash: contentHash,
            CorrelationId: command.CorrelationId,
            OccurredAt: occurredAt);

        await _eventBus.PublishAsync("document.uploaded", uploadEvent, cancellationToken);

        // 12. Commit transaction — document metadata and CAP outbox message are now durable
        await transaction.CommitAsync(cancellationToken);

        return new UploadDocumentResponse(
            DocumentId: documentId,
            VersionId: versionId,
            Status: document.Status.ToString());
    }

    private static void Validate(UploadDocumentCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.FileName))
        {
            throw new AppException("File name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(command.ContentType))
        {
            throw new AppException("Content type cannot be empty.");
        }

        if (command.SizeBytes <= 0)
        {
            throw new AppException("File size must be greater than zero.");
        }

        if (command.SizeBytes > MaxFileSizeBytes)
        {
            throw new AppException($"File size exceeds maximum allowed size of {MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        if (command.Content is null)
        {
            throw new AppException("Content stream cannot be null.");
        }
    }
}
