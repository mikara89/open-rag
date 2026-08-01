using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.GetMarkdownArtifact;

public sealed record GetMarkdownArtifactQuery(
    Guid DocumentId,
    Guid VersionId
) : IOpenRagQuery<GetMarkdownArtifactResponse>, IAuthenticatedApplicationMessage;

public sealed record GetMarkdownArtifactResponse(
    string Content,
    string ContentType
);
