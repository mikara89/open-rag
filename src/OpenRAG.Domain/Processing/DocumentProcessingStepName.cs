namespace OpenRAG.Domain.Processing;

public enum DocumentProcessingStepName
{
    Preprocess = 1,
    Chunk = 2,
    GenerateEmbeddings = 3,
    Classify = 4,
    Summarize = 5,
    ExtractFields = 6
}
