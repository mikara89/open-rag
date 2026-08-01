using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using OpenRAG.Api.Security;

namespace OpenRAG.IntegrationTests.Api;

public sealed class HttpContextCurrentTenantTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Returns_single_authenticated_non_empty_guid_claim()
    {
        var currentTenant = CreateCurrentTenant(
        [
            new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString())
        ]);

        Assert.Equal(TenantId, currentTenant.TenantId);
    }

    [Fact]
    public void Rejects_unauthenticated_principal()
    {
        var currentTenant = CreateCurrentTenant(
        [
            new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString())
        ],
        authenticationType: null);

        Assert.Throws<InvalidOperationException>(() => currentTenant.TenantId);
    }

    [Fact]
    public void Rejects_missing_tenant_claim()
    {
        var currentTenant = CreateCurrentTenant([]);

        Assert.Throws<InvalidOperationException>(() => currentTenant.TenantId);
    }

    [Fact]
    public void Does_not_use_user_claim_as_tenant_id()
    {
        var currentTenant = CreateCurrentTenant(
        [
            new Claim(OpenRagClaimTypes.UserId, TenantId.ToString())
        ]);

        Assert.Throws<InvalidOperationException>(() => currentTenant.TenantId);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Rejects_invalid_tenant_claim(string value)
    {
        var currentTenant = CreateCurrentTenant(
        [
            new Claim(OpenRagClaimTypes.TenantId, value)
        ]);

        Assert.Throws<InvalidOperationException>(() => currentTenant.TenantId);
    }

    [Fact]
    public void Rejects_duplicate_tenant_claims_even_when_values_match()
    {
        var currentTenant = CreateCurrentTenant(
        [
            new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString()),
            new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString())
        ]);

        Assert.Throws<InvalidOperationException>(() => currentTenant.TenantId);
    }

    [Fact]
    public void Uses_only_configured_tenant_claim_type()
    {
        const string customClaimType = "organization_id";
        var currentTenant = CreateCurrentTenant(
        [
            new Claim(OpenRagClaimTypes.TenantId, Guid.NewGuid().ToString()),
            new Claim(customClaimType, TenantId.ToString())
        ],
        claimType: customClaimType);

        Assert.Equal(TenantId, currentTenant.TenantId);
    }

    private static HttpContextCurrentTenant CreateCurrentTenant(
        IEnumerable<Claim> claims,
        string? authenticationType = "Bearer",
        string claimType = OpenRagClaimTypes.TenantId)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType))
        };
        return new HttpContextCurrentTenant(
            new HttpContextAccessor { HttpContext = context },
            Options.Create(new JwtAuthenticationOptions { TenantIdClaimType = claimType }));
    }
}
