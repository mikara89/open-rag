namespace OpenRAG.Application.Messaging.Events;

public sealed record DocumentEmbeddingRequestedEvent(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    string CorrelationId,
    DateTimeOffset OccurredAt
);
