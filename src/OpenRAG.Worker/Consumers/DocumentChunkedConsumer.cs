using DotNetCore.CAP;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Messaging.Events;

namespace OpenRAG.Worker.Consumers;

/// <summary>
/// Consumes document.chunked events and triggers the embedding pipeline.
/// </summary>
public sealed class DocumentChunkedConsumer : ICapSubscribe
{
    private readonly IDocumentEventBus _eventBus;
    private readonly IClock _clock;
    private readonly ILogger<DocumentChunkedConsumer> _logger;

    public DocumentChunkedConsumer(
        IDocumentEventBus eventBus,
        IClock clock,
        ILogger<DocumentChunkedConsumer> logger)
    {
        _eventBus = eventBus;
        _clock = clock;
        _logger = logger;
    }

    [CapSubscribe("document.chunked")]
    public async Task HandleAsync(
        DocumentChunkedEvent message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Received document.chunked: DocumentId={DocumentId}, VersionId={VersionId}, ChunkCount={ChunkCount}",
            message.DocumentId, message.VersionId, message.ChunkCount);

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
