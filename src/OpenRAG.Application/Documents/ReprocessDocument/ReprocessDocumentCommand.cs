using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.ReprocessDocument;

public sealed record ReprocessDocumentCommand(
    Guid DocumentId,
    bool ForcePreprocess,
    bool ForceChunk,
    bool ForceIntelligence,
    bool ForceEmbeddings,
    string CorrelationId
) : IOpenRagCommand<ReprocessDocumentResponse>,
    IAuthenticatedApplicationMessage,
    ICorrelatedMessage;

public sealed record ReprocessDocumentResponse(
    Guid DocumentId,
    Guid VersionId,
    string Status,
    string CorrelationId
);
