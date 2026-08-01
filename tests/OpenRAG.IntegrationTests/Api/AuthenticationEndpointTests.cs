using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using OpenRAG.Api.Security;

namespace OpenRAG.IntegrationTests.Api;

public sealed class AuthenticationEndpointTests
    : IClassFixture<AuthenticatedApiWebApplicationFactory>
{
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly AuthenticatedApiWebApplicationFactory _factory;

    public AuthenticationEndpointTests(AuthenticatedApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Protected_endpoint_without_authorization_header_returns_401()
    {
        using var client = _factory.CreateHttpsClient();

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        AssertUnauthorizedBearerChallenge(response);
    }

    [Fact]
    public async Task Malformed_bearer_token_returns_401()
    {
        using var client = CreateClientWithToken("not-a-jwt");

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        AssertUnauthorizedBearerChallenge(response);
    }

    [Fact]
    public async Task Token_with_invalid_signature_returns_401()
    {
        var otherKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        using var client = CreateClientWithToken(_factory.CreateToken(AdminClaims(), signingKey: otherKey));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        AssertUnauthorizedBearerChallenge(response);
    }

    [Fact]
    public async Task Token_with_wrong_issuer_returns_401()
    {
        using var client = CreateClientWithToken(
            _factory.CreateToken(AdminClaims(), issuer: "https://wrong-issuer.example.invalid"));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        AssertUnauthorizedBearerChallenge(response);
    }

    [Fact]
    public async Task Token_with_wrong_audience_returns_401()
    {
        using var client = CreateClientWithToken(
            _factory.CreateToken(AdminClaims(), audience: "wrong-audience"));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        AssertUnauthorizedBearerChallenge(response);
    }

    [Fact]
    public async Task Expired_token_returns_401()
    {
        using var client = CreateClientWithToken(
            _factory.CreateToken(AdminClaims(), expires: DateTime.UtcNow.AddMinutes(-2)));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        AssertUnauthorizedBearerChallenge(response);
    }

    [Fact]
    public async Task Token_without_expiration_returns_401()
    {
        using var client = CreateClientWithToken(
            _factory.CreateToken(AdminClaims(), includeExpiration: false));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        AssertUnauthorizedBearerChallenge(response);
    }

    [Fact]
    public async Task Unsigned_token_returns_401()
    {
        using var client = CreateClientWithToken(
            _factory.CreateToken(AdminClaims(), signToken: false));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        AssertUnauthorizedBearerChallenge(response);
    }

    [Fact]
    public async Task Valid_admin_token_with_guid_subject_returns_200()
    {
        using var client = CreateClientWithToken(_factory.CreateToken(AdminClaims()));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Valid_non_admin_token_returns_403_for_provider_diagnostics()
    {
        using var client = CreateClientWithToken(_factory.CreateToken(UserClaims()));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Valid_token_without_user_id_claim_returns_403()
    {
        using var client = CreateClientWithToken(_factory.CreateToken(
        [
            new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString()),
            new Claim(OpenRagClaimTypes.Role, OpenRagRoles.Administrator)
        ]));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Valid_token_with_malformed_user_id_claim_returns_403()
    {
        using var client = CreateClientWithToken(_factory.CreateToken(
        [
            new Claim(OpenRagClaimTypes.UserId, "not-a-guid"),
            new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString()),
            new Claim(OpenRagClaimTypes.Role, OpenRagRoles.Administrator)
        ]));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Valid_token_with_empty_guid_user_id_claim_returns_403()
    {
        using var client = CreateClientWithToken(_factory.CreateToken(
        [
            new Claim(OpenRagClaimTypes.UserId, Guid.Empty.ToString()),
            new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString()),
            new Claim(OpenRagClaimTypes.Role, OpenRagRoles.Administrator)
        ]));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Valid_token_with_conflicting_user_id_claims_returns_403()
    {
        const string configuredUserIdClaim = "test_user_id";
        using var customFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{JwtAuthenticationOptions.SectionName}:UserIdClaimType"] = configuredUserIdClaim
                })));
        using var client = customFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _factory.CreateToken(
        [
            new Claim(configuredUserIdClaim, UserId.ToString()),
            new Claim(configuredUserIdClaim, Guid.NewGuid().ToString()),
            new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString()),
            new Claim(OpenRagClaimTypes.Role, OpenRagRoles.Administrator)
        ]));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Valid_token_without_tenant_id_claim_returns_403()
    {
        using var client = CreateClientWithToken(_factory.CreateToken(
        [
            new Claim(OpenRagClaimTypes.UserId, UserId.ToString()),
            new Claim(OpenRagClaimTypes.Role, OpenRagRoles.Administrator)
        ]));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Valid_token_with_invalid_tenant_id_claim_returns_403(string tenantId)
    {
        using var client = CreateClientWithToken(_factory.CreateToken(
        [
            new Claim(OpenRagClaimTypes.UserId, UserId.ToString()),
            new Claim(OpenRagClaimTypes.TenantId, tenantId),
            new Claim(OpenRagClaimTypes.Role, OpenRagRoles.Administrator)
        ]));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Valid_token_with_duplicate_tenant_id_claims_returns_403()
    {
        using var client = CreateClientWithToken(_factory.CreateToken(
        [
            new Claim(OpenRagClaimTypes.UserId, UserId.ToString()),
            new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString()),
            new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString()),
            new Claim(OpenRagClaimTypes.Role, OpenRagRoles.Administrator)
        ]));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Valid_token_with_conflicting_tenant_id_claims_returns_403()
    {
        using var client = CreateClientWithToken(_factory.CreateToken(
        [
            new Claim(OpenRagClaimTypes.UserId, UserId.ToString()),
            new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString()),
            new Claim(OpenRagClaimTypes.TenantId, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            new Claim(OpenRagClaimTypes.Role, OpenRagRoles.Administrator)
        ]));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Configured_custom_tenant_claim_is_accepted()
    {
        const string customTenantClaim = "organization_id";
        using var customFactory = CreateFactoryWithTenantClaim(customTenantClaim);
        using var client = CreateHttpsClient(customFactory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _factory.CreateToken(
            [
                new Claim(OpenRagClaimTypes.UserId, UserId.ToString()),
                new Claim(customTenantClaim, TenantId.ToString()),
                new Claim(OpenRagClaimTypes.Role, OpenRagRoles.Administrator)
            ]));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Configured_custom_tenant_claim_ignores_default_tenant_id()
    {
        const string customTenantClaim = "organization_id";
        using var customFactory = CreateFactoryWithTenantClaim(customTenantClaim);
        using var client = CreateHttpsClient(customFactory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _factory.CreateToken(
            [
                new Claim(OpenRagClaimTypes.UserId, UserId.ToString()),
                new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString()),
                new Claim(OpenRagClaimTypes.Role, OpenRagRoles.Administrator)
            ]));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Default_tenant_claim_configuration_ignores_other_claim_names()
    {
        using var client = CreateClientWithToken(_factory.CreateToken(
        [
            new Claim(OpenRagClaimTypes.UserId, UserId.ToString()),
            new Claim("organization_id", TenantId.ToString()),
            new Claim(OpenRagClaimTypes.Role, OpenRagRoles.Administrator)
        ]));

        using var response = await client.GetAsync("/api/system/providers", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateClientWithToken(string token)
    {
        var client = _factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private WebApplicationFactory<OpenRAG.Api.AssemblyReference> CreateFactoryWithTenantClaim(string claimType)
        => _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{JwtAuthenticationOptions.SectionName}:TenantIdClaimType"] = claimType
                })));

    private static HttpClient CreateHttpsClient(WebApplicationFactory<OpenRAG.Api.AssemblyReference> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

    private static Claim[] UserClaims() =>
    [
        new Claim(OpenRagClaimTypes.UserId, UserId.ToString()),
        new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString())
    ];

    private static Claim[] AdminClaims() =>
    [
        new Claim(OpenRagClaimTypes.UserId, UserId.ToString()),
        new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString()),
        new Claim(OpenRagClaimTypes.Role, OpenRagRoles.Administrator)
    ];

    private static void AssertUnauthorizedBearerChallenge(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(
            response.Headers.WwwAuthenticate,
            value => string.Equals(value.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase));
    }
}
