using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OpenRAG.Api.Errors;
using OpenRAG.Application.Common;

namespace OpenRAG.IntegrationTests.Api;

public sealed class ProblemDetailsIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [MemberData(nameof(ExceptionMappings))]
    public async Task Typed_exceptions_produce_stable_safe_problem_details(
        Exception exception,
        int expectedStatus,
        string expectedType)
    {
        var problem = await HandleAsync(exception);

        Assert.Equal(expectedStatus, problem.Status);
        Assert.Equal($"https://openrag.dev/problems/{expectedType}", problem.Type);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.True(problem.Extensions.ContainsKey("traceId"));
        Assert.DoesNotContain("tenant-secret", problem.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("storage-secret", problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_and_foreign_resources_have_identical_public_problem_shape()
    {
        var missing = await HandleAsync(new ResourceNotFoundException());
        var foreign = await HandleAsync(new ResourceNotFoundException());

        Assert.Equal(missing.Status, foreign.Status);
        Assert.Equal(missing.Type, foreign.Type);
        Assert.Equal(missing.Title, foreign.Title);
        Assert.Equal(missing.Detail, foreign.Detail);
        Assert.Equal(ResourceNotFoundException.PublicMessage, missing.Detail);
    }

    [Fact]
    public async Task Unexpected_exception_is_generic_even_in_development()
    {
        var problem = await HandleAsync(
            new InvalidOperationException("tenant-secret storage-secret stack-secret"));

        Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
        Assert.Equal("https://openrag.dev/problems/internal-server-error", problem.Type);
        Assert.DoesNotContain("tenant-secret", problem.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("storage-secret", problem.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("stack-secret", problem.Detail, StringComparison.Ordinal);
    }

    public static TheoryData<Exception, int, string> ExceptionMappings() => new()
    {
        { new RequestValidationException("Invalid request."), 400, "request-validation" },
        { new ResourceNotFoundException(), 404, "resource-not-found" },
        { new ResourceConflictException("Invalid state."), 409, "resource-conflict" },
        { new IsolationViolationException("tenant-secret storage-secret"), 500, "isolation-violation" }
    };

    private static async Task<ProblemDetails> HandleAsync(Exception exception)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        await using var provider = services.BuildServiceProvider();
        var problemDetailsService = provider.GetRequiredService<IProblemDetailsService>();
        var handler = new OpenRagExceptionHandler(
            problemDetailsService,
            new TestHostEnvironment(),
            NullLogger<OpenRagExceptionHandler>.Instance);
        var context = new DefaultHttpContext
        {
            RequestServices = provider,
            TraceIdentifier = "trace-test"
        };
        context.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        context.Response.Body.Position = 0;
        var result = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            context.Response.Body,
            JsonOptions);
        return Assert.IsType<ProblemDetails>(result);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "OpenRAG.IntegrationTests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
