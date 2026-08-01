using Mediator;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;

namespace OpenRAG.Application.Documents.ListDocumentChunks;

public sealed class ListDocumentChunksHandler : IRequestHandler<ListDocumentChunksQuery, ListDocumentChunksResponse>
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

    public async ValueTask<ListDocumentChunksResponse> Handle(
        ListDocumentChunksQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _currentTenant.TenantId;
        if (query.DocumentId == Guid.Empty || query.VersionId == Guid.Empty)
            throw new RequestValidationException("Document and version identifiers must be non-empty.");
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        // Validate document/version exist for tenant
        var version = await _documentRepository.GetVersionAsync(
            tenantId, query.DocumentId, query.VersionId, cancellationToken);

        if (version is null)
            throw new ResourceNotFoundException();

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

        return new ListDocumentChunksResponse(items, pageNumber, pageSize, result.TotalCount);
    }
}
