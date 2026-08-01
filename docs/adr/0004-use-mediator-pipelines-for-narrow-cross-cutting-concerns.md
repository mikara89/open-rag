# ADR 0004 — Use Mediator Pipelines for Narrow Cross-Cutting Concerns

## Status

Accepted

## Context

OpenRAG dispatches API commands and queries and Worker processing commands through Mediator 3.0.2. Handlers depend on scoped application and persistence services, while several safe cross-cutting concerns were repeated or lacked a single reusable execution boundary: primitive message-shape validation, correlation scopes, application activities, and host-context guards.

The existing P0.4 authorization design intentionally keeps resource decisions visible in use-case handlers and tenant-scoped repositories. A generic pipeline must not become an implicit authorization, persistence, or error-mapping layer.

## Decision

Classify every application request explicitly as an OpenRAG command or query. HTTP-originated messages additionally implement `IAuthenticatedApplicationMessage`; Worker processing commands implement `IExplicitTenantMessage` and continue to carry `TenantId` explicitly. Correlated messages implement `ICorrelatedMessage`.

Register scoped Mediator behaviors explicitly in this wrapping order:

```text
1. Telemetry
2. Structured logging scope
3. Authenticated API context or explicit Worker tenant guard
4. Primitive message validation
5. Handler
```

The reverse order applies while the stack unwinds. A test using the repository's actual Mediator version proves this behavior rather than assuming container ordering.

The API context behavior uses `ICurrentUser` and `ICurrentTenant`. It is defense in depth behind endpoint authentication and authorization policies and does not inspect claims. The Worker behavior uses only the immutable tenant carried by its message, never an HTTP or ambient tenant context.

Use small `IMessageValidator<TMessage>` implementations for dependency-free primitive shape rules. Validators execute in registration order, stop at the first failure, propagate cancellation, and throw the existing `RequestValidationException`. They do not query resource state.

Use `ActivitySource` name `OpenRAG.Application.Mediator`. Activities contain only message name/category, outcome, correlation ID, duration, and an explicit Worker tenant when present. Message bodies and sensitive fields are never serialized into spans or scopes.

Retain scoped Mediator lifetime because handlers and behaviors consume scoped context and persistence services.

## Deliberately excluded

Pipelines do not implement or hide:

```text
resource or document authorization
tenant-scoped repository predicates
nested-resource relationship checks
storage object-key ownership
vector filtering or RAG result validation
database or CAP transactions
CAP inbox/idempotency or retries
caching or rate limiting
HTTP exception conversion or Problem Details
```

Resource authorization remains explicit in each use case because it depends on the requested resource, nested identity, operation, and fail-closed behavior. Generic transaction behavior was rejected because transaction and CAP outbox boundaries differ by workflow. HTTP exception mapping remains at the API boundary because Application and Worker must not acquire ASP.NET Core semantics.

## Consequences

Positive:

```text
Every request has an explicit category and execution-context contract.
Primitive validation and cancellation behavior are reusable and deterministic.
API and Worker context rules remain separate.
Safe application timing and structured scopes are consistent.
Authorization and persistence boundaries remain reviewable in handlers.
```

Negative:

```text
Primitive handler guards are temporarily duplicated for direct-invocation defense in depth.
Adding a new message requires explicit classification and usually a validator registration.
Host composition must preserve and test behavior order.
```

## Rollback

Remove the host pipeline registrations, message validator registrations, and OpenRAG marker interfaces, then restore each message to `IRequest<TResponse>`. No database or persisted-message migration is required. Handler-level security and primitive guards remain available throughout rollback.
