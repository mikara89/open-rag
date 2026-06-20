using Mediator;

namespace OpenRAG.Application.Documents.ReprocessDocument;

public sealed record ReprocessDocumentCommand(
    Guid TenantId,
    Guid DocumentId,
    bool ForcePreprocess,
    bool ForceChunk,
    bool ForceEmbeddings,
    string CorrelationId
) : IRequest<ReprocessDocumentResponse>;

public sealed record ReprocessDocumentResponse(
    Guid DocumentId,
    Guid VersionId,
    string Status,
    string CorrelationId
);
