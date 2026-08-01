namespace OpenRAG.Application.Documents.GetDocumentStatus;

public sealed record GetDocumentStatusResponse(
    Guid DocumentId,
    string Status,
    Guid? CurrentVersionId,
    string? OriginalFileName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<DocumentVersionStatusDto> Versions,
    IReadOnlyList<ProcessingRunHistoryDto> ProcessingRuns
);

public sealed record ProcessingRunHistoryDto(
    Guid RunId,
    string Reason,
    string Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string CorrelationId,
    IReadOnlyList<ProcessingStepHistoryDto> Steps
);

public sealed record ProcessingStepHistoryDto(
    string Name,
    string Status,
    int AttemptCount,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    bool HasError
);

public sealed record DocumentVersionStatusDto(
    Guid VersionId,
    int VersionNumber,
    string Status,
    bool HasSourceFile,
    bool HasMarkdownArtifact,
    bool HasJsonArtifact,
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
    bool HasError
);
