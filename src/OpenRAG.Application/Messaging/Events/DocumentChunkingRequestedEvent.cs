namespace OpenRAG.Application.Messaging.Events;

public sealed record DocumentChunkingRequestedEvent(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    string MarkdownObjectKey,
    string CorrelationId,
    DateTimeOffset OccurredAt
);
