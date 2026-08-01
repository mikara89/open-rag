using Mediator;

namespace OpenRAG.Application.Documents.ListDocuments;

public sealed record ListDocumentsQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Status = null,
    string? Search = null
) : IRequest<ListDocumentsResponse>;

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
