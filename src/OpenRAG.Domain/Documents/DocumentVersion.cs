using OpenRAG.Domain.Common;

namespace OpenRAG.Domain.Documents;

public sealed class DocumentVersion : Entity
{
    public Guid TenantId { get; private set; }
    public Guid DocumentId { get; private set; }
    public int VersionNumber { get; private set; }
    public string OriginalObjectKey { get; private set; }
    public string OriginalContentType { get; private set; }
    public long OriginalSizeBytes { get; private set; }
    public string OriginalSha256 { get; private set; }
    public string? DoclingMarkdownObjectKey { get; private set; }
    public string? DoclingJsonObjectKey { get; private set; }
    public DocumentVersionStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private DocumentVersion(
        Guid id,
        Guid tenantId,
        Guid documentId,
        int versionNumber,
        string originalObjectKey,
        string originalContentType,
        long originalSizeBytes,
        string originalSha256)
        : base(id)
    {
        TenantId = GuardNotEmpty(tenantId, "TenantId");
        DocumentId = GuardNotEmpty(documentId, "DocumentId");
        VersionNumber = GuardPositive(versionNumber, "VersionNumber");
        OriginalObjectKey = GuardNotEmpty(originalObjectKey, "OriginalObjectKey");
        OriginalContentType = GuardNotEmpty(originalContentType, "OriginalContentType");
        OriginalSizeBytes = GuardPositive(originalSizeBytes, "OriginalSizeBytes");
        OriginalSha256 = GuardNotEmpty(originalSha256, "OriginalSha256");
        Status = DocumentVersionStatus.Uploaded;
        CreatedAt = DateTime.UtcNow;
    }

    private DocumentVersion() { } // EF Core

    public static DocumentVersion Create(
        Guid id,
        Guid tenantId,
        Guid documentId,
        int versionNumber,
        string originalObjectKey,
        string originalContentType,
        long originalSizeBytes,
        string originalSha256)
    {
        return new DocumentVersion(
            id, tenantId, documentId, versionNumber,
            originalObjectKey, originalContentType, originalSizeBytes, originalSha256);
    }

    public void AttachDoclingArtifacts(string markdownObjectKey, string jsonObjectKey)
    {
        if (Status == DocumentVersionStatus.Deleted)
        {
            throw new DomainException("Cannot attach artifacts to a deleted version.");
        }

        DoclingMarkdownObjectKey = GuardNotEmpty(markdownObjectKey, nameof(markdownObjectKey));
        DoclingJsonObjectKey = GuardNotEmpty(jsonObjectKey, nameof(jsonObjectKey));
        Status = DocumentVersionStatus.Preprocessing;
    }

    public void MarkPreprocessed()
    {
        if (string.IsNullOrWhiteSpace(DoclingMarkdownObjectKey)
            || string.IsNullOrWhiteSpace(DoclingJsonObjectKey))
        {
            throw new DomainException(
                "Cannot mark version as preprocessed without both Markdown and JSON object keys.");
        }

        if (Status != DocumentVersionStatus.Preprocessing)
        {
            throw new DomainException(
                $"Cannot transition version from {Status} to Preprocessed.");
        }

        Status = DocumentVersionStatus.Preprocessed;
    }

    public void MarkFailed()
    {
        if (Status == DocumentVersionStatus.Deleted)
        {
            throw new DomainException("Cannot mark a deleted version as failed.");
        }

        Status = DocumentVersionStatus.Failed;
    }

    private static string GuardNotEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{paramName} cannot be empty.");
        }

        return value;
    }

    private static Guid GuardNotEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException($"{paramName} cannot be empty.");
        }

        return value;
    }

    private static int GuardPositive(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new DomainException($"{paramName} must be greater than zero.");
        }

        return value;
    }

    private static long GuardPositive(long value, string paramName)
    {
        if (value <= 0)
        {
            throw new DomainException($"{paramName} must be greater than zero.");
        }

        return value;
    }
}
