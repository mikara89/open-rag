namespace OpenRAG.Application.Messaging.Events;

public sealed record DocumentChunkedEvent(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    int ChunkCount,
    string CorrelationId,
    DateTimeOffset OccurredAt
);
