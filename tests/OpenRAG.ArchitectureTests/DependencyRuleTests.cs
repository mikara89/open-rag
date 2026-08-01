using System.Reflection;
using Mediator;
using NetArchTest.Rules;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Abstractions.Vector;
using OpenRAG.Application.Common.Results;
using OpenRAG.Application.Pipeline;
using OpenRAG.Application.Pipeline.Behaviors;
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

    [Fact]
    public void Every_application_request_is_explicitly_classified_as_command_or_query()
    {
        var requestTypes = ApplicationAssembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .Where(type => type.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IRequest<>)))
            .ToArray();

        Assert.Equal(17, requestTypes.Length);
        Assert.All(requestTypes, requestType =>
        {
            Assert.True(typeof(IOpenRagMessage).IsAssignableFrom(requestType));
            var isCommand = typeof(IOpenRagCommand).IsAssignableFrom(requestType);
            var isQuery = typeof(IOpenRagQuery).IsAssignableFrom(requestType);
            Assert.NotEqual(isCommand, isQuery);
        });
    }

    [Fact]
    public void Worker_processing_messages_use_explicit_tenant_contract_not_authenticated_context()
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
            Assert.True(typeof(IExplicitTenantMessage).IsAssignableFrom(commandType));
            Assert.True(typeof(ICorrelatedMessage).IsAssignableFrom(commandType));
            Assert.False(typeof(IAuthenticatedApplicationMessage).IsAssignableFrom(commandType));
        });
    }

    [Fact]
    public void Authenticated_http_messages_do_not_expose_tenant_selection()
    {
        var authenticatedMessages = ApplicationAssembly.GetTypes()
            .Where(type => type.IsClass)
            .Where(type => typeof(IAuthenticatedApplicationMessage).IsAssignableFrom(type))
            .ToArray();

        Assert.Equal(12, authenticatedMessages.Length);
        Assert.All(
            authenticatedMessages,
            messageType => Assert.Null(messageType.GetProperty("TenantId")));
    }

    [Fact]
    public void Generic_pipeline_behaviors_live_in_application_and_have_no_resource_dependencies()
    {
        var behaviorTypes = ApplicationAssembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .Where(type => type.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)))
            .ToArray();

        Assert.Equal(6, behaviorTypes.Length);
        Assert.All(behaviorTypes, behaviorType =>
        {
            Assert.StartsWith("OpenRAG.Application.Pipeline", behaviorType.Namespace, StringComparison.Ordinal);
            var parameters = behaviorType.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .ToArray();

            Assert.DoesNotContain(parameters, parameter => parameter.ParameterType == typeof(IUnitOfWork));
            Assert.DoesNotContain(parameters, parameter => parameter.ParameterType == typeof(IFileStorage));
            Assert.DoesNotContain(parameters, parameter => parameter.ParameterType == typeof(IVectorSearchService));
            Assert.DoesNotContain(
                parameters,
                parameter => parameter.ParameterType.Namespace == "OpenRAG.Application.Abstractions.Persistence");
        });
    }

    [Fact]
    public void Every_authenticated_application_message_is_explicitly_result_based()
    {
        var messages = ApplicationAssembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .Where(type => typeof(IAuthenticatedApplicationMessage).IsAssignableFrom(type))
            .ToArray();

        Assert.Equal(12, messages.Length);
        Assert.All(messages, message =>
        {
            Assert.True(typeof(IResultApplicationMessage).IsAssignableFrom(message));
            var responseType = GetMediatorResponseType(message);
            Assert.True(responseType.IsGenericType);
            Assert.Equal(typeof(Result<>), responseType.GetGenericTypeDefinition());
        });
    }

    [Fact]
    public void Worker_processing_messages_are_neither_marked_nor_returned_as_results()
    {
        Type[] commands =
        [
            typeof(PreprocessDocumentCommand),
            typeof(ChunkDocumentCommand),
            typeof(GenerateIntelligenceCommand),
            typeof(GenerateEmbeddingsCommand)
        ];

        Assert.All(commands, command =>
        {
            Assert.False(typeof(IResultApplicationMessage).IsAssignableFrom(command));
            var responseType = GetMediatorResponseType(command);
            Assert.False(
                responseType.IsGenericType
                && responseType.GetGenericTypeDefinition() == typeof(Result<>));
        });
    }

    [Fact]
    public void Application_result_model_contains_no_http_concepts_or_aspnet_dependencies()
    {
        var resultTypes = ApplicationAssembly.GetTypes()
            .Where(type => type.Namespace == "OpenRAG.Application.Common.Results")
            .ToArray();

        Assert.NotEmpty(resultTypes);
        Assert.All(
            resultTypes,
            type => Assert.DoesNotContain(
                type.GetProperties(),
                property => property.Name.Contains("StatusCode", StringComparison.Ordinal)
                    || property.PropertyType.Namespace?.StartsWith(
                        "Microsoft.AspNetCore",
                        StringComparison.Ordinal) == true));
        Assert.Null(typeof(ApplicationError).GetProperty("StatusCode"));
    }

    [Fact]
    public void Result_to_http_mapping_exists_only_in_api()
    {
        var assemblies = new[] { DomainAssembly, ApplicationAssembly, InfrastructureAssembly, ApiAssembly, WorkerAssembly };
        var mappingMethods = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.Name == "ToHttpResult")
            .ToArray();

        var mapping = Assert.Single(mappingMethods);
        Assert.Equal(ApiAssembly, mapping.DeclaringType!.Assembly);
    }

    [Fact]
    public void Validation_behaviors_do_not_catch_arbitrary_exceptions()
    {
        Type[] behaviors = [typeof(ResultValidationBehavior<,>), typeof(WorkerValidationBehavior<,>)];

        Assert.All(behaviors, behavior =>
        {
            var handle = behavior.GetMethod("Handle")!;
            Assert.DoesNotContain(
                handle.GetMethodBody()!.ExceptionHandlingClauses,
                clause => clause.CatchType == typeof(Exception));
        });
    }

    private static Type GetMediatorResponseType(Type messageType) =>
        messageType.GetInterfaces()
            .Single(contract => contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IRequest<>))
            .GetGenericArguments()[0];

    [Fact]
    public void Worker_tenant_behavior_has_no_ambient_tenant_or_http_dependency()
    {
        var parameters = typeof(ExplicitTenantMessageBehavior<,>)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .ToArray();

        Assert.DoesNotContain(parameters, parameter => parameter.ParameterType == typeof(ICurrentTenant));
        Assert.DoesNotContain(
            parameters,
            parameter => parameter.ParameterType.Namespace?.StartsWith(
                "Microsoft.AspNetCore.Http",
                StringComparison.Ordinal) == true);
    }

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

    [Fact]
    public void Tenant_owned_repository_read_contracts_start_with_explicit_tenant_id()
    {
        Type[] repositoryTypes =
        [
            typeof(IDocumentRepository),
            typeof(IDocumentAuthorizationRepository),
            typeof(IDocumentChunkRepository),
            typeof(IDocumentEmbeddingRepository),
            typeof(IDocumentIntelligenceRepository),
            typeof(IProcessingRunRepository)
        ];

        var readPrefixes = new[] { "Get", "List", "Count", "Any", "Exists" };
        var readMethods = repositoryTypes
            .SelectMany(type => type.GetMethods())
            .Where(method => readPrefixes.Any(prefix =>
                method.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.NotEmpty(readMethods);
        Assert.All(readMethods, method =>
        {
            var first = Assert.Single(method.GetParameters().Take(1));
            Assert.Equal(typeof(Guid), first.ParameterType);
            Assert.Equal("tenantId", first.Name);
        });
    }

    [Fact]
    public void Vector_results_carry_complete_tenant_owned_identity()
    {
        var properties = typeof(VectorSearchResultItem).GetProperties()
            .ToDictionary(property => property.Name, property => property.PropertyType);

        Assert.Equal(typeof(Guid), properties["TenantId"]);
        Assert.Equal(typeof(Guid), properties["DocumentId"]);
        Assert.Equal(typeof(Guid), properties["VersionId"]);
        Assert.Equal(typeof(Guid), properties["ChunkId"]);
    }

    [Fact]
    public void Production_source_contains_no_forbidden_lookup_bypasses_and_only_allowlisted_raw_sql()
    {
        var root = FindSolutionRoot();
        var productionFiles = Directory.GetFiles(
            Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories);

        foreach (var file in productionFiles)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("IgnoreQueryFilters", source, StringComparison.Ordinal);
            Assert.DoesNotContain("FindAsync", source, StringComparison.Ordinal);

            if (source.Contains("SqlQuery", StringComparison.Ordinal)
                || source.Contains("FromSql", StringComparison.Ordinal)
                || source.Contains("ExecuteSql", StringComparison.Ordinal))
            {
                Assert.EndsWith(
                    Path.Combine("Infrastructure", "Vector", "EfVectorSearchService.cs"),
                    file,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
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

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenRAG.slnx")))
            directory = directory.Parent;

        return directory?.FullName
               ?? throw new InvalidOperationException("Could not locate the solution root.");
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
