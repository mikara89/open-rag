using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Processing.GenerateEmbeddings;

public sealed record GenerateEmbeddingsCommand(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    string CorrelationId
) : IOpenRagCommand<GenerateEmbeddingsResponse>,
    IExplicitTenantMessage,
    ICorrelatedMessage;

public sealed record GenerateEmbeddingsResponse(
    Guid DocumentId,
    Guid VersionId,
    int EmbeddingCount,
    string EmbeddingModel,
    int EmbeddingDimensions,
    string Status
);
