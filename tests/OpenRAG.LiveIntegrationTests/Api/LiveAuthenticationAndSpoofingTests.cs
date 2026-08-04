using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using OpenRAG.Api.Security;
using OpenRAG.LiveIntegrationTests.Infrastructure;

namespace OpenRAG.LiveIntegrationTests.Api;

[Collection(OpenRagLiveInfrastructureTestGroup.Name)]
[Trait("Category", "LiveIntegration")]
[Trait("Security", "CrossTenant")]
public sealed class LiveAuthenticationAndSpoofingTests
{
    private readonly OpenRagLiveInfrastructureFixture _fixture;

    public LiveAuthenticationAndSpoofingTests(OpenRagLiveInfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Real_authentication_rejects_missing_and_invalid_tokens()
    {
        using var anonymous = _fixture.ApiFactory.CreateClient();
        using var missing = await anonymous.GetAsync("/api/documents");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        using var invalidClient = _fixture.ApiFactory.CreateClient();
        invalidClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        using var invalid = await invalidClient.GetAsync("/api/documents");
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InvalidIdentityClaims))]
    public async Task Real_authorization_rejects_missing_malformed_and_duplicate_identity_claims(
        Claim[] claims)
    {
        using var client = _fixture.ApiFactory.CreateClientWithClaims(claims);
        using var response = await client.GetAsync("/api/documents");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Header_query_body_and_route_spoofing_cannot_override_Tenant_B_JWT()
    {
        await _fixture.ResetAsync();
        var ids = LiveTestIds.ForScenario(700);
        await _fixture.SeedDocumentAsync(LiveTestData.TenantA1(ids));
        await _fixture.SeedDocumentAsync(LiveTestData.TenantB1(ids));
        using var tenantB = _fixture.CreateTenantBClient();
        tenantB.DefaultRequestHeaders.Add("X-Tenant-Id", LiveTestConstants.TenantA.ToString("D"));

        using var response = await tenantB.PostAsJsonAsync(
            $"/api/rag/ask?tenantId={LiveTestConstants.TenantA:D}",
            new Dictionary<string, object?>
            {
                ["question"] = "Which tenant is authoritative?",
                ["tenantId"] = LiveTestConstants.TenantA
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(LiveTestConstants.TenantBMarker, body, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveTestConstants.TenantAMarker, body, StringComparison.Ordinal);
        Assert.All(_fixture.ProviderProbe.EmbeddingRequests, request =>
            Assert.Equal(LiveTestConstants.TenantB, request.TenantId));
        Assert.All(_fixture.ProviderProbe.ChatRequests, request =>
            Assert.Equal(LiveTestConstants.TenantB, request.TenantId));

        using var nonexistentTenantRoute = await tenantB.GetAsync(
            $"/api/tenants/{LiveTestConstants.TenantA:D}/documents");
        Assert.Equal(HttpStatusCode.NotFound, nonexistentTenantRoute.StatusCode);
    }

    public static TheoryData<Claim[]> InvalidIdentityClaims =>
        new()
        {
            new Claim[]
            {
                new Claim(OpenRagClaimTypes.UserId, LiveTestConstants.UserB.ToString("D"))
            },
            new Claim[]
            {
                new Claim(OpenRagClaimTypes.UserId, LiveTestConstants.UserB.ToString("D")),
                new Claim(OpenRagClaimTypes.TenantId, "not-a-guid")
            },
            new Claim[]
            {
                new Claim(OpenRagClaimTypes.UserId, LiveTestConstants.UserB.ToString("D")),
                new Claim(OpenRagClaimTypes.TenantId, LiveTestConstants.TenantB.ToString("D")),
                new Claim(OpenRagClaimTypes.TenantId, LiveTestConstants.TenantA.ToString("D"))
            },
            new Claim[]
            {
                new Claim(OpenRagClaimTypes.TenantId, LiveTestConstants.TenantB.ToString("D"))
            },
            new Claim[]
            {
                new Claim(OpenRagClaimTypes.UserId, "not-a-guid"),
                new Claim(OpenRagClaimTypes.TenantId, LiveTestConstants.TenantB.ToString("D"))
            }
        };
}
