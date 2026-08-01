using System.Diagnostics;
using Mediator;
using OpenRAG.Application.Common.Results;

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

        try
        {
            var response = await next(message, cancellationToken);

            if (response is IApplicationResult { IsFailure: true } rejected)
            {
                var primaryError = rejected.Errors[0];
                activity?.SetTag("openrag.message.outcome", "rejected");
                activity?.SetTag(
                    "openrag.error.type",
                    primaryError.Type switch
                    {
                        ApplicationErrorType.Validation => "validation",
                        ApplicationErrorType.NotFound => "not_found",
                        ApplicationErrorType.Conflict => "conflict",
                        _ => "unknown"
                    });
                activity?.SetTag("openrag.error.code", primaryError.Code);
                return response;
            }

            activity?.SetTag("openrag.message.outcome", "success");
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (OperationCanceledException)
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
