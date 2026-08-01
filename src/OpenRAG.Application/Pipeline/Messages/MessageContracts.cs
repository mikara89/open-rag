using Mediator;

namespace OpenRAG.Application.Pipeline;

public interface IOpenRagMessage : IMessage
{
}

public interface IOpenRagCommand : IOpenRagMessage
{
}

public interface IOpenRagCommand<out TResponse>
    : IRequest<TResponse>, IOpenRagCommand
{
}

public interface IOpenRagQuery : IOpenRagMessage
{
}

public interface IOpenRagQuery<out TResponse>
    : IRequest<TResponse>, IOpenRagQuery
{
}

public interface ICorrelatedMessage
{
    string CorrelationId { get; }
}

public interface IAuthenticatedApplicationMessage
{
}

public interface IExplicitTenantMessage
{
    Guid TenantId { get; }
}
