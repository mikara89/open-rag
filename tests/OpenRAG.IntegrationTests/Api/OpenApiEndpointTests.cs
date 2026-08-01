using System.Net;
using System.Text.Json;

namespace OpenRAG.IntegrationTests.Api;

public sealed class OpenApiEndpointTests
    : IClassFixture<AuthenticatedApiWebApplicationFactory>
{
    private readonly AuthenticatedApiWebApplicationFactory _factory;

    public OpenApiEndpointTests(AuthenticatedApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OpenApi_document_describes_bearer_security_for_protected_operations()
    {
        using var client = _factory.CreateHttpsClient();

        using var response = await client.GetAsync("/openapi/v1.json", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        await using var responseStream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        using var document = await JsonDocument.ParseAsync(
            responseStream,
            cancellationToken: CancellationToken.None);

        Assert.True(document.RootElement.TryGetProperty("openapi", out var versionElement));
        Assert.Equal(JsonValueKind.String, versionElement.ValueKind);
        Assert.True(Version.TryParse(versionElement.GetString(), out var openApiVersion));
        Assert.True(openApiVersion.Major >= 3);

        var bearerScheme = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");
        Assert.Equal("http", bearerScheme.GetProperty("type").GetString());
        Assert.Equal("bearer", bearerScheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearerScheme.GetProperty("bearerFormat").GetString());

        var protectedOperationCount = 0;
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (!path.Name.StartsWith("/api", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var operation in path.Value.EnumerateObject())
            {
                protectedOperationCount++;
                var requirements = operation.Value.GetProperty("security");
                Assert.Contains(
                    requirements.EnumerateArray(),
                    requirement => requirement.TryGetProperty("Bearer", out _));
            }
        }

        Assert.True(protectedOperationCount > 0);
        Assert.False(document.RootElement.GetProperty("paths").TryGetProperty(
            "/openapi/{documentName}.json",
            out _));
    }

    [Fact]
    public async Task OpenApi_does_not_expose_tenant_as_request_data()
    {
        using var client = _factory.CreateHttpsClient();
        using var response = await client.GetAsync("/openapi/v1.json", CancellationToken.None);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.DoesNotContain("X-Tenant-Id", json, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(json);
        var askOperation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/rag/ask")
            .GetProperty("post");
        Assert.DoesNotContain("tenantId", askOperation.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenApi_describes_public_payloads_and_never_result_wrappers()
    {
        using var client = _factory.CreateHttpsClient();
        using var response = await client.GetAsync("/openapi/v1.json", CancellationToken.None);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(CancellationToken.None);

        Assert.DoesNotContain("isSuccess", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isFailure", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ResultOf", json, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("paths");
        var expectedSuccesses = new (string Path, string Method, string Status)[]
        {
            ("/api/documents", "get", "200"),
            ("/api/documents/upload", "post", "201"),
            ("/api/documents/{documentId}/status", "get", "200"),
            ("/api/documents/{documentId}/reprocess", "post", "202"),
            ("/api/documents/{documentId}", "get", "200"),
            ("/api/documents/{documentId}", "delete", "204"),
            ("/api/documents/{documentId}/versions/{versionId}/artifacts/markdown", "get", "200"),
            ("/api/documents/{documentId}/versions/{versionId}/artifacts/json", "get", "200"),
            ("/api/documents/{documentId}/versions/{versionId}/chunks", "get", "200"),
            ("/api/documents/{documentId}/versions/{versionId}/chunks/{chunkId}", "get", "200"),
            ("/api/documents/{documentId}/versions/{versionId}/intelligence", "get", "200"),
            ("/api/rag/ask", "post", "200")
        };

        Assert.All(expectedSuccesses, expected =>
        {
            var operation = paths.GetProperty(expected.Path).GetProperty(expected.Method);
            Assert.True(operation.GetProperty("responses").TryGetProperty(expected.Status, out _));
        });
    }
}
