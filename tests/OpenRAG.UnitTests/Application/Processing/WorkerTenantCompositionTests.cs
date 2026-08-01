using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRAG.Application;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Infrastructure;
using OpenRAG.Worker.Consumers;

namespace OpenRAG.UnitTests.Application.Processing;

public sealed class WorkerTenantCompositionTests
{
    [Fact]
    public void Worker_composition_builds_and_resolves_processing_consumers_without_current_tenant()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:openrag-db"] =
                    "Host=localhost;Port=5432;Database=openrag_worker_test;Username=test;Password=test",
                ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@localhost:5672/",
                ["Storage:LocalRootPath"] = Path.Combine(Path.GetTempPath(), "openrag-worker-tests")
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddTransient<DocumentPreprocessRequestedConsumer>();
        services.AddTransient<DocumentChunkingRequestedConsumer>();
        services.AddTransient<DocumentIntelligenceRequestedConsumer>();
        services.AddTransient<DocumentEmbeddingsRequestedConsumer>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<ICurrentTenant>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DocumentPreprocessRequestedConsumer>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DocumentChunkingRequestedConsumer>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DocumentIntelligenceRequestedConsumer>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DocumentEmbeddingsRequestedConsumer>());
    }
}
