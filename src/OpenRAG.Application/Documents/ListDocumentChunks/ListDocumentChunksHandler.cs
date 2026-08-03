using Mediator;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;
using OpenRAG.Application.Common.Results;

namespace OpenRAG.Application.Documents.ListDocumentChunks;

public sealed class ListDocumentChunksHandler
    : IRequestHandler<ListDocumentChunksQuery, Result<ListDocumentChunksResponse>>
{
    private const int MaxPageSize = 100;
    private const int PreviewLength = 300;

    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly ICurrentTenant _currentTenant;

    public ListDocumentChunksHandler(
        IDocumentChunkRepository chunkRepository,
        IDocumentRepository documentRepository,
        ICurrentTenant currentTenant)
    {
        _chunkRepository = chunkRepository;
        _documentRepository = documentRepository;
        _currentTenant = currentTenant;
    }

    public async ValueTask<Result<ListDocumentChunksResponse>> Handle(
        ListDocumentChunksQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.DocumentId == Guid.Empty || query.VersionId == Guid.Empty)
        {
            var error = query.DocumentId == Guid.Empty
                ? ApplicationErrors.InvalidRequest(
                    "request.document_id_required", "DocumentId cannot be empty.", "documentId")
                : ApplicationErrors.InvalidRequest(
                    "request.version_id_required", "VersionId cannot be empty.", "versionId");
            return Result<ListDocumentChunksResponse>.Failure(error);
        }

        if (query.PageNumber <= 0)
        {
            return Result<ListDocumentChunksResponse>.Failure(
                ApplicationErrors.InvalidRequest(
                    "request.page_number_invalid",
                    "Page number must be greater than zero.",
                    "pageNumber"));
        }

        if (query.PageSize <= 0 || query.PageSize > MaxPageSize)
        {
            return Result<ListDocumentChunksResponse>.Failure(
                ApplicationErrors.InvalidRequest(
                    "request.page_size_invalid",
                    $"Page size must be between 1 and {MaxPageSize}.",
                    "pageSize"));
        }

        if (query.PageNumberFilter <= 0)
        {
            return Result<ListDocumentChunksResponse>.Failure(
                ApplicationErrors.InvalidRequest(
                    "request.page_number_filter_invalid",
                    "Page number filter must be greater than zero.",
                    "pageNumberFilter"));
        }

        var tenantId = _currentTenant.TenantId;
        var pageNumber = query.PageNumber;
        var pageSize = query.PageSize;

        // Validate document/version exist for tenant
        var version = await _documentRepository.GetVersionAsync(
            tenantId, query.DocumentId, query.VersionId, cancellationToken);

        if (version is null)
            return Result<ListDocumentChunksResponse>.Failure(ApplicationErrors.ResourceNotFound());

        IsolationGuard.Equal(version.TenantId, tenantId, nameof(version.TenantId));
        IsolationGuard.Equal(version.DocumentId, query.DocumentId, nameof(version.DocumentId));
        IsolationGuard.Equal(version.Id, query.VersionId, nameof(version.Id));

        var result = await _chunkRepository.ListByVersionAsync(
            tenantId, query.DocumentId, query.VersionId,
            pageNumber, pageSize,
            query.Search, query.SectionTitle, query.PageNumberFilter,
            cancellationToken);

        foreach (var chunk in result.Items)
        {
            IsolationGuard.Equal(chunk.TenantId, tenantId, nameof(chunk.TenantId));
            IsolationGuard.Equal(chunk.DocumentId, query.DocumentId, nameof(chunk.DocumentId));
            IsolationGuard.Equal(chunk.VersionId, query.VersionId, nameof(chunk.VersionId));
            IsolationGuard.NonEmpty(chunk.Id, nameof(chunk.Id));
        }

        var items = result.Items.Select(c => new DocumentChunkItemDto(
            ChunkId: c.Id,
            ChunkIndex: c.ChunkIndex,
            Content: c.Content,
            ContentPreview: c.Content.Length > PreviewLength
                ? c.Content[..PreviewLength] + "..."
                : c.Content,
            ContentHash: c.ContentHash,
            TokenCount: c.TokenCount,
            SectionTitle: c.SectionTitle,
            PageNumber: c.PageNumber,
            CreatedAt: c.CreatedAt
        )).ToList();

        return Result<ListDocumentChunksResponse>.Success(
            new ListDocumentChunksResponse(items, pageNumber, pageSize, result.TotalCount));
    }
}
