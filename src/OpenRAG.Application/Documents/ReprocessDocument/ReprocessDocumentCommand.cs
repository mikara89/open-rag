using OpenRAG.Application.Common.Results;
using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.ReprocessDocument;

public sealed record ReprocessDocumentCommand(
    Guid DocumentId,
    bool ForcePreprocess,
    bool ForceChunk,
    bool ForceIntelligence,
    bool ForceEmbeddings,
    string CorrelationId
) : IOpenRagCommand<Result<ReprocessDocumentResponse>>,
    IAuthenticatedApplicationMessage,
    IResultApplicationMessage,
    ICorrelatedMessage;

public sealed record ReprocessDocumentResponse(
    Guid DocumentId,
    Guid VersionId,
    string Status,
    string CorrelationId
);
