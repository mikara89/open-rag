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

        switch (host)
        {
            case OpenRagPipelineHost.Api:
                services.AddScoped(
                    typeof(IPipelineBehavior<,>),
                    typeof(AuthenticatedContextBehavior<,>));
                services.AddScoped(
                    typeof(IPipelineBehavior<,>),
                    typeof(LoggingScopeBehavior<,>));
                break;

            case OpenRagPipelineHost.Worker:
                services.AddScoped(
                    typeof(IPipelineBehavior<,>),
                    typeof(LoggingScopeBehavior<,>));
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
