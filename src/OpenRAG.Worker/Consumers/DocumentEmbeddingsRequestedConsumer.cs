using DotNetCore.CAP;
using Mediator;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Application.Processing.GenerateEmbeddings;

namespace OpenRAG.Worker.Consumers;

/// <summary>
/// Consumes document.embeddings.requested events.
/// Dispatches embedding generation via Mediator.
/// </summary>
public sealed class DocumentEmbeddingsRequestedConsumer : ICapSubscribe
{
    private readonly ISender _sender;
    private readonly ILogger<DocumentEmbeddingsRequestedConsumer> _logger;

    public DocumentEmbeddingsRequestedConsumer(
        ISender sender,
        ILogger<DocumentEmbeddingsRequestedConsumer> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    [CapSubscribe("document.embeddings.requested")]
    public async Task HandleAsync(
        DocumentEmbeddingsRequestedEvent message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Received document.embeddings.requested: DocumentId={DocumentId}, VersionId={VersionId}",
            message.DocumentId, message.VersionId);

        // Dispatch to Application handler via Mediator
        var command = new GenerateEmbeddingsCommand(
            DocumentId: message.DocumentId,
            VersionId: message.VersionId,
            ProcessingRunId: message.ProcessingRunId,
            CorrelationId: message.CorrelationId);

        var result = await _sender.Send(command, cancellationToken);

        _logger.LogInformation(
            "Embedding generation completed: DocumentId={DocumentId}, VersionId={VersionId}, Status={Status}, Count={Count}",
            result.DocumentId, result.VersionId, result.Status, result.EmbeddingCount);
    }
}
