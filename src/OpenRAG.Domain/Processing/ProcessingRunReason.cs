namespace OpenRAG.Domain.Processing;

public enum ProcessingRunReason
{
    InitialUpload = 1,
    ManualRetry = 2,
    ReprocessWithNewPreprocessor = 3,
    ReprocessWithNewEmbeddingModel = 4,
    ReprocessWithNewIntelligenceModel = 5,
    ReprocessWithNewExtractionSchema = 6
}
