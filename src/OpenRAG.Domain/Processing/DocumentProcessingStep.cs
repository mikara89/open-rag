using OpenRAG.Domain.Common;

namespace OpenRAG.Domain.Processing;

public sealed class DocumentProcessingStep : Entity
{
    public Guid TenantId { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid VersionId { get; private set; }
    public Guid ProcessingRunId { get; private set; }
    public DocumentProcessingStepName StepName { get; private set; }
    public DocumentProcessingStepStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public string InputHash { get; private set; }
    public string? OutputHash { get; private set; }
    public string ProcessorName { get; private set; }
    public string ProcessorVersion { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastErrorMessage { get; private set; }

    private DocumentProcessingStep(
        Guid id,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        Guid processingRunId,
        DocumentProcessingStepName stepName,
        int maxAttempts,
        string inputHash,
        string processorName,
        string processorVersion)
        : base(id)
    {
        TenantId = GuardNotEmpty(tenantId, "TenantId");
        DocumentId = GuardNotEmpty(documentId, "DocumentId");
        VersionId = GuardNotEmpty(versionId, "VersionId");
        ProcessingRunId = GuardNotEmpty(processingRunId, "ProcessingRunId");
        StepName = stepName;
        MaxAttempts = GuardPositive(maxAttempts, "MaxAttempts");
        InputHash = GuardNotEmpty(inputHash, "InputHash");
        ProcessorName = GuardNotEmpty(processorName, "ProcessorName");
        ProcessorVersion = GuardNotEmpty(processorVersion, "ProcessorVersion");
        Status = DocumentProcessingStepStatus.Pending;
        AttemptCount = 0;
    }

    private DocumentProcessingStep() { } // EF Core

    public static DocumentProcessingStep Create(
        Guid id,
        Guid tenantId,
        Guid documentId,
        Guid versionId,
        Guid processingRunId,
        DocumentProcessingStepName stepName,
        int maxAttempts,
        string inputHash,
        string processorName,
        string processorVersion)
    {
        return new DocumentProcessingStep(
            id, tenantId, documentId, versionId, processingRunId,
            stepName, maxAttempts, inputHash, processorName, processorVersion);
    }

    public bool CanRetry => Status == DocumentProcessingStepStatus.Failed
                            && AttemptCount < MaxAttempts;

    public void Start()
    {
        if (Status != DocumentProcessingStepStatus.Pending
            && Status != DocumentProcessingStepStatus.Failed)
        {
            throw new DomainException(
                $"Cannot start a step with status {Status}.");
        }

        if (AttemptCount >= MaxAttempts)
        {
            throw new DomainException(
                $"Cannot start step: attempt count {AttemptCount} has reached max {MaxAttempts}.");
        }

        AttemptCount++;
        Status = DocumentProcessingStepStatus.Running;
        StartedAt = DateTime.UtcNow;
    }

    public void MarkCompleted(string outputHash)
    {
        if (Status != DocumentProcessingStepStatus.Running)
        {
            throw new DomainException(
                $"Cannot mark step as completed when it has not been started. Current status: {Status}.");
        }

        OutputHash = GuardNotEmpty(outputHash, nameof(outputHash));
        Status = DocumentProcessingStepStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string errorCode, string errorMessage)
    {
        if (Status != DocumentProcessingStepStatus.Running)
        {
            throw new DomainException(
                $"Cannot mark step as failed when status is {Status}.");
        }

        Status = DocumentProcessingStepStatus.Failed;
        LastErrorCode = errorCode;
        LastErrorMessage = errorMessage;
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

    private static int GuardPositive(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new DomainException($"{paramName} must be greater than zero.");
        }

        return value;
    }
}
