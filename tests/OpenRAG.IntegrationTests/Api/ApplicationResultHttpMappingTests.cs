using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenRAG.Api.Results;
using OpenRAG.Application.Common.Results;

namespace OpenRAG.IntegrationTests.Api;

public sealed class ApplicationResultHttpMappingTests
{
    [Theory]
    [InlineData(ApplicationErrorType.Validation, 400, "request-validation", "The request is invalid.")]
    [InlineData(ApplicationErrorType.NotFound, 404, "resource-not-found", "Resource not found.")]
    [InlineData(ApplicationErrorType.Conflict, 409, "resource-conflict", "The request conflicts with the resource state.")]
    public async Task Failure_maps_to_problem_details_with_stable_application_errors(
        ApplicationErrorType errorType,
        int expectedStatus,
        string expectedProblemType,
        string expectedTitle)
    {
        var error = new ApplicationError(
            errorType == ApplicationErrorType.NotFound ? "resource.not_found" : "request.safe_code",
            errorType == ApplicationErrorType.NotFound
                ? "The requested resource was not found."
                : "Safe detail.",
            errorType,
            errorType == ApplicationErrorType.Validation ? "pageSize" : null);
        var result = Result<TestPayload>.Failure(error);

        var response = await ExecuteAsync(result);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.ContentType);
        using var json = JsonDocument.Parse(response.Body);
        var root = json.RootElement;
        Assert.Equal($"https://openrag.dev/problems/{expectedProblemType}", root.GetProperty("type").GetString());
        Assert.Equal(expectedTitle, root.GetProperty("title").GetString());
        Assert.Equal("trace-result-test", root.GetProperty("traceId").GetString());
        var publicError = Assert.Single(root.GetProperty("errors").EnumerateArray());
        Assert.Equal(error.Code, publicError.GetProperty("code").GetString());
        Assert.Equal(error.Message, publicError.GetProperty("message").GetString());
        Assert.False(root.TryGetProperty("isSuccess", out _));
        Assert.False(root.TryGetProperty("isFailure", out _));
        Assert.False(publicError.TryGetProperty("type", out _));
    }

    [Fact]
    public async Task Success_serializes_only_endpoint_payload_not_result_wrapper()
    {
        var response = await ExecuteAsync(
            Result<TestPayload>.Success(new TestPayload("visible")));

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        var root = json.RootElement;
        Assert.Equal("visible", root.GetProperty("value").GetString());
        Assert.False(root.TryGetProperty("isSuccess", out _));
        Assert.False(root.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Missing_and_foreign_resources_have_identical_public_problem_details()
    {
        var missing = await ExecuteAsync(
            Result<TestPayload>.Failure(ApplicationErrors.ResourceNotFound()));
        var foreign = await ExecuteAsync(
            Result<TestPayload>.Failure(ApplicationErrors.ResourceNotFound()));

        Assert.Equal(missing.StatusCode, foreign.StatusCode);
        Assert.Equal(missing.Body, foreign.Body);
    }

    private static async Task<CapturedResponse> ExecuteAsync(Result<TestPayload> result)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddProblemDetails();
        using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = provider,
            TraceIdentifier = "trace-result-test"
        };
        await using var body = new MemoryStream();
        context.Response.Body = body;

        var httpResult = result.ToHttpResult(context, value => Results.Ok(value));
        await httpResult.ExecuteAsync(context);
        body.Position = 0;
        using var reader = new StreamReader(body);
        var content = await reader.ReadToEndAsync();

        return new CapturedResponse(
            context.Response.StatusCode,
            context.Response.ContentType,
            content);
    }

    private sealed record TestPayload(string Value);

    private sealed record CapturedResponse(int StatusCode, string? ContentType, string Body);
}
