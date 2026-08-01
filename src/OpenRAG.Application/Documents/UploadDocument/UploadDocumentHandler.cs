using System.Security.Cryptography;
using Mediator;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Common;
using OpenRAG.Application.Common.Results;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Domain.Documents;
using OpenRAG.Domain.Processing;

namespace OpenRAG.Application.Documents.UploadDocument;

public sealed class UploadDocumentHandler
    : IRequestHandler<UploadDocumentCommand, Result<UploadDocumentResponse>>
{
    private readonly IFileStorage _fileStorage;
    private readonly IDocumentObjectKeyPolicy _objectKeyPolicy;
    private readonly IDocumentRepository _documentRepository;
    private readonly IProcessingRunRepository _processingRunRepository;
    private readonly IDocumentEventBus _eventBus;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public UploadDocumentHandler(
        IFileStorage fileStorage,
        IDocumentObjectKeyPolicy objectKeyPolicy,
        IDocumentRepository documentRepository,
        IProcessingRunRepository processingRunRepository,
        IDocumentEventBus eventBus,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _fileStorage = fileStorage;
        _objectKeyPolicy = objectKeyPolicy;
        _documentRepository = documentRepository;
        _processingRunRepository = processingRunRepository;
        _eventBus = eventBus;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async ValueTask<Result<UploadDocumentResponse>> Handle(
        UploadDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationError = Validate(command);
        if (validationError is not null)
            return Result<UploadDocumentResponse>.Failure(validationError);

        var tenantId = _currentTenant.TenantId;
        var userId = _currentUser.UserId;

        if (tenantId == Guid.Empty)
        {
            throw new IsolationViolationException("The trusted tenant context is empty.");
        }

        if (userId == Guid.Empty)
        {
            throw new IsolationViolationException("The trusted user context is empty.");
        }

        if (!_currentUser.IsAuthenticated)
        {
            throw new IsolationViolationException("The upload handler received an unauthenticated user context.");
        }

        // 1. Generate IDs
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var processingRunId = Guid.NewGuid();

        // 2. Build safe object key
        var safeFileName = Path.GetFileName(command.FileName);
        var objectKey = _objectKeyPolicy.BuildSourceKey(
            tenantId, documentId, versionId, safeFileName);

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
        _objectKeyPolicy.EnsureOwned(
            storedResult.ObjectKey,
            tenantId,
            documentId,
            versionId,
            DocumentObjectKind.Source);
        if (!string.Equals(storedResult.ObjectKey, objectKey, StringComparison.Ordinal))
        {
            throw new IsolationViolationException(
                "The storage provider returned a different source object key.");
        }

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

        return Result<UploadDocumentResponse>.Success(new UploadDocumentResponse(
            DocumentId: documentId,
            VersionId: versionId,
            Status: document.Status.ToString()));
    }

    private static ApplicationError? Validate(UploadDocumentCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.FileName))
        {
            return ApplicationErrors.InvalidRequest(
                "request.file_name_required", "File name cannot be empty.", "fileName");
        }

        if (string.IsNullOrWhiteSpace(command.ContentType))
        {
            return ApplicationErrors.InvalidRequest(
                "request.content_type_required", "Content type cannot be empty.", "contentType");
        }

        if (command.SizeBytes <= 0 || command.SizeBytes > 100L * 1024 * 1024)
        {
            var message = command.SizeBytes <= 0
                ? "File size must be greater than zero."
                : "File size exceeds the maximum allowed size of 100 MB.";
            return ApplicationErrors.InvalidRequest(
                "request.file_size_invalid", message, "sizeBytes");
        }

        if (command.Content is null)
        {
            return ApplicationErrors.InvalidRequest(
                "request.content_required", "Content stream cannot be null.", "content");
        }

        if (string.IsNullOrWhiteSpace(command.CorrelationId))
        {
            return ApplicationErrors.InvalidRequest(
                "request.correlation_id_required", "CorrelationId cannot be empty.", "correlationId");
        }

        return null;
    }
}
