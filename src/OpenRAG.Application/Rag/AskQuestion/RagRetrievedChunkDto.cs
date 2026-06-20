namespace OpenRAG.Application.Rag.AskQuestion;

public sealed record RagRetrievedChunkDto(
    Guid ChunkId,
    Guid DocumentId,
    Guid VersionId,
    string Content,
    int? PageNumber,
    string? SectionTitle,
    double Score
);
