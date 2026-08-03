# ADR 0005 — Use a Hybrid Result Model for Expected API Outcomes

## Status

Accepted

## Context

Authenticated HTTP use cases previously represented ordinary validation, missing-resource, and resource-state outcomes with exceptions. That made expected rejections indistinguishable from unexpected failures inside application telemetry and relied on exception control flow for routine API behavior. Worker processing has a different contract: an exception may be required for CAP retry and acknowledgement semantics.

## Decision

Use this rule:

```text
Expected HTTP application outcome → Result<T>
Unexpected, technical, cancellation, or isolation failure → exception
```

The Application layer owns a minimal `Result<T>` with immutable errors, a success value, failure factories, a primary error, and `Match`. It has no HTTP status codes or ASP.NET Core dependency, and no external Result package is added. A successful result has one non-null value and no errors. A failed result has at least one defensively copied error and exposes no value.

Application errors have a stable code, a client-safe message, a semantic type (`Validation`, `NotFound`, or `Conflict`), and an optional field target. They never contain tenant IDs, ownership facts, storage keys, SQL, stack traces, document content, prompts, or vectors. The generic missing-resource representation is always:

```text
resource.not_found
The requested resource was not found.
```

Missing, foreign-tenant, and invalid nested relationships therefore remain externally indistinguishable.

The 12 authenticated API commands and queries implement `IResultApplicationMessage` and return `Result<TResponse>`. Provider diagnostics remains exception-based because it has no expected domain rejection in this phase. The four Worker processing commands remain ordinary responses and do not implement the Result marker.

Validation now returns structured errors. API validation aggregates validators in deterministic registration order and returns a failed Result without invoking the handler. Worker validation uses the same structured validator output but throws `RequestValidationException`; CAP consumers therefore cannot mistake validation failure for successful processing or acknowledge a retryable failure. Cancellation always throws.

The pipeline orders are:

```text
API:    telemetry → authenticated context → logging scope → Result validation → handler
Worker: telemetry → logging scope → explicit tenant guard → throwing validation → handler
```

The API alone maps Result failures to Problem Details: validation to 400, missing resources to 404, and conflicts to 409. Each endpoint still selects its own success result, including 201, 202, 204, text, and JSON content. The Result wrapper is never an HTTP schema or response body. Authentication middleware continues to own 401 and authorization policies continue to own 403.

`OpenRagExceptionHandler` remains for isolation violations, unexpected/technical failures, cancellation pass-through, and remaining exception-based boundaries. `IsolationViolationException` deliberately remains an exception and maps to a generic public 500; converting it to 404 or validation would hide a fail-closed security defect as a routine rejection.

Telemetry treats successful Results as `success`, expected failed Results as `rejected`, exceptions as `error`, and cancellation as `cancelled`. Rejections record only the primary stable error code and semantic type; they are not marked as unexpected exceptions.

## Worker and CAP safety

Worker commands and CAP consumers are intentionally not migrated. Existing idempotent no-op and terminal persisted-failure responses remain unchanged. Infrastructure, provider, database, transaction, CAP publication, unexpected, and cancellation failures continue to throw. This prevents:

```text
processing fails → Result.Failure returned → CAP method completes → message acknowledged
```

## Consequences

Expected API control flow is explicit and testable without exception handling. Problem Details gains stable machine-readable application codes while retaining prior status, type, title, trace ID, and safe detail semantics. Application and Worker validation share primitive rules but preserve different host semantics.

The Result model is deliberately small. It is not a general functional-programming library, transaction abstraction, authorization system, or exception-conversion mechanism. Technical and isolation failures remain visible to retries, logs, and centralized exception handling.

## Rollback

Restore the 12 message and handler response types, replace API Result validation with throwing validation, restore expected exception mappings, and remove the API Result mapper. No package, database, event schema, CAP message, or migration rollback is required. Worker processing is unaffected throughout rollback.
