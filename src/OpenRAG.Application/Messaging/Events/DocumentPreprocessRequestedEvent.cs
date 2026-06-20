namespace OpenRAG.Application.Messaging.Events;

public sealed record DocumentPreprocessRequestedEvent(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    string OriginalObjectKey,
    string FileName,
    string MimeType,
    string CorrelationId,
    DateTimeOffset OccurredAt
);
