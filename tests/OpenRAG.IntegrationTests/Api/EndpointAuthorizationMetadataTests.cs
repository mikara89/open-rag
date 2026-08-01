using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenRAG.Api.Security;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Infrastructure.Security;

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
    public void Api_composition_root_uses_http_current_user_and_retains_development_tenant()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.IsType<HttpContextCurrentUser>(scope.ServiceProvider.GetRequiredService<ICurrentUser>());
        Assert.IsType<DevelopmentCurrentTenant>(scope.ServiceProvider.GetRequiredService<ICurrentTenant>());
    }
}
