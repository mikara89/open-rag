namespace OpenRAG.Domain.Documents;

public enum DocumentStatus
{
    Uploaded = 1,
    Processing = 2,
    Ready = 3,
    Failed = 4,
    Deleted = 5
}
