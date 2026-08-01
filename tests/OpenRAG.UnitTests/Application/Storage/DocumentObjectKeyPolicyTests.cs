using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Common;
using OpenRAG.Application.Storage;

namespace OpenRAG.UnitTests.Application.Storage;

public sealed class DocumentObjectKeyPolicyTests
{
    private static readonly Guid TenantId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DocumentId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid VersionId = new("22222222-2222-2222-2222-222222222222");
    private readonly DocumentObjectKeyPolicy _policy = new();

    [Fact]
    public void Builds_and_accepts_canonical_keys()
    {
        var source = _policy.BuildSourceKey(TenantId, DocumentId, VersionId, "report.pdf");
        var markdown = _policy.BuildArtifactKey(TenantId, DocumentId, VersionId, DocumentObjectKind.Markdown);
        var json = _policy.BuildArtifactKey(TenantId, DocumentId, VersionId, DocumentObjectKind.Json);

        Assert.Equal(
            $"tenants/{TenantId:D}/documents/{DocumentId:D}/versions/{VersionId:D}/original/report.pdf",
            source);
        Assert.Equal(
            $"tenants/{TenantId:D}/documents/{DocumentId:D}/versions/{VersionId:D}/docling/document.md",
            markdown);
        Assert.Equal(
            $"tenants/{TenantId:D}/documents/{DocumentId:D}/versions/{VersionId:D}/docling/document.json",
            json);
        _policy.EnsureOwned(source, TenantId, DocumentId, VersionId, DocumentObjectKind.Source);
        _policy.EnsureOwned(markdown, TenantId, DocumentId, VersionId, DocumentObjectKind.Markdown);
        _policy.EnsureOwned(json, TenantId, DocumentId, VersionId, DocumentObjectKind.Json);
    }

    [Theory]
    [MemberData(nameof(InvalidOwnedKeys))]
    public void Rejects_keys_outside_the_complete_ownership_boundary(string key)
    {
        Assert.Throws<IsolationViolationException>(() =>
            _policy.EnsureOwned(key, TenantId, DocumentId, VersionId, DocumentObjectKind.Markdown));
    }

    [Theory]
    [InlineData("../report.pdf")]
    [InlineData("folder/report.pdf")]
    [InlineData("folder\\report.pdf")]
    [InlineData(".")]
    [InlineData("..")]
    public void Rejects_non_leaf_source_file_names(string fileName)
    {
        Assert.Throws<RequestValidationException>(() =>
            _policy.BuildSourceKey(TenantId, DocumentId, VersionId, fileName));
    }

    [Fact]
    public void Rejects_empty_identifiers()
    {
        Assert.Throws<RequestValidationException>(() =>
            _policy.BuildArtifactKey(Guid.Empty, DocumentId, VersionId, DocumentObjectKind.Markdown));
        Assert.Throws<RequestValidationException>(() =>
            _policy.BuildArtifactKey(TenantId, Guid.Empty, VersionId, DocumentObjectKind.Markdown));
        Assert.Throws<RequestValidationException>(() =>
            _policy.BuildArtifactKey(TenantId, DocumentId, Guid.Empty, DocumentObjectKind.Markdown));
    }

    public static TheoryData<string> InvalidOwnedKeys()
    {
        var otherTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var otherDocument = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var otherVersion = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var prefix = $"tenants/{TenantId:D}/documents/{DocumentId:D}/versions/{VersionId:D}/";

        return new TheoryData<string>
        {
            $"tenants/{otherTenant:D}/documents/{DocumentId:D}/versions/{VersionId:D}/docling/document.md",
            $"tenants/{TenantId:D}/documents/{otherDocument:D}/versions/{VersionId:D}/docling/document.md",
            $"tenants/{TenantId:D}/documents/{DocumentId:D}/versions/{otherVersion:D}/docling/document.md",
            $"{prefix}docling/../document.md",
            $"/{prefix}docling/document.md",
            $"C:/{prefix}docling/document.md",
            prefix.Replace('/', '\\') + "docling\\document.md",
            $"{prefix}docling/document.json",
            $"{prefix}docling/document.md/extra"
        };
    }
}
