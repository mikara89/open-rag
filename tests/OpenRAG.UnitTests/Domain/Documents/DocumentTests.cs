using OpenRAG.Domain.Common;
using OpenRAG.Domain.Documents;

namespace OpenRAG.UnitTests.Domain.Documents;

public sealed class DocumentTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Can_be_created_with_valid_input()
    {
        var doc = Document.Create(
            Guid.NewGuid(), TenantId, "Report.pdf", "report.pdf", UserId);

        Assert.Equal(TenantId, doc.TenantId);
        Assert.Equal("Report.pdf", doc.Title);
        Assert.Equal("report.pdf", doc.OriginalFileName);
        Assert.Equal(UserId, doc.CreatedByUserId);
        Assert.Equal(DocumentStatus.Uploaded, doc.Status);
        Assert.NotEqual(Guid.Empty, doc.Id);
    }

    [Fact]
    public void Cannot_be_created_with_empty_TenantId()
    {
        var ex = Assert.Throws<DomainException>(() =>
            Document.Create(Guid.NewGuid(), Guid.Empty, "Title", "file.pdf", UserId));

        Assert.Contains("TenantId", ex.Message);
    }

    [Fact]
    public void Cannot_be_created_with_empty_Title()
    {
        var ex = Assert.Throws<DomainException>(() =>
            Document.Create(Guid.NewGuid(), TenantId, "", "file.pdf", UserId));

        Assert.Contains("Title", ex.Message);
    }

    [Fact]
    public void Cannot_be_created_with_empty_OriginalFileName()
    {
        var ex = Assert.Throws<DomainException>(() =>
            Document.Create(Guid.NewGuid(), TenantId, "Title", "", UserId));

        Assert.Contains("OriginalFileName", ex.Message);
    }

    [Fact]
    public void Cannot_be_created_with_empty_CreatedByUserId()
    {
        var ex = Assert.Throws<DomainException>(() =>
            Document.Create(Guid.NewGuid(), TenantId, "Title", "file.pdf", Guid.Empty));

        Assert.Contains("CreatedByUserId", ex.Message);
    }

    [Fact]
    public void Cannot_be_created_with_empty_Id()
    {
        Assert.Throws<DomainException>(() =>
            Document.Create(Guid.Empty, TenantId, "Title", "file.pdf", UserId));
    }

    [Fact]
    public void Can_add_a_new_version()
    {
        var doc = CreateDocument();

        var version = doc.AddVersion(
            Guid.NewGuid(), 1, "objects/doc-1/v1/original.pdf",
            "application/pdf", 1024, "abc123");

        Assert.NotNull(version);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(doc.Id, version.DocumentId);
        Assert.Single(doc.Versions);
        Assert.Equal(version.Id, doc.CurrentVersionId);
    }

    [Fact]
    public void Deleted_document_cannot_add_a_new_version()
    {
        var doc = CreateDocument();
        doc.SoftDelete();

        var ex = Assert.Throws<DomainException>(() =>
            doc.AddVersion(
                Guid.NewGuid(), 1, "objects/doc-1/v1/original.pdf",
                "application/pdf", 1024, "abc123"));

        Assert.Contains("deleted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deleted_document_cannot_be_marked_ready()
    {
        var doc = CreateDocument();
        doc.SoftDelete();

        var ex = Assert.Throws<DomainException>(() => doc.MarkReady());

        Assert.Contains("deleted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Can_mark_processing_and_ready()
    {
        var doc = CreateDocument();
        doc.AddVersion(
            Guid.NewGuid(), 1, "objects/doc-1/v1/original.pdf",
            "application/pdf", 1024, "abc123");

        doc.MarkProcessing();
        Assert.Equal(DocumentStatus.Processing, doc.Status);

        doc.MarkReady();
        Assert.Equal(DocumentStatus.Ready, doc.Status);
    }

    [Fact]
    public void Cannot_mark_ready_without_processing()
    {
        var doc = CreateDocument();

        var ex = Assert.Throws<DomainException>(() => doc.MarkReady());
        Assert.Contains("Uploaded", ex.Message);
    }

    [Fact]
    public void Can_mark_failed_from_uploaded()
    {
        var doc = CreateDocument();

        doc.MarkFailed();
        Assert.Equal(DocumentStatus.Failed, doc.Status);
    }

    [Fact]
    public void Soft_delete_sets_deleted_status_and_timestamp()
    {
        var doc = CreateDocument();

        doc.SoftDelete();
        Assert.Equal(DocumentStatus.Deleted, doc.Status);
        Assert.NotNull(doc.DeletedAt);
    }

    [Fact]
    public void Cannot_soft_delete_already_deleted_document()
    {
        var doc = CreateDocument();
        doc.SoftDelete();

        Assert.Throws<DomainException>(() => doc.SoftDelete());
    }

    [Fact]
    public void CurrentVersionId_points_to_latest_version()
    {
        var doc = CreateDocument();

        var v1 = doc.AddVersion(
            Guid.NewGuid(), 1, "objects/doc-1/v1/original.pdf",
            "application/pdf", 1024, "abc123");
        Assert.Equal(v1.Id, doc.CurrentVersionId);

        var v2 = doc.AddVersion(
            Guid.NewGuid(), 2, "objects/doc-1/v2/original.pdf",
            "application/pdf", 2048, "def456");
        Assert.Equal(v2.Id, doc.CurrentVersionId);
        Assert.Equal(2, doc.Versions.Count);
    }

    private static Document CreateDocument()
        => Document.Create(Guid.NewGuid(), TenantId, "Test Doc", "test.pdf", UserId);
}
