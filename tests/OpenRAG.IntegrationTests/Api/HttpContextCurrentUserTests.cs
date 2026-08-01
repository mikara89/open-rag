using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using OpenRAG.Api.Security;

namespace OpenRAG.IntegrationTests.Api;

public sealed class HttpContextCurrentUserTests
{
    private static readonly Guid UserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Returns_authenticated_guid_user_id()
    {
        var currentUser = CreateCurrentUser(
        [
            new Claim(OpenRagClaimTypes.UserId, UserId.ToString())
        ]);

        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal(UserId, currentUser.UserId);
    }

    [Fact]
    public void Rejects_missing_authenticated_identity()
    {
        var currentUser = CreateCurrentUser([], authenticationType: null);

        Assert.False(currentUser.IsAuthenticated);
        Assert.Throws<InvalidOperationException>(() => currentUser.UserId);
    }

    [Fact]
    public void Rejects_missing_user_id_claim()
    {
        var currentUser = CreateCurrentUser([]);

        Assert.Throws<InvalidOperationException>(() => currentUser.UserId);
    }

    [Fact]
    public void Rejects_malformed_user_id_claim()
    {
        var currentUser = CreateCurrentUser(
        [
            new Claim(OpenRagClaimTypes.UserId, "not-a-guid")
        ]);

        Assert.Throws<InvalidOperationException>(() => currentUser.UserId);
    }

    [Fact]
    public void Rejects_empty_guid_user_id_claim()
    {
        var currentUser = CreateCurrentUser(
        [
            new Claim(OpenRagClaimTypes.UserId, Guid.Empty.ToString())
        ]);

        Assert.Throws<InvalidOperationException>(() => currentUser.UserId);
    }

    [Fact]
    public void Rejects_duplicate_user_id_claims()
    {
        var currentUser = CreateCurrentUser(
        [
            new Claim(OpenRagClaimTypes.UserId, UserId.ToString()),
            new Claim(OpenRagClaimTypes.UserId, Guid.NewGuid().ToString())
        ]);

        Assert.Throws<InvalidOperationException>(() => currentUser.UserId);
    }

    [Fact]
    public void Does_not_use_tenant_claim_as_user_id()
    {
        var currentUser = CreateCurrentUser(
        [
            new Claim(OpenRagClaimTypes.TenantId, UserId.ToString())
        ]);

        Assert.Throws<InvalidOperationException>(() => currentUser.UserId);
    }

    [Fact]
    public void Uses_configured_user_id_claim_type()
    {
        const string customClaimType = "user_id";
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(OpenRagClaimTypes.UserId, Guid.NewGuid().ToString()),
                new Claim(customClaimType, UserId.ToString())
            ],
            "Bearer"))
        };
        var currentUser = new HttpContextCurrentUser(
            new HttpContextAccessor { HttpContext = context },
            Options.Create(new JwtAuthenticationOptions { UserIdClaimType = customClaimType }));

        Assert.Equal(UserId, currentUser.UserId);
    }

    private static HttpContextCurrentUser CreateCurrentUser(
        IEnumerable<Claim> claims,
        string? authenticationType = "Bearer")
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType))
        };
        var accessor = new HttpContextAccessor { HttpContext = context };
        var options = Options.Create(new JwtAuthenticationOptions());
        return new HttpContextCurrentUser(accessor, options);
    }
}
