using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Processing.GenerateIntelligence;

public sealed record GenerateIntelligenceCommand(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    string CorrelationId
) : IOpenRagCommand<GenerateIntelligenceResponse>,
    IExplicitTenantMessage,
    ICorrelatedMessage;

public sealed record GenerateIntelligenceResponse(
    Guid DocumentId,
    Guid VersionId,
    string Status,
    string? Provider,
    string? Model
);
