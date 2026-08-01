using OpenRAG.Application.Common.Results;
using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.GetMarkdownArtifact;

public sealed record GetMarkdownArtifactQuery(
    Guid DocumentId,
    Guid VersionId
) : IOpenRagQuery<Result<GetMarkdownArtifactResponse>>,
    IAuthenticatedApplicationMessage,
    IResultApplicationMessage;

public sealed record GetMarkdownArtifactResponse(
    string Content,
    string ContentType
);
