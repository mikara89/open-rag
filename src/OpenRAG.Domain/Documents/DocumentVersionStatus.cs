namespace OpenRAG.Domain.Documents;

public enum DocumentVersionStatus
{
    Uploaded = 1,
    Preprocessing = 2,
    Preprocessed = 3,
    Failed = 4,
    Deleted = 5
}
