using OpenRAG.Application.Common.Results;

namespace OpenRAG.Api.Results;

public static class ApplicationResultHttpExtensions
{
    private const string ProblemBase = "https://openrag.dev/problems/";

    public static IResult ToHttpResult<T>(
        this Result<T> result,
        HttpContext httpContext,
        Func<T, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (result.IsSuccess)
            return onSuccess(result.Value);

        var mapping = Map(result.PrimaryError.Type);
        var extensions = new Dictionary<string, object?>
        {
            ["traceId"] = httpContext.TraceIdentifier,
            ["errors"] = result.Errors.Select(ToPublicError).ToArray()
        };

        return Microsoft.AspNetCore.Http.Results.Problem(
            statusCode: mapping.Status,
            type: $"{ProblemBase}{mapping.Type}",
            title: mapping.Title,
            detail: result.PrimaryError.Message,
            extensions: extensions);
    }

    private static IReadOnlyDictionary<string, object?> ToPublicError(ApplicationError error)
    {
        var publicError = new Dictionary<string, object?>
        {
            ["code"] = error.Code,
            ["message"] = error.Message
        };

        if (!string.IsNullOrWhiteSpace(error.Target))
            publicError["target"] = error.Target;

        return publicError;
    }

    private static ProblemMapping Map(ApplicationErrorType type) => type switch
    {
        ApplicationErrorType.Validation => new(
            StatusCodes.Status400BadRequest,
            "request-validation",
            "The request is invalid."),
        ApplicationErrorType.NotFound => new(
            StatusCodes.Status404NotFound,
            "resource-not-found",
            "Resource not found."),
        ApplicationErrorType.Conflict => new(
            StatusCodes.Status409Conflict,
            "resource-conflict",
            "The request conflicts with the resource state."),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private sealed record ProblemMapping(int Status, string Type, string Title);
}
