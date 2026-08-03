using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OpenRAG.Application.Common;
using OpenRAG.Domain.Common;

namespace OpenRAG.Api.Errors;

public sealed class OpenRagExceptionHandler : IExceptionHandler
{
    private const string ProblemBase = "https://openrag.dev/problems/";

    private readonly IProblemDetailsService _problemDetailsService;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<OpenRagExceptionHandler> _logger;

    public OpenRagExceptionHandler(
        IProblemDetailsService problemDetailsService,
        IHostEnvironment environment,
        ILogger<OpenRagExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _environment = environment;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException)
            return false;

        var mapping = Map(exception);

        if (exception is IsolationViolationException || mapping.Status == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "Request failed with an internal isolation or server error. TraceId={TraceId}, Path={Path}, Status={Status}",
                httpContext.TraceIdentifier,
                httpContext.Request.Path.Value,
                mapping.Status);
        }

        httpContext.Response.StatusCode = mapping.Status;
        var problem = new ProblemDetails
        {
            Status = mapping.Status,
            Type = $"{ProblemBase}{mapping.Type}",
            Title = mapping.Title,
            Detail = mapping.Detail
        };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private ProblemMapping Map(Exception exception) => exception switch
    {
        RequestValidationException validation => new(
            StatusCodes.Status400BadRequest,
            "request-validation",
            "The request is invalid.",
            validation.Message),
        DomainException conflict => new(
            StatusCodes.Status409Conflict,
            "resource-conflict",
            "The request conflicts with the resource state.",
            conflict.Message),
        IsolationViolationException => Internal("isolation-violation"),
        _ => Internal("internal-server-error")
    };

    private ProblemMapping Internal(string type) => new(
        StatusCodes.Status500InternalServerError,
        type,
        "An internal server error occurred.",
        _environment.IsDevelopment() && type == "internal-server-error"
            ? "An unexpected server error occurred. Inspect server logs using the trace identifier."
            : "The server could not complete the request.");

    private sealed record ProblemMapping(int Status, string Type, string Title, string Detail);
}
