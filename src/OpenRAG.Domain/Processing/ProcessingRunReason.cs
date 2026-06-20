namespace OpenRAG.Domain.Processing;

public enum ProcessingRunReason
{
    InitialUpload = 1,
    ManualRetry = 2,
    ReprocessWithNewPreprocessor = 3,
    ReprocessWithNewEmbeddingModel = 4,
    ReprocessWithNewExtractionSchema = 5
}
