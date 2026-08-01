using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using OpenRAG.Api.Security;

namespace OpenRAG.IntegrationTests.Api;

public sealed class ResultValidationEndpointTests
    : IClassFixture<AuthenticatedApiWebApplicationFactory>
{
    private static readonly Guid UserId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantId = new("22222222-2222-2222-2222-222222222222");
    private readonly AuthenticatedApiWebApplicationFactory _factory;

    public ResultValidationEndpointTests(AuthenticatedApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Invalid_authenticated_request_returns_400_problem_details_without_result_wrapper()
    {
        using var client = _factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _factory.CreateToken(
            [
                new Claim(OpenRagClaimTypes.UserId, UserId.ToString("D")),
                new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString("D"))
            ]));

        using var response = await client.GetAsync(
            "/api/documents?pageNumber=0&pageSize=101",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(
            "https://openrag.dev/problems/request-validation",
            root.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        var errors = root.GetProperty("errors").EnumerateArray().ToArray();
        Assert.Equal(2, errors.Length);
        Assert.Equal("request.page_number_invalid", errors[0].GetProperty("code").GetString());
        Assert.Equal("request.page_size_invalid", errors[1].GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("isSuccess", out _));
        Assert.False(root.TryGetProperty("isFailure", out _));
        Assert.False(root.TryGetProperty("value", out _));
    }
}
