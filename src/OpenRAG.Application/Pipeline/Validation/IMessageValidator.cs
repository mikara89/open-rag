using OpenRAG.Application.Common.Results;

namespace OpenRAG.Application.Pipeline.Validation;

public interface IMessageValidator<in TMessage>
{
    ValueTask<IReadOnlyList<ApplicationError>> ValidateAsync(
        TMessage message,
        CancellationToken cancellationToken);
}
