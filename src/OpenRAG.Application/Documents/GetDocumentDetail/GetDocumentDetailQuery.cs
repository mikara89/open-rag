using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.GetDocumentDetail;

public sealed record GetDocumentDetailQuery(
    Guid DocumentId
) : IOpenRagQuery<GetDocumentDetailResponse>, IAuthenticatedApplicationMessage;

public sealed record GetDocumentDetailResponse(
    Guid DocumentId,
    string FileName,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DocumentDetailVersionDto? LatestVersion,
    DocumentDetailIntelligenceDto? Intelligence
);

public sealed record DocumentDetailVersionDto(
    Guid VersionId,
    int VersionNumber,
    bool HasSourceFile,
    bool HasMarkdownArtifact,
    bool HasJsonArtifact,
    int ChunkCount,
    int EmbeddingCount,
    string? EmbeddingProvider,
    string? EmbeddingModel,
    int? EmbeddingDimensions
);

public sealed record DocumentDetailIntelligenceDto(
    string? Classification,
    string? Summary,
    string? IntelligenceProvider,
    string? IntelligenceModel,
    DateTimeOffset? IntelligenceUpdatedAt
);
