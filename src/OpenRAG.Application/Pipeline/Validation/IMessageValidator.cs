namespace OpenRAG.Application.Pipeline.Validation;

public interface IMessageValidator<in TMessage>
{
    ValueTask ValidateAsync(
        TMessage message,
        CancellationToken cancellationToken);
}
