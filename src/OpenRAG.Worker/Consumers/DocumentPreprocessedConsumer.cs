using DotNetCore.CAP;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Messaging.Events;

namespace OpenRAG.Worker.Consumers;

/// <summary>
/// Consumes document.preprocessed events and triggers the chunking pipeline.
/// </summary>
public sealed class DocumentPreprocessedConsumer : ICapSubscribe
{
    private readonly IDocumentEventBus _eventBus;
    private readonly IClock _clock;
    private readonly ILogger<DocumentPreprocessedConsumer> _logger;

    public DocumentPreprocessedConsumer(
        IDocumentEventBus eventBus,
        IClock clock,
        ILogger<DocumentPreprocessedConsumer> logger)
    {
        _eventBus = eventBus;
        _clock = clock;
        _logger = logger;
    }

    [CapSubscribe("document.preprocessed")]
    public async Task HandleAsync(
        DocumentPreprocessedEvent message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Received document.preprocessed: DocumentId={DocumentId}, VersionId={VersionId}, MarkdownKey={MarkdownKey}",
            message.DocumentId, message.VersionId, message.MarkdownObjectKey);

        // Trigger the next step in the pipeline: chunking
        var chunkingRequestedEvent = new DocumentChunkingRequestedEvent(
            TenantId: message.TenantId,
            DocumentId: message.DocumentId,
            VersionId: message.VersionId,
            ProcessingRunId: message.ProcessingRunId,
            MarkdownObjectKey: message.MarkdownObjectKey,
            CorrelationId: message.CorrelationId,
            OccurredAt: _clock.UtcNow);

        await _eventBus.PublishAsync(
            "document.chunking.requested",
            chunkingRequestedEvent,
            cancellationToken);

        _logger.LogInformation(
            "Published document.chunking.requested: DocumentId={DocumentId}, VersionId={VersionId}",
            message.DocumentId, message.VersionId);
    }
}
