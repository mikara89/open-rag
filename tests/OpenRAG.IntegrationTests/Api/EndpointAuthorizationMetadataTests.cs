using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenRAG.Api.Security;
using OpenRAG.Application.Abstractions.Security;

namespace OpenRAG.IntegrationTests.Api;

public sealed class EndpointAuthorizationMetadataTests
    : IClassFixture<AuthenticatedApiWebApplicationFactory>
{
    private readonly AuthenticatedApiWebApplicationFactory _factory;

    public EndpointAuthorizationMetadataTests(AuthenticatedApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Api_endpoints_are_protected_and_development_openapi_is_anonymous()
    {
        _ = _factory.Server;
        var endpoints = _factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        var apiEndpoints = endpoints
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(apiEndpoints);
        Assert.All(apiEndpoints, endpoint =>
        {
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        });

        var administratorEndpoint = Assert.Single(
            apiEndpoints,
            endpoint => endpoint.RoutePattern.RawText == "/api/system/providers");
        Assert.Contains(
            administratorEndpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => string.Equals(
                metadata.Policy,
                OpenRagPolicies.Administrator,
                StringComparison.Ordinal));

        var openApiEndpoint = Assert.Single(
            endpoints,
            endpoint => endpoint.RoutePattern.RawText == "/openapi/{documentName}.json");
        Assert.NotNull(openApiEndpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [Fact]
    public void Api_composition_root_uses_http_principal_for_user_and_tenant()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.IsType<HttpContextCurrentUser>(scope.ServiceProvider.GetRequiredService<ICurrentUser>());
        Assert.IsType<HttpContextCurrentTenant>(scope.ServiceProvider.GetRequiredService<ICurrentTenant>());
    }

    [Fact]
    public async Task Authorization_policies_require_valid_user_and_tenant_identities()
    {
        var provider = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var authenticatedUser = await provider.GetPolicyAsync(OpenRagPolicies.AuthenticatedUser);
        Assert.NotNull(authenticatedUser);
        Assert.Contains(authenticatedUser.Requirements, requirement => requirement is ValidUserIdentityRequirement);
        Assert.Contains(authenticatedUser.Requirements, requirement => requirement is ValidTenantIdentityRequirement);

        var administrator = await provider.GetPolicyAsync(OpenRagPolicies.Administrator);
        Assert.NotNull(administrator);
        Assert.Contains(administrator.Requirements, requirement => requirement is ValidUserIdentityRequirement);
        Assert.Contains(administrator.Requirements, requirement => requirement is ValidTenantIdentityRequirement);
        Assert.Contains(administrator.Requirements, requirement =>
            requirement is RolesAuthorizationRequirement roles
            && roles.AllowedRoles.Contains(OpenRagRoles.Administrator, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_api_endpoint_combines_to_valid_user_and_tenant_requirements()
    {
        _ = _factory.Server;
        var provider = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var apiEndpoints = _factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(apiEndpoints);
        foreach (var endpoint in apiEndpoints)
        {
            var policy = await AuthorizationPolicy.CombineAsync(
                provider,
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.NotNull(policy);
            Assert.Contains(policy.Requirements, requirement => requirement is ValidUserIdentityRequirement);
            Assert.Contains(policy.Requirements, requirement => requirement is ValidTenantIdentityRequirement);
        }
    }
}
