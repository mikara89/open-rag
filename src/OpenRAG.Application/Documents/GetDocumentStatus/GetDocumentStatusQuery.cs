using OpenRAG.Application.Common.Results;
using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.GetDocumentStatus;

public sealed record GetDocumentStatusQuery(Guid DocumentId)
    : IOpenRagQuery<Result<GetDocumentStatusResponse>>,
      IAuthenticatedApplicationMessage,
      IResultApplicationMessage;
