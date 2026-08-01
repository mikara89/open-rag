using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenRAG.Application;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;
using OpenRAG.Application.Documents.GetDocumentDetail;
using OpenRAG.Application.Pipeline;
using OpenRAG.Application.Pipeline.Behaviors;
using OpenRAG.Application.Pipeline.Validation;
using OpenRAG.Application.Processing.GenerateEmbeddings;

namespace OpenRAG.UnitTests.Application.Pipeline;

public sealed class MediatorPipelineBehaviorTests
{
    private static readonly Guid TenantId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Validation_without_validators_executes_handler_once()
    {
        var behavior = new ValidationBehavior<TestCommand, string>([]);
        var calls = 0;

        var result = await behavior.Handle(
            new TestCommand("corr", "sensitive-question"),
            Handler,
            CancellationToken.None);

        Assert.Equal("handled", result);
        Assert.Equal(1, calls);
        return;

        ValueTask<string> Handler(TestCommand _, CancellationToken __)
        {
            calls++;
            return ValueTask.FromResult("handled");
        }
    }

    [Fact]
    public async Task Validation_with_valid_validator_executes_handler_once()
    {
        var validator = new RecordingValidator<TestCommand>("valid", []);
        var behavior = new ValidationBehavior<TestCommand, string>([validator]);
        var calls = 0;

        await behavior.Handle(
            new TestCommand("corr", "sensitive-question"),
            (_, _) =>
            {
                calls++;
                return ValueTask.FromResult("handled");
            },
            CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal(1, validator.CallCount);
    }

    [Fact]
    public async Task Validation_failure_short_circuits_handler_and_later_validators()
    {
        var order = new List<string>();
        var failure = new RequestValidationException("invalid");
        var first = new RecordingValidator<TestCommand>("first", order, failure);
        var second = new RecordingValidator<TestCommand>("second", order);
        var behavior = new ValidationBehavior<TestCommand, string>([first, second]);
        var handlerCalls = 0;

        var actual = await Assert.ThrowsAsync<RequestValidationException>(
            () => behavior.Handle(
                new TestCommand("corr", "sensitive-question"),
                (_, _) =>
                {
                    handlerCalls++;
                    return ValueTask.FromResult("handled");
                },
                CancellationToken.None).AsTask());

        Assert.Same(failure, actual);
        Assert.Equal(["first"], order);
        Assert.Equal(0, second.CallCount);
        Assert.Equal(0, handlerCalls);
    }

    [Fact]
    public async Task Validation_executes_multiple_validators_in_registration_order()
    {
        var order = new List<string>();
        var behavior = new ValidationBehavior<TestCommand, string>(
        [
            new RecordingValidator<TestCommand>("first", order),
            new RecordingValidator<TestCommand>("second", order)
        ]);

        await behavior.Handle(
            new TestCommand("corr", "sensitive-question"),
            (_, _) =>
            {
                order.Add("handler");
                return ValueTask.FromResult("handled");
            },
            CancellationToken.None);

        Assert.Equal(["first", "second", "handler"], order);
    }

    [Fact]
    public async Task Validation_propagates_cancellation_without_handler_execution()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var behavior = new ValidationBehavior<TestCommand, string>([]);
        var handlerCalls = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => behavior.Handle(
                new TestCommand("corr", "sensitive-question"),
                (_, _) =>
                {
                    handlerCalls++;
                    return ValueTask.FromResult("handled");
                },
                cancellation.Token).AsTask());

        Assert.Equal(0, handlerCalls);
    }

    [Fact]
    public async Task Authenticated_context_executes_handler_for_valid_user_and_tenant()
    {
        var logger = new ScopeCapturingLogger<AuthenticatedContextBehavior<AuthenticatedTestCommand, string>>();
        var behavior = new AuthenticatedContextBehavior<AuthenticatedTestCommand, string>(
            new StubCurrentUser(UserId, true),
            new StubCurrentTenant(TenantId),
            logger);
        var calls = 0;

        await behavior.Handle(
            new AuthenticatedTestCommand(),
            (_, _) =>
            {
                calls++;
                return ValueTask.FromResult("handled");
            },
            CancellationToken.None);

        Assert.Equal(1, calls);
        var scope = Assert.Single(logger.Scopes);
        Assert.Equal(UserId, scope["UserId"]);
        Assert.Equal(TenantId, scope["TenantId"]);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task Authenticated_context_rejects_missing_identity_before_handler(
        bool authenticated,
        bool emptyUser,
        bool emptyTenant)
    {
        var logger = new ScopeCapturingLogger<AuthenticatedContextBehavior<AuthenticatedTestCommand, string>>();
        var behavior = new AuthenticatedContextBehavior<AuthenticatedTestCommand, string>(
            new StubCurrentUser(emptyUser ? Guid.Empty : UserId, authenticated),
            new StubCurrentTenant(emptyTenant ? Guid.Empty : TenantId),
            logger);
        var calls = 0;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => behavior.Handle(
                new AuthenticatedTestCommand(),
                (_, _) =>
                {
                    calls++;
                    return ValueTask.FromResult("handled");
                },
                CancellationToken.None).AsTask());

        Assert.Equal(0, calls);
        Assert.Empty(logger.Scopes);
    }

    [Fact]
    public async Task Explicit_tenant_guard_preserves_message_tenant_and_executes_once()
    {
        var logger = new ScopeCapturingLogger<ExplicitTenantMessageBehavior<WorkerTestCommand, string>>();
        var behavior = new ExplicitTenantMessageBehavior<WorkerTestCommand, string>(logger);
        var message = new WorkerTestCommand(TenantId, "corr", "sensitive-content");
        var calls = 0;

        await behavior.Handle(
            message,
            (actual, _) =>
            {
                calls++;
                Assert.Same(message, actual);
                Assert.Equal(TenantId, actual.TenantId);
                return ValueTask.FromResult("handled");
            },
            CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal(TenantId, logger.Scopes.Single()["TenantId"]);
    }

    [Fact]
    public async Task Explicit_tenant_guard_rejects_empty_tenant_before_handler()
    {
        var logger = new ScopeCapturingLogger<ExplicitTenantMessageBehavior<WorkerTestCommand, string>>();
        var behavior = new ExplicitTenantMessageBehavior<WorkerTestCommand, string>(logger);
        var calls = 0;

        await Assert.ThrowsAsync<RequestValidationException>(
            () => behavior.Handle(
                new WorkerTestCommand(Guid.Empty, "corr", "sensitive-content"),
                (_, _) =>
                {
                    calls++;
                    return ValueTask.FromResult("handled");
                },
                CancellationToken.None).AsTask());

        Assert.Equal(0, calls);
        Assert.Empty(logger.Scopes);
    }

    [Fact]
    public async Task Telemetry_records_safe_success_tags_without_message_content()
    {
        using var listener = CreateActivityListener();
        var behavior = new TelemetryBehavior<WorkerTestCommand, string>();
        var message = new WorkerTestCommand(TenantId, "corr-safe", "sensitive-content");

        await behavior.Handle(
            message,
            (_, _) => ValueTask.FromResult("handled"),
            CancellationToken.None);

        var activity = Assert.Single(listener.StoppedActivities);
        Assert.Equal(nameof(WorkerTestCommand), activity.GetTagItem("openrag.message.name"));
        Assert.Equal("command", activity.GetTagItem("openrag.message.category"));
        Assert.Equal("success", activity.GetTagItem("openrag.message.outcome"));
        Assert.Equal("corr-safe", activity.GetTagItem("openrag.correlation_id"));
        Assert.Equal(TenantId.ToString("D"), activity.GetTagItem("openrag.tenant_id"));
        Assert.NotNull(activity.GetTagItem("openrag.duration_ms"));
        Assert.DoesNotContain(
            activity.TagObjects,
            tag => string.Equals(tag.Value?.ToString(), message.SensitiveValue, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Telemetry_marks_failure_and_rethrows_same_exception()
    {
        using var listener = CreateActivityListener();
        var behavior = new TelemetryBehavior<TestCommand, string>();
        var expected = new InvalidOperationException("provider failed");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => behavior.Handle(
                new TestCommand("corr", "sensitive-question"),
                (_, _) => ValueTask.FromException<string>(expected),
                CancellationToken.None).AsTask());

        Assert.Same(expected, actual);
        var activity = Assert.Single(listener.StoppedActivities);
        Assert.Equal("error", activity.GetTagItem("openrag.message.outcome"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task Telemetry_distinguishes_cancellation_and_preserves_exception()
    {
        using var listener = CreateActivityListener();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var behavior = new TelemetryBehavior<TestCommand, string>();
        var expected = new OperationCanceledException(cancellation.Token);

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => behavior.Handle(
                new TestCommand("corr", "sensitive-question"),
                (_, _) => ValueTask.FromException<string>(expected),
                cancellation.Token).AsTask());

        Assert.Same(expected, actual);
        Assert.Equal(
            "cancelled",
            Assert.Single(listener.StoppedActivities).GetTagItem("openrag.message.outcome"));
    }

    [Fact]
    public async Task Logging_scope_contains_only_safe_message_metadata()
    {
        var logger = new ScopeCapturingLogger<LoggingScopeBehavior<AuthenticatedCorrelatedCommand, string>>();
        var behavior = new LoggingScopeBehavior<AuthenticatedCorrelatedCommand, string>(logger);
        var message = new AuthenticatedCorrelatedCommand("corr-auth", "sensitive-question");

        await behavior.Handle(
            message,
            (_, _) => ValueTask.FromResult("handled"),
            CancellationToken.None);

        var scope = Assert.Single(logger.Scopes);
        Assert.Equal(nameof(AuthenticatedCorrelatedCommand), scope["MessageType"]);
        Assert.Equal("command", scope["MessageCategory"]);
        Assert.Equal("corr-auth", scope["CorrelationId"]);
        Assert.DoesNotContain("UserId", scope.Keys);
        Assert.DoesNotContain("TenantId", scope.Keys);
        Assert.DoesNotContain(message.SensitiveValue, scope.Values);
    }

    [Fact]
    public async Task Logging_scope_leaves_worker_tenant_to_explicit_tenant_guard()
    {
        var logger = new ScopeCapturingLogger<LoggingScopeBehavior<WorkerTestCommand, string>>();
        var behavior = new LoggingScopeBehavior<WorkerTestCommand, string>(logger);
        var message = new WorkerTestCommand(TenantId, "corr-worker", "sensitive-content");

        await behavior.Handle(
            message,
            (_, _) => ValueTask.FromResult("handled"),
            CancellationToken.None);

        var scope = Assert.Single(logger.Scopes);
        Assert.Equal("corr-worker", scope["CorrelationId"]);
        Assert.DoesNotContain("TenantId", scope.Keys);
        Assert.DoesNotContain(message.SensitiveValue, scope.Values);
    }

    [Theory]
    [InlineData(OpenRagPipelineHost.Api, typeof(AuthenticatedContextBehavior<,>))]
    [InlineData(OpenRagPipelineHost.Worker, typeof(ExplicitTenantMessageBehavior<,>))]
    public void Pipeline_registration_has_deterministic_host_specific_order(
        OpenRagPipelineHost host,
        Type contextBehavior)
    {
        var services = new ServiceCollection();

        services.AddOpenRagMediatorPipelines(host);

        var behaviorTypes = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();
        var expected = host == OpenRagPipelineHost.Api
            ? new Type[]
            {
                typeof(TelemetryBehavior<,>),
                contextBehavior,
                typeof(LoggingScopeBehavior<,>),
                typeof(ValidationBehavior<,>)
            }
            :
            [
                typeof(TelemetryBehavior<,>),
                typeof(LoggingScopeBehavior<,>),
                contextBehavior,
                typeof(ValidationBehavior<,>)
            ];

        Assert.Equal(expected, behaviorTypes);
        Assert.All(
            services.Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>)),
            descriptor => Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));
    }

    [Fact]
    public async Task Mediator_wraps_behaviors_in_registration_order()
    {
        var order = new List<string>();
        var services = new ServiceCollection();
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddScoped<IRequestHandler<GenerateEmbeddingsCommand, GenerateEmbeddingsResponse>>(
            _ => new RecordingHandler(order));
        services.AddScoped<IPipelineBehavior<GenerateEmbeddingsCommand, GenerateEmbeddingsResponse>>(
            _ => new RecordingBehavior("telemetry", order));
        services.AddScoped<IPipelineBehavior<GenerateEmbeddingsCommand, GenerateEmbeddingsResponse>>(
            _ => new RecordingBehavior("context", order));
        services.AddScoped<IPipelineBehavior<GenerateEmbeddingsCommand, GenerateEmbeddingsResponse>>(
            _ => new RecordingBehavior("logging", order));
        services.AddScoped<IPipelineBehavior<GenerateEmbeddingsCommand, GenerateEmbeddingsResponse>>(
            _ => new RecordingBehavior("validation", order));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(
            new GenerateEmbeddingsCommand(
                TenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "corr"));

        Assert.Equal(
        [
            "telemetry-before",
            "context-before",
            "logging-before",
            "validation-before",
            "handler",
            "validation-after",
            "logging-after",
            "context-after",
            "telemetry-after"
        ],
            order);
    }

    [Fact]
    public void Api_and_worker_resolve_only_their_applicable_context_guards()
    {
        var apiServices = new ServiceCollection();
        apiServices.AddLogging();
        apiServices.AddSingleton<ICurrentUser>(new StubCurrentUser(UserId, true));
        apiServices.AddSingleton<ICurrentTenant>(new StubCurrentTenant(TenantId));
        apiServices.AddOpenRagMediatorPipelines(OpenRagPipelineHost.Api);
        using var apiProvider = apiServices.BuildServiceProvider();

        var apiAuthPipeline = apiProvider
            .GetServices<IPipelineBehavior<GetDocumentDetailQuery, GetDocumentDetailResponse>>()
            .ToArray();
        var apiWorkerPipeline = apiProvider
            .GetServices<IPipelineBehavior<GenerateEmbeddingsCommand, GenerateEmbeddingsResponse>>()
            .ToArray();

        Assert.Contains(apiAuthPipeline, behavior => behavior.GetType().Name.StartsWith("AuthenticatedContextBehavior", StringComparison.Ordinal));
        Assert.DoesNotContain(apiWorkerPipeline, behavior => behavior.GetType().Name.StartsWith("AuthenticatedContextBehavior", StringComparison.Ordinal));
        Assert.DoesNotContain(apiWorkerPipeline, behavior => behavior.GetType().Name.StartsWith("ExplicitTenantMessageBehavior", StringComparison.Ordinal));

        var workerServices = new ServiceCollection();
        workerServices.AddLogging();
        workerServices.AddOpenRagMediatorPipelines(OpenRagPipelineHost.Worker);
        using var workerProvider = workerServices.BuildServiceProvider();

        var workerPipeline = workerProvider
            .GetServices<IPipelineBehavior<GenerateEmbeddingsCommand, GenerateEmbeddingsResponse>>()
            .ToArray();
        var workerAuthPipeline = workerProvider
            .GetServices<IPipelineBehavior<GetDocumentDetailQuery, GetDocumentDetailResponse>>()
            .ToArray();

        Assert.Contains(workerPipeline, behavior => behavior.GetType().Name.StartsWith("ExplicitTenantMessageBehavior", StringComparison.Ordinal));
        Assert.DoesNotContain(workerAuthPipeline, behavior => behavior.GetType().Name.StartsWith("ExplicitTenantMessageBehavior", StringComparison.Ordinal));
        Assert.Null(workerProvider.GetService<ICurrentUser>());
        Assert.Null(workerProvider.GetService<ICurrentTenant>());
    }

    private static CapturingActivityListener CreateActivityListener()
    {
        var listener = new CapturingActivityListener();
        ActivitySource.AddActivityListener(listener.Listener);
        return listener;
    }

    private sealed record TestCommand(string CorrelationId, string SensitiveValue)
        : IOpenRagCommand<string>, ICorrelatedMessage;

    private sealed record AuthenticatedTestCommand
        : IOpenRagCommand<string>, IAuthenticatedApplicationMessage;

    private sealed record AuthenticatedCorrelatedCommand(
        string CorrelationId,
        string SensitiveValue)
        : IOpenRagCommand<string>,
            IAuthenticatedApplicationMessage,
            ICorrelatedMessage;

    private sealed record WorkerTestCommand(
        Guid TenantId,
        string CorrelationId,
        string SensitiveValue)
        : IOpenRagCommand<string>, IExplicitTenantMessage, ICorrelatedMessage;

    private sealed class StubCurrentUser : ICurrentUser
    {
        public StubCurrentUser(Guid userId, bool isAuthenticated)
        {
            UserId = userId;
            IsAuthenticated = isAuthenticated;
        }

        public Guid UserId { get; }
        public bool IsAuthenticated { get; }
    }

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public StubCurrentTenant(Guid tenantId) => TenantId = tenantId;
        public Guid TenantId { get; }
    }

    private sealed class RecordingValidator<TMessage> : IMessageValidator<TMessage>
    {
        private readonly string _name;
        private readonly ICollection<string> _order;
        private readonly Exception? _exception;

        public RecordingValidator(
            string name,
            ICollection<string> order,
            Exception? exception = null)
        {
            _name = name;
            _order = order;
            _exception = exception;
        }

        public int CallCount { get; private set; }

        public ValueTask ValidateAsync(TMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            _order.Add(_name);

            if (_exception is not null)
                return ValueTask.FromException(_exception);

            return ValueTask.CompletedTask;
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
                ShouldListenTo = source => source.Name == OpenRagMediatorTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => StoppedActivities.Add(activity)
            };
        }

        public ActivityListener Listener { get; }
        public List<Activity> StoppedActivities { get; } = [];

        public void Dispose() => Listener.Dispose();
    }

    private sealed class RecordingBehavior
        : IPipelineBehavior<GenerateEmbeddingsCommand, GenerateEmbeddingsResponse>
    {
        private readonly string _name;
        private readonly ICollection<string> _order;

        public RecordingBehavior(string name, ICollection<string> order)
        {
            _name = name;
            _order = order;
        }

        public async ValueTask<GenerateEmbeddingsResponse> Handle(
            GenerateEmbeddingsCommand message,
            MessageHandlerDelegate<GenerateEmbeddingsCommand, GenerateEmbeddingsResponse> next,
            CancellationToken cancellationToken)
        {
            _order.Add($"{_name}-before");
            var response = await next(message, cancellationToken);
            _order.Add($"{_name}-after");
            return response;
        }
    }

    private sealed class RecordingHandler
        : IRequestHandler<GenerateEmbeddingsCommand, GenerateEmbeddingsResponse>
    {
        private readonly ICollection<string> _order;

        public RecordingHandler(ICollection<string> order) => _order = order;

        public ValueTask<GenerateEmbeddingsResponse> Handle(
            GenerateEmbeddingsCommand request,
            CancellationToken cancellationToken)
        {
            _order.Add("handler");
            return ValueTask.FromResult(
                new GenerateEmbeddingsResponse(
                    request.DocumentId,
                    request.VersionId,
                    0,
                    "test",
                    0,
                    "Handled"));
        }
    }
}
