using OpenRAG.Domain.Common;

namespace OpenRAG.Domain.Documents;

public sealed class Document : Entity
{
    private readonly List<DocumentVersion> _versions = [];

    public Guid TenantId { get; private set; }
    public string Title { get; private set; }
    public string OriginalFileName { get; private set; }
    public Guid? CurrentVersionId { get; private set; }
    public DocumentStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    public IReadOnlyCollection<DocumentVersion> Versions => _versions.AsReadOnly();

    private Document(
        Guid id,
        Guid tenantId,
        string title,
        string originalFileName,
        Guid createdByUserId)
        : base(id)
    {
        TenantId = GuardNotEmpty(tenantId, "TenantId");
        Title = GuardNotEmpty(title, "Title");
        OriginalFileName = GuardNotEmpty(originalFileName, "OriginalFileName");
        CreatedByUserId = GuardNotEmpty(createdByUserId, "CreatedByUserId");
        Status = DocumentStatus.Uploaded;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    private Document() { } // EF Core

    public static Document Create(
        Guid id,
        Guid tenantId,
        string title,
        string originalFileName,
        Guid createdByUserId)
    {
        return new Document(id, tenantId, title, originalFileName, createdByUserId);
    }

    public DocumentVersion AddVersion(
        Guid versionId,
        int versionNumber,
        string originalObjectKey,
        string originalContentType,
        long originalSizeBytes,
        string originalSha256)
    {
        if (Status == DocumentStatus.Deleted)
        {
            throw new DomainException("Cannot add a version to a deleted document.");
        }

        var version = DocumentVersion.Create(
            versionId,
            TenantId,
            Id,
            versionNumber,
            originalObjectKey,
            originalContentType,
            originalSizeBytes,
            originalSha256);

        _versions.Add(version);
        CurrentVersionId = version.Id;
        UpdatedAt = DateTime.UtcNow;

        return version;
    }

    public DocumentVersion GetCurrentVersion()
    {
        if (CurrentVersionId is null)
        {
            throw new DomainException("Document has no version.");
        }

        return _versions.First(v => v.Id == CurrentVersionId.Value);
    }

    public void MarkProcessing()
    {
        if (Status == DocumentStatus.Deleted)
        {
            throw new DomainException("Cannot mark a deleted document as processing.");
        }

        if (Status != DocumentStatus.Uploaded && Status != DocumentStatus.Failed)
        {
            throw new DomainException(
                $"Cannot transition document from {Status} to Processing.");
        }

        Status = DocumentStatus.Processing;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkReady()
    {
        if (Status == DocumentStatus.Deleted)
        {
            throw new DomainException("Cannot mark a deleted document as ready.");
        }

        if (Status != DocumentStatus.Processing)
        {
            throw new DomainException(
                $"Cannot transition document from {Status} to Ready.");
        }

        Status = DocumentStatus.Ready;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkFailed()
    {
        if (Status == DocumentStatus.Deleted)
        {
            throw new DomainException("Cannot mark a deleted document as failed.");
        }

        Status = DocumentStatus.Failed;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SoftDelete()
    {
        if (Status == DocumentStatus.Deleted)
        {
            throw new DomainException("Document is already deleted.");
        }

        Status = DocumentStatus.Deleted;
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
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
}
