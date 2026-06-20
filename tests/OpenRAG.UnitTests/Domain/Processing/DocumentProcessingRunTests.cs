using OpenRAG.Domain.Common;
using OpenRAG.Domain.Processing;

namespace OpenRAG.UnitTests.Domain.Processing;

public sealed class DocumentProcessingRunTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid VersionId = Guid.NewGuid();

    [Fact]
    public void Can_be_created_with_valid_input()
    {
        var run = DocumentProcessingRun.Create(
            Guid.NewGuid(), TenantId, DocumentId, VersionId,
            ProcessingRunReason.InitialUpload, "corr-123");

        Assert.Equal(TenantId, run.TenantId);
        Assert.Equal(DocumentId, run.DocumentId);
        Assert.Equal(VersionId, run.VersionId);
        Assert.Equal(ProcessingRunReason.InitialUpload, run.RunReason);
        Assert.Equal(DocumentProcessingRunStatus.Pending, run.Status);
    }

    [Fact]
    public void Cannot_be_created_with_empty_TenantId()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentProcessingRun.Create(
                Guid.NewGuid(), Guid.Empty, DocumentId, VersionId,
                ProcessingRunReason.InitialUpload, "corr-123"));

        Assert.Contains("TenantId", ex.Message);
    }

    [Fact]
    public void Cannot_be_created_with_empty_DocumentId()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentProcessingRun.Create(
                Guid.NewGuid(), TenantId, Guid.Empty, VersionId,
                ProcessingRunReason.InitialUpload, "corr-123"));

        Assert.Contains("DocumentId", ex.Message);
    }

    [Fact]
    public void Cannot_be_created_with_empty_VersionId()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentProcessingRun.Create(
                Guid.NewGuid(), TenantId, DocumentId, Guid.Empty,
                ProcessingRunReason.InitialUpload, "corr-123"));

        Assert.Contains("VersionId", ex.Message);
    }

    [Fact]
    public void Can_start_and_complete()
    {
        var run = CreateRun();
        Assert.Equal(DocumentProcessingRunStatus.Pending, run.Status);

        run.Start();
        Assert.Equal(DocumentProcessingRunStatus.Running, run.Status);

        run.MarkCompleted();
        Assert.Equal(DocumentProcessingRunStatus.Completed, run.Status);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public void Can_mark_failed_from_running()
    {
        var run = CreateRun();
        run.Start();
        run.MarkFailed();

        Assert.Equal(DocumentProcessingRunStatus.Failed, run.Status);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public void Cannot_mark_failed_when_already_completed()
    {
        var run = CreateRun();
        run.Start();
        run.MarkCompleted();

        var ex = Assert.Throws<DomainException>(() => run.MarkFailed());
        Assert.Contains("completed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cannot_start_when_already_running()
    {
        var run = CreateRun();
        run.Start();

        var ex = Assert.Throws<DomainException>(() => run.Start());
        Assert.Contains("Running", ex.Message);
    }

    [Fact]
    public void All_run_reasons_exist_in_enum()
    {
        var values = Enum.GetValues<ProcessingRunReason>();
        Assert.Equal(5, values.Length);
        Assert.Contains(ProcessingRunReason.InitialUpload, values);
        Assert.Contains(ProcessingRunReason.ManualRetry, values);
        Assert.Contains(ProcessingRunReason.ReprocessWithNewPreprocessor, values);
        Assert.Contains(ProcessingRunReason.ReprocessWithNewEmbeddingModel, values);
        Assert.Contains(ProcessingRunReason.ReprocessWithNewExtractionSchema, values);
    }

    private static DocumentProcessingRun CreateRun()
        => DocumentProcessingRun.Create(
            Guid.NewGuid(), TenantId, DocumentId, VersionId,
            ProcessingRunReason.InitialUpload, "corr-123");
}
