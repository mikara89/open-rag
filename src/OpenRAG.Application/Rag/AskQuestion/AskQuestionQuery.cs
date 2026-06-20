using Mediator;

namespace OpenRAG.Application.Rag.AskQuestion;

public sealed record AskQuestionQuery(
    string Question,
    Guid TenantId,
    IReadOnlyCollection<Guid>? FilterDocumentIds,
    int TopK,
    string Model,
    string CorrelationId
) : IRequest<AskQuestionResponse>;
