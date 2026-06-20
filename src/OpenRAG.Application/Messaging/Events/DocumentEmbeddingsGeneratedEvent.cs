namespace OpenRAG.Application.Messaging.Events;

public sealed record DocumentEmbeddingsGeneratedEvent(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    int EmbeddingCount,
    string EmbeddingModel,
    int EmbeddingDimensions,
    string CorrelationId,
    DateTimeOffset OccurredAt
);
