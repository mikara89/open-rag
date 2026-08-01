using OpenRAG.Application.Common.Results;
using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.GetJsonArtifact;

public sealed record GetJsonArtifactQuery(
    Guid DocumentId,
    Guid VersionId
) : IOpenRagQuery<Result<GetJsonArtifactResponse>>,
    IAuthenticatedApplicationMessage,
    IResultApplicationMessage;

public sealed record GetJsonArtifactResponse(
    string Content,
    string ContentType
);
