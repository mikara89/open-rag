namespace OpenRAG.Application.Messaging.Events;

public sealed record DocumentPreprocessedEvent(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    string MarkdownObjectKey,
    string JsonObjectKey,
    string MarkdownSha256,
    string JsonSha256,
    string CorrelationId,
    DateTimeOffset OccurredAt
);
