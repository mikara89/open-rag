using Mediator;
using OpenRAG.Application.Common;
using OpenRAG.Application.Pipeline.Validation;

namespace OpenRAG.Application.Pipeline.Behaviors;

public sealed class WorkerValidationBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IOpenRagMessage, IExplicitTenantMessage
{
    private readonly IReadOnlyList<IMessageValidator<TMessage>> _validators;

    public WorkerValidationBehavior(IEnumerable<IMessageValidator<TMessage>> validators)
    {
        _validators = validators.ToArray();
    }

    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        foreach (var validator in _validators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validationErrors = await validator.ValidateAsync(message, cancellationToken);
            errors.AddRange(validationErrors.Select(error => error.Message));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (errors.Count > 0)
            throw new RequestValidationException(string.Join(" ", errors));

        return await next(message, cancellationToken);
    }
}
