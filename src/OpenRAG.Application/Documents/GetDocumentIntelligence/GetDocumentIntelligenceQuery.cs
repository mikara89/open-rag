using Mediator;

namespace OpenRAG.Application.Documents.GetDocumentIntelligence;

public sealed record GetDocumentIntelligenceQuery(
    Guid DocumentId,
    Guid VersionId
) : IRequest<DocumentIntelligenceResponse?>;
