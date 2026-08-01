using OpenRAG.Application.Pipeline;

namespace OpenRAG.Application.Documents.GetDocumentChunk;

public sealed record GetDocumentChunkQuery(
    Guid DocumentId,
    Guid VersionId,
    Guid ChunkId
) : IOpenRagQuery<GetDocumentChunkResponse>, IAuthenticatedApplicationMessage;

public sealed record GetDocumentChunkResponse(
    Guid ChunkId,
    int ChunkIndex,
    string Content,
    string ContentHash,
    int TokenCount,
    string? SectionTitle,
    int? PageNumber,
    DateTimeOffset CreatedAt,
    string? EmbeddingProvider,
    string? EmbeddingModel,
    int? EmbeddingDimensions,
    bool HasEmbedding
);
