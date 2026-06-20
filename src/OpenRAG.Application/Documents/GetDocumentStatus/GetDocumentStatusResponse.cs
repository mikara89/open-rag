namespace OpenRAG.Application.Documents.GetDocumentStatus;

public sealed record GetDocumentStatusResponse(
    Guid DocumentId,
    string Status,
    Guid? CurrentVersionId,
    string? OriginalFileName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<DocumentVersionStatusDto> Versions
);

public sealed record DocumentVersionStatusDto(
    Guid VersionId,
    int VersionNumber,
    string Status,
    string? OriginalObjectKey,
    string? MarkdownObjectKey,
    string? JsonObjectKey,
    int ChunkCount,
    int EmbeddingCount,
    string? EmbeddingProvider,
    string? EmbeddingModel,
    int? EmbeddingDimensions,
    IReadOnlyList<ProcessingStepStatusDto> Steps
);

public sealed record ProcessingStepStatusDto(
    string Name,
    string Status,
    int AttemptCount,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage
);
