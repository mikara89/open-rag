using Mediator;

namespace OpenRAG.Application.Processing.GenerateEmbeddings;

public sealed record GenerateEmbeddingsCommand(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    string CorrelationId
) : IRequest<GenerateEmbeddingsResponse>;

public sealed record GenerateEmbeddingsResponse(
    Guid DocumentId,
    Guid VersionId,
    int EmbeddingCount,
    string EmbeddingModel,
    int EmbeddingDimensions,
    string Status
);
