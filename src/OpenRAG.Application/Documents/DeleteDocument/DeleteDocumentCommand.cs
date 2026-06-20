using Mediator;

namespace OpenRAG.Application.Documents.DeleteDocument;

public sealed record DeleteDocumentCommand(
    Guid DocumentId
) : IRequest<DeleteDocumentResponse>;

public sealed record DeleteDocumentResponse(
    Guid DocumentId,
    bool Deleted
);
