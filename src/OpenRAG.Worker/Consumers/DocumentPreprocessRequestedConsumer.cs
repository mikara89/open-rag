using DotNetCore.CAP;
using Mediator;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Application.Processing.PreprocessDocument;

namespace OpenRAG.Worker.Consumers;

/// <summary>
/// Consumes document.preprocess.requested events.
/// Dispatches preprocessing via Mediator.
/// Placeholder — Docling invocation will be added later.
/// </summary>
public sealed class DocumentPreprocessRequestedConsumer : ICapSubscribe
{
    private readonly ISender _sender;
    private readonly ILogger<DocumentPreprocessRequestedConsumer> _logger;

    public DocumentPreprocessRequestedConsumer(
        ISender sender,
        ILogger<DocumentPreprocessRequestedConsumer> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    [CapSubscribe("document.preprocess.requested")]
    public async Task HandleAsync(
        DocumentPreprocessRequestedEvent message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Received document.preprocess.requested: DocumentId={DocumentId}, VersionId={VersionId}, FileName={FileName}",
            message.DocumentId, message.VersionId, message.FileName);

        // Dispatch to Application handler via Mediator
        var command = new PreprocessDocumentCommand(
            DocumentId: message.DocumentId,
            VersionId: message.VersionId,
            ProcessingRunId: message.ProcessingRunId,
            CorrelationId: message.CorrelationId);

        // TODO: Invoke Docling preprocessing when Docling client is available.
        var result = await _sender.Send(command, cancellationToken);

        _logger.LogInformation(
            "Preprocess completed: DocumentId={DocumentId}, VersionId={VersionId}, Status={Status}, Markdown={MarkdownKey}, Json={JsonKey}",
            result.DocumentId, result.VersionId, result.Status, result.MarkdownObjectKey, result.JsonObjectKey);
    }
}
