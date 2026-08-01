using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenRAG.Api;

namespace OpenRAG.IntegrationTests.Api;

public sealed class OpenApiEndpointTests
{
    [Fact]
    public async Task OpenApi_document_is_generated_in_mock_mode()
    {
        using var factory = new OpenApiWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

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
    }

    private sealed class OpenApiWebApplicationFactory : WebApplicationFactory<AssemblyReference>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:openrag-db"] =
                        "Host=localhost;Port=5432;Database=openrag_openapi_test;Username=test;Password=test",
                    ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@localhost:5672/",
                    ["Storage:LocalRootPath"] = Path.Combine(Path.GetTempPath(), "openrag-openapi-tests"),
                    ["Preprocessing:Docling:Provider"] = "Mock",
                    ["AI:Embeddings:Provider"] = "Mock",
                    ["AI:Chat:Provider"] = "Mock",
                    ["Intelligence:Provider"] = "Mock"
                });
            });

            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }
    }
}
