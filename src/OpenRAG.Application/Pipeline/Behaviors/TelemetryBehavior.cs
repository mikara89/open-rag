using System.Diagnostics;
using Mediator;

namespace OpenRAG.Application.Pipeline.Behaviors;

public static class OpenRagMediatorTelemetry
{
    public const string ActivitySourceName = "OpenRAG.Application.Mediator";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}

public sealed class TelemetryBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IOpenRagMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var messageName = OpenRagMessageMetadata.Name<TMessage>();
        using var activity = OpenRagMediatorTelemetry.ActivitySource.StartActivity(
            messageName,
            ActivityKind.Internal);
        var stopwatch = Stopwatch.StartNew();

        activity?.SetTag("openrag.message.name", messageName);
        activity?.SetTag(
            "openrag.message.category",
            OpenRagMessageMetadata.Category<TMessage>());

        if (message is ICorrelatedMessage correlated
            && !string.IsNullOrWhiteSpace(correlated.CorrelationId))
        {
            activity?.SetTag("openrag.correlation_id", correlated.CorrelationId);
        }

        if (message is IExplicitTenantMessage explicitTenant
            && explicitTenant.TenantId != Guid.Empty)
        {
            activity?.SetTag("openrag.tenant_id", explicitTenant.TenantId.ToString("D"));
        }

        try
        {
            var response = await next(message, cancellationToken);
            activity?.SetTag("openrag.message.outcome", "success");
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetTag("openrag.message.outcome", "cancelled");
            throw;
        }
        catch (Exception)
        {
            activity?.SetTag("openrag.message.outcome", "error");
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            activity?.SetTag("openrag.duration_ms", stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
