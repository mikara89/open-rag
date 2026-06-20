using DotNetCore.CAP;
using OpenRAG.Application.Abstractions.Messaging;
using OpenRAG.Application.Abstractions.Time;
using OpenRAG.Application.Messaging.Events;

namespace OpenRAG.Worker.Consumers;

/// <summary>
/// Consumes document.uploaded events and triggers the preprocessing pipeline.
/// </summary>
public sealed class DocumentUploadedConsumer : ICapSubscribe
{
    private readonly IDocumentEventBus _eventBus;
    private readonly IClock _clock;
    private readonly ILogger<DocumentUploadedConsumer> _logger;

    public DocumentUploadedConsumer(
        IDocumentEventBus eventBus,
        IClock clock,
        ILogger<DocumentUploadedConsumer> logger)
    {
        _eventBus = eventBus;
        _clock = clock;
        _logger = logger;
    }

    [CapSubscribe("document.uploaded")]
    public async Task HandleAsync(
        DocumentUploadedEvent message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Received document.uploaded: DocumentId={DocumentId}, VersionId={VersionId}, FileName={FileName}",
            message.DocumentId, message.VersionId, message.FileName);

        // Trigger the next step in the pipeline
        var preprocessEvent = new DocumentPreprocessRequestedEvent(
            TenantId: message.TenantId,
            DocumentId: message.DocumentId,
            VersionId: message.VersionId,
            ProcessingRunId: message.ProcessingRunId,
            OriginalObjectKey: message.OriginalObjectKey,
            FileName: message.FileName,
            MimeType: message.MimeType,
            CorrelationId: message.CorrelationId,
            OccurredAt: _clock.UtcNow);

        await _eventBus.PublishAsync(
            "document.preprocess.requested",
            preprocessEvent,
            cancellationToken);

        _logger.LogInformation(
            "Published document.preprocess.requested for DocumentId={DocumentId}",
            message.DocumentId);
    }
}
