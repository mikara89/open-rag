using OpenRAG.Application.Common.Results;
using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Rag.AskQuestion;

public sealed record AskQuestionQuery(
    string Question,
    IReadOnlyCollection<Guid>? FilterDocumentIds,
    int? TopK,
    string Model,
    string CorrelationId
) : IOpenRagQuery<Result<AskQuestionResponse>>,
    IAuthenticatedApplicationMessage,
    IResultApplicationMessage,
    ICorrelatedMessage;
