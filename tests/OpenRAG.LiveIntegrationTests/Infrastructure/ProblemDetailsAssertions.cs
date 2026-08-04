using System.Net;
using System.Text.Json;

namespace OpenRAG.LiveIntegrationTests.Infrastructure;

internal static class ProblemDetailsAssertions
{
    public static async Task AssertEquivalentMissingAndForeignAsync(
        HttpResponseMessage missingResponse,
        HttpResponseMessage foreignResponse)
    {
        var missing = await ReadSnapshotAsync(missingResponse);
        var foreign = await ReadSnapshotAsync(foreignResponse);

        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Equal("application/problem+json", missingResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/problem+json", foreignResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(missing.Type, foreign.Type);
        Assert.Equal(missing.Title, foreign.Title);
        Assert.Equal(missing.Detail, foreign.Detail);
        Assert.Equal(missing.Status, foreign.Status);
        Assert.Equal(missing.Properties, foreign.Properties);
        Assert.Equal(missing.Errors, foreign.Errors);
        Assert.Equal("https://openrag.dev/problems/resource-not-found", missing.Type);
        Assert.Equal("Resource not found.", missing.Title);
        Assert.Equal("The requested resource was not found.", missing.Detail);
        var error = Assert.Single(missing.Errors);
        Assert.Equal("resource.not_found", error.Code);
        Assert.Null(error.Target);
    }

    public static async Task AssertValidationAsync(
        HttpResponseMessage response,
        string code,
        string? target)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var snapshot = await ReadSnapshotAsync(response);
        Assert.Equal("https://openrag.dev/problems/request-validation", snapshot.Type);
        Assert.Contains(snapshot.Errors, error => error.Code == code && error.Target == target);
    }

    public static async Task AssertConflictAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var snapshot = await ReadSnapshotAsync(response);
        Assert.Equal("https://openrag.dev/problems/resource-conflict", snapshot.Type);
        Assert.Contains(snapshot.Errors, error => error.Code == code);
    }

    public static async Task AssertGenericInternalServerErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(LiveTestConstants.TenantAMarker, body, StringComparison.Ordinal);
        Assert.DoesNotContain("tenants/", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IsolationViolationException", body, StringComparison.Ordinal);
    }

    private static async Task<ProblemSnapshot> ReadSnapshotAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("isSuccess", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isFailure", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("applicationErrorType", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(LiveTestConstants.TenantAMarker, body, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveTestConstants.TenantBMarker, body, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.False(root.TryGetProperty("value", out _));
        Assert.False(root.TryGetProperty("errorType", out _));
        Assert.False(root.TryGetProperty("internalErrorType", out _));
        Assert.True(root.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
        var errors = root.TryGetProperty("errors", out var errorElement)
            ? errorElement.EnumerateArray()
                .Select(error => new ErrorSnapshot(
                    error.GetProperty("code").GetString()!,
                    error.TryGetProperty("target", out var target) ? target.GetString() : null))
                .ToArray()
            : [];
        var properties = root.EnumerateObject()
            .Where(property => property.Name != "traceId")
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        return new ProblemSnapshot(
            root.GetProperty("type").GetString(),
            root.GetProperty("title").GetString(),
            root.GetProperty("detail").GetString(),
            root.GetProperty("status").GetInt32(),
            properties,
            errors);
    }

    private sealed record ProblemSnapshot(
        string? Type,
        string? Title,
        string? Detail,
        int Status,
        IReadOnlyList<string> Properties,
        IReadOnlyList<ErrorSnapshot> Errors);

    private sealed record ErrorSnapshot(string Code, string? Target);
}
