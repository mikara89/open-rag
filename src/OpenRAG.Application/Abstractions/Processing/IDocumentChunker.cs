namespace OpenRAG.Application.Abstractions.Processing;

public sealed record DocumentChunkingRequest(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    string Markdown,
    string? DoclingJson,
    string CorrelationId
);

public sealed record DocumentChunkingResultItem(
    int ChunkIndex,
    string Content,
    string ContentHash,
    int TokenCount,
    int? PageNumber,
    string? SectionTitle
);

public interface IDocumentChunker
{
    Task<IReadOnlyList<DocumentChunkingResultItem>> ChunkAsync(
        DocumentChunkingRequest request,
        CancellationToken cancellationToken = default);
}
