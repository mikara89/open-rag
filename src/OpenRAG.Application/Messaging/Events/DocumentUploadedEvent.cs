namespace OpenRAG.Application.Messaging.Events;

public sealed record DocumentUploadedEvent(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    string OriginalObjectKey,
    string FileName,
    string MimeType,
    string ContentHash,
    string CorrelationId,
    DateTimeOffset OccurredAt
);
