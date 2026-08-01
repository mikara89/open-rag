using OpenRAG.Application.Common.Results;
using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.ListDocuments;

public sealed record ListDocumentsQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Status = null,
    string? Search = null
) : IOpenRagQuery<Result<ListDocumentsResponse>>,
    IAuthenticatedApplicationMessage,
    IResultApplicationMessage;

public sealed record ListDocumentsResponse(
    IReadOnlyList<ListDocumentsItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount
);

public sealed record ListDocumentsItem(
    Guid DocumentId,
    string FileName,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? LatestVersionId,
    int ChunkCount,
    int EmbeddingCount
);
