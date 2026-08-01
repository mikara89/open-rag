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
}
