using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenRAG.Application;
using OpenRAG.Application.Pipeline;
using OpenRAG.Infrastructure;
using OpenRAG.Worker.Consumers;

namespace OpenRAG.Worker;

public static class WorkerServiceCollectionExtensions
{
    public static IServiceCollection AddOpenRagWorkerApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddOpenRagMediatorPipelines(OpenRagPipelineHost.Worker);

        services.AddTransient<DocumentUploadedConsumer>();
        services.AddTransient<DocumentPreprocessRequestedConsumer>();
        services.AddTransient<DocumentPreprocessedConsumer>();
        services.AddTransient<DocumentChunkingRequestedConsumer>();
        services.AddTransient<DocumentChunkedConsumer>();
        services.AddTransient<DocumentIntelligenceRequestedConsumer>();
        services.AddTransient<DocumentIntelligenceGeneratedConsumer>();
        services.AddTransient<DocumentEmbeddingsRequestedConsumer>();

        return services;
    }
}
