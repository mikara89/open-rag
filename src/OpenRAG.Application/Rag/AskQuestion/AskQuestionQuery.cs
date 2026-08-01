using Mediator;

namespace OpenRAG.Application.Rag.AskQuestion;

public sealed record AskQuestionQuery(
    string Question,
    IReadOnlyCollection<Guid>? FilterDocumentIds,
    int? TopK,
    string Model,
    string CorrelationId
) : IRequest<AskQuestionResponse>;
