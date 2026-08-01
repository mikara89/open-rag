using DotNetCore.CAP;
using Mediator;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Application.Processing.GenerateIntelligence;

namespace OpenRAG.Worker.Consumers;

/// <summary>
/// Consumes document.intelligence.requested events.
/// Dispatches intelligence generation via Mediator.
/// </summary>
public sealed class DocumentIntelligenceRequestedConsumer : ICapSubscribe
{
    private readonly ISender _sender;
    private readonly ILogger<DocumentIntelligenceRequestedConsumer> _logger;

    public DocumentIntelligenceRequestedConsumer(
        ISender sender,
        ILogger<DocumentIntelligenceRequestedConsumer> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    [CapSubscribe("document.intelligence.requested")]
    public async Task HandleAsync(
        DocumentIntelligenceRequestedEvent message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Received document.intelligence.requested: DocumentId={DocumentId}, VersionId={VersionId}",
            message.DocumentId, message.VersionId);

        var command = new GenerateIntelligenceCommand(
            TenantId: message.TenantId,
            DocumentId: message.DocumentId,
            VersionId: message.VersionId,
            ProcessingRunId: message.ProcessingRunId,
            CorrelationId: message.CorrelationId);

        var result = await _sender.Send(command, cancellationToken);

        _logger.LogInformation(
            "Intelligence generation completed: DocumentId={DocumentId}, VersionId={VersionId}, Status={Status}",
            result.DocumentId, result.VersionId, result.Status);
    }
}
