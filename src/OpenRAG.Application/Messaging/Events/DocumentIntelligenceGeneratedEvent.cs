namespace OpenRAG.Application.Messaging.Events;

/// <summary>
/// Published after intelligence generation completes successfully.
/// Consumers should trigger the embeddings step.
/// </summary>
public sealed record DocumentIntelligenceGeneratedEvent(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    string Provider,
    string Model,
    string CorrelationId,
    DateTimeOffset OccurredAt
);
