using Mediator;

namespace OpenRAG.Application.Documents.GetMarkdownArtifact;

public sealed record GetMarkdownArtifactQuery(
    Guid DocumentId,
    Guid VersionId
) : IRequest<GetMarkdownArtifactResponse>;

public sealed record GetMarkdownArtifactResponse(
    string Content,
    string ContentType
);
