using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRAG.Api.Security;
using OpenRAG.Application;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common.Results;
using OpenRAG.Application.Documents.GetDocumentDetail;
using OpenRAG.Application.Pipeline;
using OpenRAG.Application.Pipeline.Behaviors;

namespace OpenRAG.IntegrationTests.Api;

public sealed class MediatorPipelineCompositionTests
{
    private const string ContextRequiredMessage =
        "An authenticated user and tenant context is required.";

    private static readonly Guid UserId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantId = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Api_composition_resolves_authenticated_pipeline_and_handler_with_scoped_mediator()
    {
        using var factory = new AuthenticatedApiWebApplicationFactory();
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var firstMediator = firstScope.ServiceProvider.GetRequiredService<IMediator>();
        var sameScopeMediator = firstScope.ServiceProvider.GetRequiredService<IMediator>();
        var secondMediator = secondScope.ServiceProvider.GetRequiredService<IMediator>();
        var pipeline = firstScope.ServiceProvider
            .GetServices<IPipelineBehavior<GetDocumentDetailQuery, Result<GetDocumentDetailResponse>>>()
            .ToArray();

        Assert.Same(firstMediator, sameScopeMediator);
        Assert.NotSame(firstMediator, secondMediator);
        Assert.Contains(
            pipeline,
            behavior => behavior is AuthenticatedContextBehavior<GetDocumentDetailQuery, Result<GetDocumentDetailResponse>>);
        Assert.DoesNotContain(
            pipeline,
            behavior => behavior.GetType().Name.StartsWith(
                "ExplicitTenantMessageBehavior",
                StringComparison.Ordinal));
        Assert.NotNull(
            firstScope.ServiceProvider
                .GetRequiredService<IRequestHandler<GetDocumentDetailQuery, Result<GetDocumentDetailResponse>>>());
    }

    [Theory]
    [InlineData("no-http-context")]
    [InlineData("unauthenticated")]
    [InlineData("missing-user")]
    [InlineData("missing-tenant")]
    [InlineData("malformed-user")]
    [InlineData("malformed-tenant")]
    public async Task Api_pipeline_normalizes_real_context_failures_before_logging(
        string scenario)
    {
        using var activityListener = new CapturingActivityListener();
        var contextAccessor = new HttpContextAccessor
        {
            HttpContext = CreateHttpContext(scenario)
        };
        var loggingLogger =
            new ScopeCapturingLogger<LoggingScopeBehavior<GetDocumentDetailQuery, Result<GetDocumentDetailResponse>>>();
        var handler = new RecordingGetDocumentDetailHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddOpenRagMediatorPipelines(OpenRagPipelineHost.Api);
        services.AddSingleton<IHttpContextAccessor>(contextAccessor);
        services.AddSingleton<IOptions<JwtAuthenticationOptions>>(
            Options.Create(new JwtAuthenticationOptions()));
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
        services.AddSingleton<ILogger<LoggingScopeBehavior<GetDocumentDetailQuery, Result<GetDocumentDetailResponse>>>>(
            loggingLogger);
        services.AddScoped<IRequestHandler<GetDocumentDetailQuery, Result<GetDocumentDetailResponse>>>(
            _ => handler);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => mediator.Send(
                new GetDocumentDetailQuery(Guid.NewGuid()),
                CancellationToken.None).AsTask());

        Assert.Equal(ContextRequiredMessage, exception.Message);
        if (scenario is "no-http-context" or "unauthenticated")
            Assert.Null(exception.InnerException);
        else
            Assert.IsType<InvalidOperationException>(exception.InnerException);

        Assert.Equal(0, handler.CallCount);
        Assert.Empty(loggingLogger.Scopes);

        var activity = Assert.Single(
            activityListener.StoppedActivities,
            item => item.OperationName == nameof(GetDocumentDetailQuery));
        Assert.Equal("error", activity.GetTagItem("openrag.message.outcome"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task Api_pipeline_authenticates_logs_and_rejects_validation_before_handler()
    {
        using var activityListener = new CapturingActivityListener();
        var contextAccessor = new HttpContextAccessor
        {
            HttpContext = CreateHttpContext("valid")
        };
        var loggingLogger =
            new ScopeCapturingLogger<LoggingScopeBehavior<GetDocumentDetailQuery, Result<GetDocumentDetailResponse>>>();
        var handler = new RecordingGetDocumentDetailHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddOpenRagMediatorPipelines(OpenRagPipelineHost.Api);
        services.AddSingleton<IHttpContextAccessor>(contextAccessor);
        services.AddSingleton<IOptions<JwtAuthenticationOptions>>(
            Options.Create(new JwtAuthenticationOptions()));
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
        services.AddSingleton<ILogger<LoggingScopeBehavior<GetDocumentDetailQuery, Result<GetDocumentDetailResponse>>>>(
            loggingLogger);
        services.AddScoped<IRequestHandler<GetDocumentDetailQuery, Result<GetDocumentDetailResponse>>>(
            _ => handler);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
            new GetDocumentDetailQuery(Guid.Empty),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("request.document_id_required", result.PrimaryError.Code);
        Assert.Equal(0, handler.CallCount);
        Assert.Single(loggingLogger.Scopes);
        var activity = Assert.Single(
            activityListener.StoppedActivities,
            item => item.OperationName == nameof(GetDocumentDetailQuery));
        Assert.Equal("rejected", activity.GetTagItem("openrag.message.outcome"));
        Assert.Equal("validation", activity.GetTagItem("openrag.error.type"));
        Assert.Equal("request.document_id_required", activity.GetTagItem("openrag.error.code"));
    }

    private static DefaultHttpContext? CreateHttpContext(string scenario)
    {
        if (scenario == "no-http-context")
            return null;

        var claims = scenario switch
        {
            "valid" => ValidClaims(),
            "unauthenticated" => ValidClaims(),
            "missing-user" =>
            [
                new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString("D"))
            ],
            "missing-tenant" =>
            [
                new Claim(OpenRagClaimTypes.UserId, UserId.ToString("D"))
            ],
            "malformed-user" =>
            [
                new Claim(OpenRagClaimTypes.UserId, "not-a-guid"),
                new Claim(OpenRagClaimTypes.TenantId, TenantId.ToString("D"))
            ],
            "malformed-tenant" =>
            [
                new Claim(OpenRagClaimTypes.UserId, UserId.ToString("D")),
                new Claim(OpenRagClaimTypes.TenantId, "not-a-guid")
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };
        var authenticationType = scenario == "unauthenticated" ? null : "Bearer";

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType))
        };
    }

    private static Claim[] ValidClaims() =>
    [
        new(OpenRagClaimTypes.UserId, UserId.ToString("D")),
        new(OpenRagClaimTypes.TenantId, TenantId.ToString("D"))
    ];

    private sealed class RecordingGetDocumentDetailHandler
        : IRequestHandler<GetDocumentDetailQuery, Result<GetDocumentDetailResponse>>
    {
        public int CallCount { get; private set; }

        public ValueTask<Result<GetDocumentDetailResponse>> Handle(
            GetDocumentDetailQuery request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(Result<GetDocumentDetailResponse>.Success(
                new GetDocumentDetailResponse(
                    request.DocumentId,
                    "document.pdf",
                    "Ready",
                    DateTime.UnixEpoch,
                    DateTime.UnixEpoch,
                    null,
                    null)));
        }
    }

    private sealed class ScopeCapturingLogger<T> : ILogger<T>
    {
        public List<IReadOnlyDictionary<string, object?>> Scopes { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            var values = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state);
            Scopes.Add(values.ToDictionary(pair => pair.Key, pair => pair.Value));
            return NoOpDisposable.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed class CapturingActivityListener : IDisposable
    {
        public CapturingActivityListener()
        {
            Listener = new ActivityListener
            {
                ShouldListenTo = source =>
                    source.Name == OpenRagMediatorTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => StoppedActivities.Enqueue(activity)
            };
            ActivitySource.AddActivityListener(Listener);
        }

        public ActivityListener Listener { get; }
        public ConcurrentQueue<Activity> StoppedActivities { get; } = [];

        public void Dispose() => Listener.Dispose();
    }
}
