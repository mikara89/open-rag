namespace OpenRAG.Application.Common.Results;

public sealed class Result<T> : IApplicationResult<Result<T>>
{
    private static readonly IReadOnlyList<ApplicationError> NoErrors =
        Array.AsReadOnly(Array.Empty<ApplicationError>());

    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        Errors = NoErrors;
        IsSuccess = true;
    }

    private Result(IReadOnlyList<ApplicationError> errors)
    {
        var copy = errors.ToArray();
        if (copy.Length == 0)
            throw new ArgumentException("A failed result requires at least one error.", nameof(errors));

        if (copy.Any(error => error is null))
            throw new ArgumentException("Result errors cannot contain null values.", nameof(errors));

        Errors = Array.AsReadOnly(copy);
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public IReadOnlyList<ApplicationError> Errors { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result does not contain a value.");

    public ApplicationError PrimaryError => IsFailure
        ? Errors[0]
        : throw new InvalidOperationException("A successful result does not contain an error.");

    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>(value);
    }

    public static Result<T> Failure(ApplicationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>([error]);
    }

    public static Result<T> Failure(IReadOnlyList<ApplicationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return new Result<T>(errors);
    }

    public TResult Match<TResult>(
        Func<T, TResult> onSuccess,
        Func<IReadOnlyList<ApplicationError>, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(Value) : onFailure(Errors);
    }
}
