namespace OpenRAG.Application.Common.Results;

public sealed record ApplicationError(
    string Code,
    string Message,
    ApplicationErrorType Type,
    string? Target = null);
