using Mediator;
using OpenRAG.Application.Common.Results;
using OpenRAG.Application.Pipeline.Validation;

namespace OpenRAG.Application.Pipeline.Behaviors;

public sealed class ResultValidationBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IOpenRagMessage, IResultApplicationMessage
    where TResponse : IApplicationResult<TResponse>
{
    private readonly IReadOnlyList<IMessageValidator<TMessage>> _validators;

    public ResultValidationBehavior(IEnumerable<IMessageValidator<TMessage>> validators)
    {
        _validators = validators.ToArray();
    }

    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var errors = new List<ApplicationError>();

        foreach (var validator in _validators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validationErrors = await validator.ValidateAsync(message, cancellationToken);
            errors.AddRange(validationErrors);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (errors.Count > 0)
            return TResponse.Failure(Array.AsReadOnly(errors.ToArray()));

        return await next(message, cancellationToken);
    }
}
