using Mediator;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;

namespace OpenRAG.Application.Documents.ListDocuments;

public sealed class ListDocumentsHandler : IRequestHandler<ListDocumentsQuery, ListDocumentsResponse>
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

    public async ValueTask<ListDocumentsResponse> Handle(
        ListDocumentsQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _currentTenant.TenantId;
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

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

        return new ListDocumentsResponse(enriched, pageNumber, pageSize, result.TotalCount);
    }
}
