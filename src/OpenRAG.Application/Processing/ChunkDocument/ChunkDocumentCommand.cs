using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Processing.ChunkDocument;

public sealed record ChunkDocumentCommand(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    string CorrelationId
) : IOpenRagCommand<ChunkDocumentResponse>,
    IExplicitTenantMessage,
    ICorrelatedMessage;

public sealed record ChunkDocumentResponse(
    Guid DocumentId,
    Guid VersionId,
    int ChunkCount,
    string Status
);
