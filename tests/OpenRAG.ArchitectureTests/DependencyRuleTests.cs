using System.Reflection;
using NetArchTest.Rules;

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
