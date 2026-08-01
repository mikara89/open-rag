using DotNetCore.CAP;
using Mediator;
using OpenRAG.Application.Messaging.Events;
using OpenRAG.Application.Processing.ChunkDocument;

namespace OpenRAG.Worker.Consumers;

/// <summary>
/// Consumes document.chunking.requested events.
/// Dispatches chunking via Mediator.
/// </summary>
public sealed class DocumentChunkingRequestedConsumer : ICapSubscribe
{
    private readonly ISender _sender;
    private readonly ILogger<DocumentChunkingRequestedConsumer> _logger;

    public DocumentChunkingRequestedConsumer(
        ISender sender,
        ILogger<DocumentChunkingRequestedConsumer> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    [CapSubscribe("document.chunking.requested")]
    public async Task HandleAsync(
        DocumentChunkingRequestedEvent message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Received document.chunking.requested: DocumentId={DocumentId}, VersionId={VersionId}, MarkdownKey={MarkdownKey}",
            message.DocumentId, message.VersionId, message.MarkdownObjectKey);

        // Dispatch to Application handler via Mediator
        var command = new ChunkDocumentCommand(
            TenantId: message.TenantId,
            DocumentId: message.DocumentId,
            VersionId: message.VersionId,
            ProcessingRunId: message.ProcessingRunId,
            CorrelationId: message.CorrelationId);

        var result = await _sender.Send(command, cancellationToken);

        _logger.LogInformation(
            "Chunking completed: DocumentId={DocumentId}, VersionId={VersionId}, Status={Status}, ChunkCount={ChunkCount}",
            result.DocumentId, result.VersionId, result.Status, result.ChunkCount);
    }
}
