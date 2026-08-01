using OpenRAG.Domain.Common;
using OpenRAG.Domain.Processing;

namespace OpenRAG.UnitTests.Domain.Processing;

public sealed class DocumentProcessingStepTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid VersionId = Guid.NewGuid();
    private static readonly Guid RunId = Guid.NewGuid();

    [Fact]
    public void Cannot_be_created_with_empty_TenantId()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentProcessingStep.Create(
                Guid.NewGuid(), Guid.Empty, DocumentId, VersionId, RunId,
                DocumentProcessingStepName.Preprocess, 3,
                "input-hash", "Processor", "1.0"));

        Assert.Contains("TenantId", ex.Message);
    }

    [Fact]
    public void Cannot_be_created_with_empty_DocumentId()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentProcessingStep.Create(
                Guid.NewGuid(), TenantId, Guid.Empty, VersionId, RunId,
                DocumentProcessingStepName.Preprocess, 3,
                "input-hash", "Processor", "1.0"));

        Assert.Contains("DocumentId", ex.Message);
    }

    [Fact]
    public void Cannot_be_created_with_empty_VersionId()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentProcessingStep.Create(
                Guid.NewGuid(), TenantId, DocumentId, Guid.Empty, RunId,
                DocumentProcessingStepName.Preprocess, 3,
                "input-hash", "Processor", "1.0"));

        Assert.Contains("VersionId", ex.Message);
    }

    [Fact]
    public void Cannot_be_created_with_empty_ProcessingRunId()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentProcessingStep.Create(
                Guid.NewGuid(), TenantId, DocumentId, VersionId, Guid.Empty,
                DocumentProcessingStepName.Preprocess, 3,
                "input-hash", "Processor", "1.0"));

        Assert.Contains("ProcessingRunId", ex.Message);
    }

    [Fact]
    public void Can_start_and_complete()
    {
        var step = CreateStep();

        step.Start();
        Assert.Equal(DocumentProcessingStepStatus.Running, step.Status);
        Assert.Equal(1, step.AttemptCount);

        step.MarkCompleted("output-hash-123");
        Assert.Equal(DocumentProcessingStepStatus.Completed, step.Status);
        Assert.Equal("output-hash-123", step.OutputHash);
    }

    [Fact]
    public void Cannot_complete_before_start()
    {
        var step = CreateStep();

        var ex = Assert.Throws<DomainException>(() => step.MarkCompleted("hash"));
        Assert.Contains("not been started", ex.Message);
    }

    [Fact]
    public void Cannot_retry_after_completed()
    {
        var step = CreateStep();
        step.Start();
        step.MarkCompleted("hash");

        var ex = Assert.Throws<DomainException>(() => step.Start());
        Assert.Contains("Completed", ex.Message);
    }

    [Fact]
    public void Can_retry_after_failed_if_under_max_attempts()
    {
        var step = CreateStep(maxAttempts: 3);
        step.Start();
        step.MarkFailed("ERR_TIMEOUT", "Connection timed out");

        Assert.Equal(DocumentProcessingStepStatus.Failed, step.Status);
        Assert.True(step.CanRetry);

        step.Start(); // retry
        Assert.Equal(DocumentProcessingStepStatus.Running, step.Status);
        Assert.Equal(2, step.AttemptCount);
    }

    [Fact]
    public void Cannot_exceed_max_attempts()
    {
        var step = CreateStep(maxAttempts: 2);

        // Attempt 1
        step.Start();
        step.MarkFailed("ERR", "fail");
        Assert.True(step.CanRetry);

        // Attempt 2
        step.Start();
        step.MarkFailed("ERR", "fail");
        Assert.False(step.CanRetry);

        // Attempt 3 should throw
        var ex = Assert.Throws<DomainException>(() => step.Start());
        Assert.Contains("max", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cannot_create_with_zero_max_attempts()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentProcessingStep.Create(
                Guid.NewGuid(), TenantId, DocumentId, VersionId, RunId,
                DocumentProcessingStepName.Preprocess, 0,
                "input-hash", "Processor", "1.0"));

        Assert.Contains("MaxAttempts", ex.Message);
    }

    [Fact]
    public void Cannot_create_with_empty_InputHash()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentProcessingStep.Create(
                Guid.NewGuid(), TenantId, DocumentId, VersionId, RunId,
                DocumentProcessingStepName.Preprocess, 3,
                "", "Processor", "1.0"));

        Assert.Contains("InputHash", ex.Message);
    }

    [Fact]
    public void Cannot_create_with_empty_ProcessorName()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentProcessingStep.Create(
                Guid.NewGuid(), TenantId, DocumentId, VersionId, RunId,
                DocumentProcessingStepName.Preprocess, 3,
                "input-hash", "", "1.0"));

        Assert.Contains("ProcessorName", ex.Message);
    }

    [Fact]
    public void Cannot_create_with_empty_ProcessorVersion()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentProcessingStep.Create(
                Guid.NewGuid(), TenantId, DocumentId, VersionId, RunId,
                DocumentProcessingStepName.Preprocess, 3,
                "input-hash", "Processor", ""));

        Assert.Contains("ProcessorVersion", ex.Message);
    }

    [Fact]
    public void MarkFailed_sets_error_details()
    {
        var step = CreateStep();
        step.Start();
        step.MarkFailed("ERR_PREPROCESS", "Docling Serve unreachable");

        Assert.Equal(DocumentProcessingStepStatus.Failed, step.Status);
        Assert.Equal("ERR_PREPROCESS", step.LastErrorCode);
        Assert.Equal("Docling Serve unreachable", step.LastErrorMessage);
    }

    [Fact]
    public void Cannot_mark_failed_when_not_running()
    {
        var step = CreateStep();

        var ex = Assert.Throws<DomainException>(() =>
            step.MarkFailed("ERR", "fail"));

        Assert.Contains("Pending", ex.Message);
    }

    [Fact]
    public void All_step_names_exist_in_enum()
    {
        var values = Enum.GetValues<DocumentProcessingStepName>();
        Assert.Equal(7, values.Length);
        Assert.Contains(DocumentProcessingStepName.Preprocess, values);
        Assert.Contains(DocumentProcessingStepName.Chunk, values);
        Assert.Contains(DocumentProcessingStepName.GenerateEmbeddings, values);
        Assert.Contains(DocumentProcessingStepName.GenerateIntelligence, values);
        Assert.Contains(DocumentProcessingStepName.Classify, values);
        Assert.Contains(DocumentProcessingStepName.Summarize, values);
        Assert.Contains(DocumentProcessingStepName.ExtractFields, values);
    }

    private static DocumentProcessingStep CreateStep(int maxAttempts = 3)
        => DocumentProcessingStep.Create(
            Guid.NewGuid(), TenantId, DocumentId, VersionId, RunId,
            DocumentProcessingStepName.Preprocess, maxAttempts,
            "input-hash-abc", "DoclingServe", "2.0");
}
