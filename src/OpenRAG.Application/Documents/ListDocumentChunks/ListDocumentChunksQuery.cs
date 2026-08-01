using OpenRAG.Application.Common.Results;
using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.ListDocumentChunks;

public sealed record ListDocumentChunksQuery(
    Guid DocumentId,
    Guid VersionId,
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    string? SectionTitle = null,
    int? PageNumberFilter = null
) : IOpenRagQuery<Result<ListDocumentChunksResponse>>,
    IAuthenticatedApplicationMessage,
    IResultApplicationMessage;

public sealed record ListDocumentChunksResponse(
    IReadOnlyList<DocumentChunkItemDto> Items,
    int PageNumber,
    int PageSize,
    int TotalCount
);

public sealed record DocumentChunkItemDto(
    Guid ChunkId,
    int ChunkIndex,
    string Content,
    string ContentPreview,
    string ContentHash,
    int TokenCount,
    string? SectionTitle,
    int? PageNumber,
    DateTimeOffset CreatedAt
);
