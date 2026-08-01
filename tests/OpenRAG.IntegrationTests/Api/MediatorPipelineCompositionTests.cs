using Mediator;
using Microsoft.Extensions.DependencyInjection;
using OpenRAG.Application.Documents.GetDocumentDetail;
using OpenRAG.Application.Pipeline.Behaviors;

namespace OpenRAG.IntegrationTests.Api;

public sealed class MediatorPipelineCompositionTests
{
    [Fact]
    public void Api_composition_resolves_authenticated_pipeline_and_handler_with_scoped_mediator()
    {
        using var factory = new AuthenticatedApiWebApplicationFactory();
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var firstMediator = firstScope.ServiceProvider.GetRequiredService<IMediator>();
        var sameScopeMediator = firstScope.ServiceProvider.GetRequiredService<IMediator>();
        var secondMediator = secondScope.ServiceProvider.GetRequiredService<IMediator>();
        var pipeline = firstScope.ServiceProvider
            .GetServices<IPipelineBehavior<GetDocumentDetailQuery, GetDocumentDetailResponse>>()
            .ToArray();

        Assert.Same(firstMediator, sameScopeMediator);
        Assert.NotSame(firstMediator, secondMediator);
        Assert.Contains(
            pipeline,
            behavior => behavior is AuthenticatedContextBehavior<GetDocumentDetailQuery, GetDocumentDetailResponse>);
        Assert.DoesNotContain(
            pipeline,
            behavior => behavior.GetType().Name.StartsWith(
                "ExplicitTenantMessageBehavior",
                StringComparison.Ordinal));
        Assert.NotNull(
            firstScope.ServiceProvider
                .GetRequiredService<IRequestHandler<GetDocumentDetailQuery, GetDocumentDetailResponse>>());
    }
}
