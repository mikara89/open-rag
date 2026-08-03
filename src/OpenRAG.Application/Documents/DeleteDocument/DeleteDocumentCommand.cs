using OpenRAG.Application.Common.Results;
using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.DeleteDocument;

public sealed record DeleteDocumentCommand(
    Guid DocumentId
) : IOpenRagCommand<Result<DeleteDocumentResponse>>,
    IAuthenticatedApplicationMessage,
    IResultApplicationMessage;

public sealed record DeleteDocumentResponse(
    Guid DocumentId,
    bool Deleted
);
