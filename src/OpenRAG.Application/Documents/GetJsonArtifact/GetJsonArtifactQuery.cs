using Mediator;

namespace OpenRAG.Application.Documents.GetJsonArtifact;

public sealed record GetJsonArtifactQuery(
    Guid DocumentId,
    Guid VersionId
) : IRequest<GetJsonArtifactResponse>;

public sealed record GetJsonArtifactResponse(
    string Content,
    string ContentType
);
