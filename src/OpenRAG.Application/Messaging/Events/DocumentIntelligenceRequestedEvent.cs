namespace OpenRAG.Application.Messaging.Events;

/// <summary>
/// Published after document chunking is complete to request intelligence generation.
/// </summary>
public sealed record DocumentIntelligenceRequestedEvent(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    string CorrelationId,
    DateTimeOffset OccurredAt
);
