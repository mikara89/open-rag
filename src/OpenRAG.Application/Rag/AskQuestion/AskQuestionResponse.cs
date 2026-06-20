namespace OpenRAG.Application.Rag.AskQuestion;

public sealed record AskQuestionResponse(
    string Answer,
    IReadOnlyList<RagCitationDto> Citations,
    IReadOnlyList<RagRetrievedChunkDto> RetrievedChunks,
    string Model,
    decimal? EstimatedCost
);
