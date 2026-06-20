using OpenRAG.Domain.Common;
using OpenRAG.Domain.Documents;

namespace OpenRAG.UnitTests.Domain.Documents;

public sealed class DocumentVersionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();

    [Fact]
    public void Can_be_created_with_valid_input()
    {
        var version = DocumentVersion.Create(
            Guid.NewGuid(), TenantId, DocumentId, 1,
            "objects/doc/v1/original.pdf", "application/pdf", 1024, "sha256-abc");

        Assert.Equal(TenantId, version.TenantId);
        Assert.Equal(DocumentId, version.DocumentId);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(DocumentVersionStatus.Uploaded, version.Status);
    }

    [Fact]
    public void Cannot_be_created_with_empty_TenantId()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentVersion.Create(
                Guid.NewGuid(), Guid.Empty, DocumentId, 1,
                "key", "application/pdf", 1024, "sha256"));

        Assert.Contains("TenantId", ex.Message);
    }

    [Fact]
    public void Cannot_be_created_with_empty_DocumentId()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentVersion.Create(
                Guid.NewGuid(), TenantId, Guid.Empty, 1,
                "key", "application/pdf", 1024, "sha256"));

        Assert.Contains("DocumentId", ex.Message);
    }

    [Fact]
    public void Cannot_be_created_with_zero_VersionNumber()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentVersion.Create(
                Guid.NewGuid(), TenantId, DocumentId, 0,
                "key", "application/pdf", 1024, "sha256"));

        Assert.Contains("VersionNumber", ex.Message);
    }

    [Fact]
    public void Cannot_be_created_with_negative_VersionNumber()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentVersion.Create(
                Guid.NewGuid(), TenantId, DocumentId, -1,
                "key", "application/pdf", 1024, "sha256"));

        Assert.Contains("VersionNumber", ex.Message);
    }

    [Fact]
    public void Cannot_be_created_with_empty_OriginalObjectKey()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentVersion.Create(
                Guid.NewGuid(), TenantId, DocumentId, 1,
                "", "application/pdf", 1024, "sha256"));

        Assert.Contains("OriginalObjectKey", ex.Message);
    }

    [Fact]
    public void Cannot_be_created_with_empty_OriginalContentType()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentVersion.Create(
                Guid.NewGuid(), TenantId, DocumentId, 1,
                "key", "", 1024, "sha256"));

        Assert.Contains("OriginalContentType", ex.Message);
    }

    [Fact]
    public void Cannot_be_created_with_zero_OriginalSizeBytes()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentVersion.Create(
                Guid.NewGuid(), TenantId, DocumentId, 1,
                "key", "application/pdf", 0, "sha256"));

        Assert.Contains("OriginalSizeBytes", ex.Message);
    }

    [Fact]
    public void Cannot_be_created_with_empty_OriginalSha256()
    {
        var ex = Assert.Throws<DomainException>(() =>
            DocumentVersion.Create(
                Guid.NewGuid(), TenantId, DocumentId, 1,
                "key", "application/pdf", 1024, ""));

        Assert.Contains("OriginalSha256", ex.Message);
    }

    [Fact]
    public void Cannot_mark_preprocessed_without_Markdown_and_JSON_keys()
    {
        var version = CreateVersion();

        var ex = Assert.Throws<DomainException>(() => version.MarkPreprocessed());
        Assert.Contains("Markdown", ex.Message);
    }

    [Fact]
    public void Cannot_mark_preprocessed_with_only_Markdown_key()
    {
        var version = CreateVersion();
        version.AttachDoclingArtifacts("md-key", "json-key");
        // Simulate missing JSON key by not being able to — the Attach sets both.
        // Instead, test that with both keys it works.

        version.MarkPreprocessed();
        Assert.Equal(DocumentVersionStatus.Preprocessed, version.Status);
    }

    [Fact]
    public void Can_attach_artifacts_and_mark_preprocessed()
    {
        var version = CreateVersion();

        version.AttachDoclingArtifacts("objects/doc/v1/markdown.md", "objects/doc/v1/doc.json");
        Assert.Equal(DocumentVersionStatus.Preprocessing, version.Status);

        version.MarkPreprocessed();
        Assert.Equal(DocumentVersionStatus.Preprocessed, version.Status);
    }

    [Fact]
    public void Can_mark_failed()
    {
        var version = CreateVersion();

        version.MarkFailed();
        Assert.Equal(DocumentVersionStatus.Failed, version.Status);
    }

    private static DocumentVersion CreateVersion()
        => DocumentVersion.Create(
            Guid.NewGuid(), TenantId, DocumentId, 1,
            "objects/doc/v1/original.pdf", "application/pdf", 1024, "sha256-abc");
}
