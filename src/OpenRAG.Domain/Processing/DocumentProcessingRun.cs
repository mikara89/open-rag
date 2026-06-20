using OpenRAG.Domain.Common;

namespace OpenRAG.Domain.Processing;

public sealed class DocumentProcessingRun : Entity
{
    public Guid TenantId { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid VersionId { get; private set; }
    public ProcessingRunReason RunReason { get; private set; }
    public DocumentProcessingRunStatus Status { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string CorrelationId { get; private set; }

    private DocumentProcessingRun(
        Guid id,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        ProcessingRunReason runReason,
        string correlationId)
        : base(id)
    {
        TenantId = GuardNotEmpty(tenantId, "TenantId");
        DocumentId = GuardNotEmpty(documentId, "DocumentId");
        VersionId = GuardNotEmpty(versionId, "VersionId");
        RunReason = runReason;
        CorrelationId = GuardNotEmpty(correlationId, "CorrelationId");
        Status = DocumentProcessingRunStatus.Pending;
        StartedAt = DateTime.UtcNow;
    }

    private DocumentProcessingRun() { } // EF Core

    public static DocumentProcessingRun Create(
        Guid id,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        ProcessingRunReason runReason,
        string correlationId)
    {
        return new DocumentProcessingRun(id, tenantId, documentId, versionId, runReason, correlationId);
    }

    public void Start()
    {
        if (Status != DocumentProcessingRunStatus.Pending)
        {
            throw new DomainException(
                $"Cannot start a processing run with status {Status}.");
        }

        Status = DocumentProcessingRunStatus.Running;
    }

    public void MarkCompleted()
    {
        if (Status != DocumentProcessingRunStatus.Running)
        {
            throw new DomainException(
                $"Cannot mark a processing run as completed when status is {Status}.");
        }

        Status = DocumentProcessingRunStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void MarkFailed()
    {
        if (Status == DocumentProcessingRunStatus.Completed)
        {
            throw new DomainException("Cannot mark a completed processing run as failed.");
        }

        Status = DocumentProcessingRunStatus.Failed;
        CompletedAt = DateTime.UtcNow;
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
