using Mediator;
using OpenRAG.Application.Pipeline.Validation;

namespace OpenRAG.Application.Pipeline.Behaviors;

public sealed class ValidationBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IOpenRagMessage
{
    private readonly IReadOnlyList<IMessageValidator<TMessage>> _validators;

    public ValidationBehavior(IEnumerable<IMessageValidator<TMessage>> validators)
    {
        _validators = validators.ToArray();
    }

    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        foreach (var validator in _validators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await validator.ValidateAsync(message, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await next(message, cancellationToken);
    }
}
