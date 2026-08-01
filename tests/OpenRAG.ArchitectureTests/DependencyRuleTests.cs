using System.Reflection;
using NetArchTest.Rules;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Processing.ChunkDocument;
using OpenRAG.Application.Processing.GenerateEmbeddings;
using OpenRAG.Application.Processing.GenerateIntelligence;
using OpenRAG.Application.Processing.PreprocessDocument;

namespace OpenRAG.ArchitectureTests;

public class DependencyRuleTests
{
    private static readonly Assembly DomainAssembly = typeof(OpenRAG.Domain.AssemblyReference).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(OpenRAG.Application.AssemblyReference).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(OpenRAG.Infrastructure.AssemblyReference).Assembly;
    private static readonly Assembly ApiAssembly = typeof(OpenRAG.Api.AssemblyReference).Assembly;
    private static readonly Assembly WorkerAssembly = typeof(OpenRAG.Worker.AssemblyReference).Assembly;

    // ── Domain rules ──────────────────────────────────────────────

    [Fact]
    public void Domain_MustNotReference_Application()
        => Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOn("OpenRAG.Application")
            .GetResult().ShouldSucceed();

    [Fact]
    public void Domain_MustNotReference_Infrastructure()
        => Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOn("OpenRAG.Infrastructure")
            .GetResult().ShouldSucceed();

    [Fact]
    public void Domain_MustNotReference_Api()
        => Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOn("OpenRAG.Api")
            .GetResult().ShouldSucceed();

    [Fact]
    public void Domain_MustNotReference_Worker()
        => Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOn("OpenRAG.Worker")
            .GetResult().ShouldSucceed();

    [Fact]
    public void Domain_MustNotReference_AspNetCore()
        => Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult().ShouldSucceed();

    [Fact]
    public void Domain_MustNotReference_EntityFrameworkCore()
        => Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult().ShouldSucceed();

    [Fact]
    public void Domain_MustNotReference_Mediator()
        => Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOn("Mediator")
            .GetResult().ShouldSucceed();

    // ── Application rules ─────────────────────────────────────────

    [Fact]
    public void Application_MustNotReference_Infrastructure()
        => Types.InAssembly(ApplicationAssembly)
            .ShouldNot().HaveDependencyOn("OpenRAG.Infrastructure")
            .GetResult().ShouldSucceed();

    [Fact]
    public void Application_MustNotReference_Api()
        => Types.InAssembly(ApplicationAssembly)
            .ShouldNot().HaveDependencyOn("OpenRAG.Api")
            .GetResult().ShouldSucceed();

    [Fact]
    public void Application_MustNotReference_Worker()
        => Types.InAssembly(ApplicationAssembly)
            .ShouldNot().HaveDependencyOn("OpenRAG.Worker")
            .GetResult().ShouldSucceed();

    [Fact]
    public void Application_MustNotReference_AspNetCore()
        => Types.InAssembly(ApplicationAssembly)
            .ShouldNot().HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult().ShouldSucceed();

    // ── Infrastructure rules ──────────────────────────────────────

    [Fact]
    public void Infrastructure_MustNotReference_Api()
        => Types.InAssembly(InfrastructureAssembly)
            .ShouldNot().HaveDependencyOn("OpenRAG.Api")
            .GetResult().ShouldSucceed();

    [Fact]
    public void Infrastructure_MustNotReference_Worker()
        => Types.InAssembly(InfrastructureAssembly)
            .ShouldNot().HaveDependencyOn("OpenRAG.Worker")
            .GetResult().ShouldSucceed();

    [Fact]
    public void Worker_MustNotReference_Api_http_tenant_resolution()
        => Types.InAssembly(WorkerAssembly)
            .ShouldNot().HaveDependencyOn("OpenRAG.Api")
            .GetResult().ShouldSucceed();

    [Fact]
    public void Infrastructure_contains_no_development_current_tenant_fallback()
        => Assert.DoesNotContain(
            InfrastructureAssembly.GetTypes(),
            type => type.Name.Contains("DevelopmentCurrentTenant", StringComparison.Ordinal));

    [Fact]
    public void Background_processing_handlers_do_not_depend_on_ambient_tenant_context()
    {
        Type[] handlerTypes =
        [
            typeof(PreprocessDocumentHandler),
            typeof(ChunkDocumentHandler),
            typeof(GenerateIntelligenceHandler),
            typeof(GenerateEmbeddingsHandler)
        ];

        Assert.All(handlerTypes, handlerType =>
            Assert.DoesNotContain(
                handlerType.GetConstructors().SelectMany(constructor => constructor.GetParameters()),
                parameter => parameter.ParameterType == typeof(ICurrentTenant)));
    }

    [Fact]
    public void Every_background_processing_command_has_public_guid_tenant_id()
    {
        Type[] commandTypes =
        [
            typeof(PreprocessDocumentCommand),
            typeof(ChunkDocumentCommand),
            typeof(GenerateIntelligenceCommand),
            typeof(GenerateEmbeddingsCommand)
        ];

        Assert.All(commandTypes, commandType =>
        {
            var tenantProperty = commandType.GetProperty("TenantId", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(tenantProperty);
            Assert.Equal(typeof(Guid), tenantProperty.PropertyType);
        });
    }

    [Fact]
    public void Every_document_event_contract_has_public_guid_tenant_id()
    {
        var eventTypes = ApplicationAssembly.GetTypes()
            .Where(type => type.Namespace == "OpenRAG.Application.Messaging.Events")
            .ToArray();

        Assert.NotEmpty(eventTypes);
        Assert.All(eventTypes, eventType =>
        {
            var tenantProperty = eventType.GetProperty("TenantId", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(tenantProperty);
            Assert.Equal(typeof(Guid), tenantProperty.PropertyType);
        });
    }

    // ── Controller rule ───────────────────────────────────────────

    [Fact]
    public void ApiControllers_MustNotUse_AppDbContext_Directly()
    {
        var dbContextType = InfrastructureAssembly
            .GetTypes()
            .FirstOrDefault(t => t.Name == "AppDbContext");

        if (dbContextType is null)
            return; // not yet created — skip

        Types.InAssembly(ApiAssembly)
            .That().ResideInNamespace("OpenRAG.Api.Controllers")
            .ShouldNot().HaveDependencyOn(dbContextType.FullName!)
            .GetResult().ShouldSucceed();
    }
}

internal static class ArchitectureTestExtensions
{
    public static void ShouldSucceed(this TestResult result)
    {
        if (!result.IsSuccessful)
        {
            var failures = string.Join(Environment.NewLine, result.FailingTypeNames ?? []);
            Assert.Fail($"Architecture rule failed:{Environment.NewLine}{failures}");
        }
    }
}
