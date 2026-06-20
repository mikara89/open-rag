using Mediator;

namespace OpenRAG.Application.Documents.GetDocumentStatus;

public sealed record GetDocumentStatusQuery(
    Guid DocumentId,
    Guid TenantId
) : IRequest<GetDocumentStatusResponse>;
