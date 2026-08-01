using Mediator;

namespace OpenRAG.Application.Processing.GenerateIntelligence;

public sealed record GenerateIntelligenceCommand(
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    string CorrelationId
) : IRequest<GenerateIntelligenceResponse>;

public sealed record GenerateIntelligenceResponse(
    Guid DocumentId,
    Guid VersionId,
    string Status,
    string? Provider,
    string? Model
);
