namespace OpenRAG.Application.Common.Results;

public static class ApplicationErrors
{
    public static ApplicationError InvalidRequest(
        string code,
        string message,
        string? target = null) =>
        new(code, message, ApplicationErrorType.Validation, target);

    public static ApplicationError ResourceNotFound() =>
        new(
            "resource.not_found",
            "The requested resource was not found.",
            ApplicationErrorType.NotFound);

    public static ApplicationError ResourceConflict(string code, string message) =>
        new(code, message, ApplicationErrorType.Conflict);
}
