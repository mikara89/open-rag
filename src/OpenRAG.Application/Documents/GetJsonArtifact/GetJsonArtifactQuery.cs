using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.GetJsonArtifact;

public sealed record GetJsonArtifactQuery(
    Guid DocumentId,
    Guid VersionId
) : IOpenRagQuery<GetJsonArtifactResponse>, IAuthenticatedApplicationMessage;

public sealed record GetJsonArtifactResponse(
    string Content,
    string ContentType
);
