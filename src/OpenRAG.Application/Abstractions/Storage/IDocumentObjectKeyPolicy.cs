namespace OpenRAG.Application.Abstractions.Storage;

public enum DocumentObjectKind
{
    Source,
    Markdown,
    Json
}

public interface IDocumentObjectKeyPolicy
{
    string BuildSourceKey(Guid tenantId, Guid documentId, Guid versionId, string fileName);

    string BuildArtifactKey(
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        DocumentObjectKind kind);

    void EnsureOwned(
        string objectKey,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        DocumentObjectKind kind);
}
