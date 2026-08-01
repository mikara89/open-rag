namespace OpenRAG.Application.Common.Results;

public interface IApplicationResult
{
    bool IsSuccess { get; }

    bool IsFailure { get; }

    IReadOnlyList<ApplicationError> Errors { get; }
}

public interface IApplicationResult<TSelf> : IApplicationResult
    where TSelf : IApplicationResult<TSelf>
{
    static abstract TSelf Failure(IReadOnlyList<ApplicationError> errors);
}
