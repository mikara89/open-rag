namespace OpenRAG.Application.Rag.AskQuestion;

public sealed record RagCitationDto(
    int Index,
    Guid DocumentId,
    Guid ChunkId,
    string Excerpt,
    int? PageNumber,
    string? SectionTitle
);
