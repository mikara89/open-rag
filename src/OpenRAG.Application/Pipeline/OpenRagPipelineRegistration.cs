using Mediator;
using Microsoft.Extensions.DependencyInjection;
using OpenRAG.Application.Pipeline.Behaviors;

namespace OpenRAG.Application.Pipeline;

public enum OpenRagPipelineHost
{
    Api,
    Worker
}

public static class OpenRagPipelineRegistration
{
    public static IServiceCollection AddOpenRagMediatorPipelines(
        this IServiceCollection services,
        OpenRagPipelineHost host)
    {
        services.AddScoped(
            typeof(IPipelineBehavior<,>),
            typeof(TelemetryBehavior<,>));
        services.AddScoped(
            typeof(IPipelineBehavior<,>),
            typeof(LoggingScopeBehavior<,>));

        switch (host)
        {
            case OpenRagPipelineHost.Api:
                services.AddScoped(
                    typeof(IPipelineBehavior<,>),
                    typeof(AuthenticatedContextBehavior<,>));
                break;

            case OpenRagPipelineHost.Worker:
                services.AddScoped(
                    typeof(IPipelineBehavior<,>),
                    typeof(ExplicitTenantMessageBehavior<,>));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(host), host, null);
        }

        services.AddScoped(
            typeof(IPipelineBehavior<,>),
            typeof(ValidationBehavior<,>));

        return services;
    }
}
