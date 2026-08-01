using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.GetDocumentIntelligence;

public sealed record GetDocumentIntelligenceQuery(
    Guid DocumentId,
    Guid VersionId
) : IOpenRagQuery<DocumentIntelligenceResponse>, IAuthenticatedApplicationMessage;
