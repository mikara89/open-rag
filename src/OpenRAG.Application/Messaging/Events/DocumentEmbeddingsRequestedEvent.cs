namespace OpenRAG.Application.Messaging.Events;

public sealed record DocumentEmbeddingsRequestedEvent(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    string CorrelationId,
    DateTimeOffset OccurredAt
);
