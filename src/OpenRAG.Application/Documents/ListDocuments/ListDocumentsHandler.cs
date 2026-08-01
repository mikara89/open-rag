using Mediator;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common.Results;

namespace OpenRAG.Application.Documents.ListDocuments;

public sealed class ListDocumentsHandler
    : IRequestHandler<ListDocumentsQuery, Result<ListDocumentsResponse>>
{
    private const int MaxPageSize = 100;

    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentEmbeddingRepository _embeddingRepository;
    private readonly ICurrentTenant _currentTenant;

    public ListDocumentsHandler(
        IDocumentRepository documentRepository,
        IDocumentChunkRepository chunkRepository,
        IDocumentEmbeddingRepository embeddingRepository,
        ICurrentTenant currentTenant)
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _embeddingRepository = embeddingRepository;
        _currentTenant = currentTenant;
    }

    public async ValueTask<Result<ListDocumentsResponse>> Handle(
        ListDocumentsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.PageNumber <= 0)
        {
            return Result<ListDocumentsResponse>.Failure(
                ApplicationErrors.InvalidRequest(
                    "request.page_number_invalid",
                    "Page number must be greater than zero.",
                    "pageNumber"));
        }

        if (query.PageSize <= 0 || query.PageSize > MaxPageSize)
        {
            return Result<ListDocumentsResponse>.Failure(
                ApplicationErrors.InvalidRequest(
                    "request.page_size_invalid",
                    $"Page size must be between 1 and {MaxPageSize}.",
                    "pageSize"));
        }

        var tenantId = _currentTenant.TenantId;
        var pageNumber = query.PageNumber;
        var pageSize = query.PageSize;

        var result = await _documentRepository.ListAsync(
            tenantId, pageNumber, pageSize, query.Status, query.Search, cancellationToken);

        // Enrich with chunk/embedding counts
        var enriched = new List<ListDocumentsItem>();
        foreach (var item in result.Items)
        {
            var versionId = item.LatestVersionId ?? Guid.Empty;
            var chunkCount = versionId != Guid.Empty
                ? await _chunkRepository.CountByVersionAsync(tenantId, item.DocumentId, versionId, cancellationToken)
                : 0;
            var embeddingCount = versionId != Guid.Empty
                ? await _embeddingRepository.CountByVersionAsync(tenantId, item.DocumentId, versionId, cancellationToken)
                : 0;

            enriched.Add(new ListDocumentsItem(
                item.DocumentId,
                item.FileName,
                item.Status,
                item.CreatedAt,
                item.UpdatedAt,
                item.LatestVersionId,
                chunkCount,
                embeddingCount));
        }

        return Result<ListDocumentsResponse>.Success(
            new ListDocumentsResponse(enriched, pageNumber, pageSize, result.TotalCount));
    }
}
