using Mediator;

namespace OpenRAG.Application.Documents.GetDocumentStatus;

public sealed record GetDocumentStatusQuery(Guid DocumentId) : IRequest<GetDocumentStatusResponse>;
