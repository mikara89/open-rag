using DotNetCore.CAP;
using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Application.Processing.GenerateIntelligence;

namespace OpenRAG.Worker.Consumers;

/// <summary>
/// Consumes document.chunked events and triggers the intelligence pipeline
/// (or falls back to embeddings if intelligence is disabled).
/// </summary>
public sealed class DocumentChunkedConsumer : ICapSubscribe
{
    private readonly IDocumentEventBus _eventBus;
    private readonly IClock _clock;
    private readonly GenerateIntelligenceOptions _intelligenceOptions;
    private readonly ILogger<DocumentChunkedConsumer> _logger;

    public DocumentChunkedConsumer(
        IDocumentEventBus eventBus,
        IClock clock,
        IOptions<GenerateIntelligenceOptions> intelligenceOptions,
        ILogger<DocumentChunkedConsumer> logger)
    {
        _eventBus = eventBus;
        _clock = clock;
        _intelligenceOptions = intelligenceOptions.Value;
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

        if (_intelligenceOptions.Enabled)
        {
            // Trigger intelligence step
            var intelligenceRequested = new DocumentIntelligenceRequestedEvent(
                TenantId: message.TenantId,
                DocumentId: message.DocumentId,
                VersionId: message.VersionId,
                ProcessingRunId: message.ProcessingRunId,
                CorrelationId: message.CorrelationId,
                OccurredAt: _clock.UtcNow);

            await _eventBus.PublishAsync(
                "document.intelligence.requested",
                intelligenceRequested,
                cancellationToken);

            _logger.LogInformation(
                "Published document.intelligence.requested: DocumentId={DocumentId}, VersionId={VersionId}",
                message.DocumentId, message.VersionId);
        }
        else
        {
            // Intelligence disabled — skip directly to embeddings
            _logger.LogInformation(
                "Intelligence disabled, skipping to embeddings: DocumentId={DocumentId}",
                message.DocumentId);

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
        }
    }
}
