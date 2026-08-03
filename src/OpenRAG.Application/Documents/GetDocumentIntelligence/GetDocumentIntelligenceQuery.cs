using OpenRAG.Application.Common.Results;
using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.GetDocumentIntelligence;

public sealed record GetDocumentIntelligenceQuery(
    Guid DocumentId,
    Guid VersionId
) : IOpenRagQuery<Result<DocumentIntelligenceResponse>>,
    IAuthenticatedApplicationMessage,
    IResultApplicationMessage;
