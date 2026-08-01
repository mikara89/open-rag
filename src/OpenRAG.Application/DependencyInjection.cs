using Microsoft.Extensions.DependencyInjection;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Storage;

namespace OpenRAG.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers Application services. Mediator source generator handles
    /// IRequestHandler registrations automatically in the composition root.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IDocumentObjectKeyPolicy, DocumentObjectKeyPolicy>();
        return services;
    }
}
