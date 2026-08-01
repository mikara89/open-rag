using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Processing.PreprocessDocument;

public sealed record PreprocessDocumentResponse(
    Guid DocumentId,
    Guid VersionId,
    string MarkdownObjectKey,
    string JsonObjectKey,
    string Status
);

public sealed record PreprocessDocumentCommand(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    string CorrelationId
) : IOpenRagCommand<PreprocessDocumentResponse>,
    IExplicitTenantMessage,
    ICorrelatedMessage;
