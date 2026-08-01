using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.DeleteDocument;

public sealed record DeleteDocumentCommand(
    Guid DocumentId
) : IOpenRagCommand<DeleteDocumentResponse>, IAuthenticatedApplicationMessage;

public sealed record DeleteDocumentResponse(
    Guid DocumentId,
    bool Deleted
);
