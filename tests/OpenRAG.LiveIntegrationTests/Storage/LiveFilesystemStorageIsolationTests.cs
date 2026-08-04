using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Common;
using OpenRAG.Infrastructure.Storage;
using OpenRAG.LiveIntegrationTests.Infrastructure;

namespace OpenRAG.LiveIntegrationTests.Storage;

[Collection(OpenRagLiveInfrastructureTestGroup.Name)]
[Trait("Category", "LiveIntegration")]
[Trait("Security", "CrossTenant")]
public sealed class LiveFilesystemStorageIsolationTests
{
    private readonly OpenRagLiveInfrastructureFixture _fixture;

    public LiveFilesystemStorageIsolationTests(OpenRagLiveInfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Real_local_storage_reads_each_tenants_canonical_objects()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(400);
        var tenantA = await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        var tenantB = await _fixture.SeedDocumentAsync(LiveTestData.TenantB1(ids));

        using var scope = _fixture.ApiFactory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        Assert.IsType<LocalFileStorage>(storage);

        Assert.Contains(
            LiveTestConstants.TenantAMarker,
            await ReadAsync(storage, tenantA.Version.OriginalObjectKey),
            StringComparison.Ordinal);
        Assert.Contains(
            LiveTestConstants.TenantAMarker,
            await ReadAsync(storage, tenantA.Version.DoclingMarkdownObjectKey!),
            StringComparison.Ordinal);
        Assert.Contains(
            LiveTestConstants.TenantAMarker,
            await ReadAsync(storage, tenantA.Version.DoclingJsonObjectKey!),
            StringComparison.Ordinal);
        Assert.Contains(
            LiveTestConstants.TenantBMarker,
            await ReadAsync(storage, tenantB.Version.OriginalObjectKey),
            StringComparison.Ordinal);
        Assert.Contains(
            LiveTestConstants.TenantBMarker,
            await ReadAsync(storage, tenantB.Version.DoclingMarkdownObjectKey!),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Storage_and_object_key_policy_reject_escape_and_ownership_attacks()
    {
        await _fixture.ResetAsync();
        using var scope = _fixture.ApiFactory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var policy = scope.ServiceProvider.GetRequiredService<IDocumentObjectKeyPolicy>();
        var tenant = LiveTestConstants.TenantB;
        var document = Guid.NewGuid();
        var version = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync("../../outside.txt"));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(Path.GetFullPath("outside.txt")));

        var invalidKeys = new[]
        {
            $"tenants/{LiveTestConstants.TenantA:D}/documents/{document:D}/versions/{version:D}/docling/document.md",
            $"tenants/{tenant:D}/documents/{Guid.NewGuid():D}/versions/{version:D}/docling/document.md",
            $"tenants/{tenant:D}/documents/{document:D}/versions/{Guid.NewGuid():D}/docling/document.md",
            $"tenants/{tenant:D}/documents/{document:D}/versions/{version:D}/../document.md",
            Path.GetFullPath("absolute-document.md"),
            $"tenants\\{tenant:D}\\documents\\{document:D}\\versions\\{version:D}\\docling\\document.md",
            $"tenants/{tenant:D}/documents/{document:D}/versions/{version:D}/docling/unexpected.md"
        };
        foreach (var invalidKey in invalidKeys)
        {
            Assert.Throws<IsolationViolationException>(() => policy.EnsureOwned(
                invalidKey,
                tenant,
                document,
                version,
                DocumentObjectKind.Markdown));
        }
    }

    [Theory]
    [InlineData("foreign-tenant")]
    [InlineData("wrong-document")]
    [InlineData("wrong-version")]
    [InlineData("traversal")]
    [InlineData("absolute")]
    [InlineData("backslash")]
    [InlineData("suffix")]
    public async Task Corrupted_persisted_artifact_keys_fail_closed_without_touching_files(string attack)
    {
        await _fixture.ResetAsync();
        var scenario = 410 + Array.IndexOf(
            new[] { "foreign-tenant", "wrong-document", "wrong-version", "traversal", "absolute", "backslash", "suffix" },
            attack);
        var ids = LiveTestIds.ForScenario(scenario);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        await _fixture.SeedDocumentAsync(LiveTestData.TenantB1(ids));
        var before = await _fixture.GetStorageManifestAsync();
        var key = attack switch
        {
            "foreign-tenant" => Canonical(
                LiveTestConstants.TenantA, ids.DocumentA1, ids.VersionA1),
            "wrong-document" => Canonical(
                LiveTestConstants.TenantB, ids.DocumentA1, ids.VersionB1),
            "wrong-version" => Canonical(
                LiveTestConstants.TenantB, ids.DocumentB1, ids.VersionA1),
            "traversal" => "../outside/document.md",
            "absolute" => Path.GetFullPath("outside-document.md"),
            "backslash" => Canonical(
                LiveTestConstants.TenantB, ids.DocumentB1, ids.VersionB1).Replace('/', '\\'),
            "suffix" => $"tenants/{LiveTestConstants.TenantB:D}/documents/{ids.DocumentB1:D}/versions/{ids.VersionB1:D}/docling/other.md",
            _ => throw new ArgumentOutOfRangeException(nameof(attack))
        };

        await using (var context = _fixture.CreateDbContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE document_versions
                SET "DoclingMarkdownObjectKey" = {key}
                WHERE "TenantId" = {LiveTestConstants.TenantB}
                  AND "DocumentId" = {ids.DocumentB1}
                  AND "Id" = {ids.VersionB1}
                """);
        }

        using var client = _fixture.CreateTenantBClient();
        using var response = await client.GetAsync(
            $"/api/documents/{ids.DocumentB1}/versions/{ids.VersionB1}/artifacts/markdown");
        await ProblemDetailsAssertions.AssertGenericInternalServerErrorAsync(response);
        Assert.Equal(before, await _fixture.GetStorageManifestAsync());
    }

    private static string Canonical(Guid tenantId, Guid documentId, Guid versionId) =>
        $"tenants/{tenantId:D}/documents/{documentId:D}/versions/{versionId:D}/docling/document.md";

    private static async Task<string> ReadAsync(IFileStorage storage, string key)
    {
        await using var stream = await storage.OpenReadAsync(key);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
