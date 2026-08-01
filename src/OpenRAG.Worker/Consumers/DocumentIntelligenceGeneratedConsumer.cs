using DotNetCore.CAP;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Messaging.Events;

namespace OpenRAG.Worker.Consumers;

/// <summary>
/// Consumes document.intelligence.generated events and triggers the embeddings pipeline.
/// </summary>
public sealed class DocumentIntelligenceGeneratedConsumer : ICapSubscribe
{
    private readonly IDocumentEventBus _eventBus;
    private readonly IClock _clock;
    private readonly ILogger<DocumentIntelligenceGeneratedConsumer> _logger;

    public DocumentIntelligenceGeneratedConsumer(
        IDocumentEventBus eventBus,
        IClock clock,
        ILogger<DocumentIntelligenceGeneratedConsumer> logger)
    {
        _eventBus = eventBus;
        _clock = clock;
        _logger = logger;
    }

    [CapSubscribe("document.intelligence.generated")]
    public async Task HandleAsync(
        DocumentIntelligenceGeneratedEvent message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Received document.intelligence.generated: DocumentId={DocumentId}, VersionId={VersionId}",
            message.DocumentId, message.VersionId);

        // Trigger the next step in the pipeline: embeddings
        var embeddingsRequestedEvent = new DocumentEmbeddingsRequestedEvent(
            TenantId: message.TenantId,
            DocumentId: message.DocumentId,
            VersionId: message.VersionId,
            ProcessingRunId: message.ProcessingRunId,
            CorrelationId: message.CorrelationId,
            OccurredAt: _clock.UtcNow);

        await _eventBus.PublishAsync(
            "document.embeddings.requested",
            embeddingsRequestedEvent,
            cancellationToken);

        _logger.LogInformation(
            "Published document.embeddings.requested: DocumentId={DocumentId}, VersionId={VersionId}",
            message.DocumentId, message.VersionId);
    }
}
