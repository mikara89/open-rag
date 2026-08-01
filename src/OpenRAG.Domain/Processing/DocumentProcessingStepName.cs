namespace OpenRAG.Domain.Processing;

public enum DocumentProcessingStepName
{
    Preprocess = 1,
    Chunk = 2,
    GenerateEmbeddings = 3,
    GenerateIntelligence = 4,
    Classify = 5,
    Summarize = 6,
    ExtractFields = 7
}
