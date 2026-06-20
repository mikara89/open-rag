# ADR 0001 — Use Clean/Onion Architecture with Vertical Slices

## Status

Accepted

## Context

The platform must integrate with multiple replaceable technologies:

```text
S3-compatible storage
Docling
CAP
RabbitMQ/Kafka/Azure Service Bus
PostgreSQL
pgvector/vector DB
OpenAI-compatible AI providers
optional graph DB
```

The core document workflow should remain stable even when infrastructure choices change.

## Decision

Use Clean/Onion Architecture at project boundaries and vertical slice organization inside the Application layer.

## Consequences

Positive:

```text
Infrastructure can be replaced behind interfaces.
Business workflows stay testable.
API and Worker stay thin.
Tenant/security rules can be centralized.
```

Negative:

```text
More project structure than a simple CRUD app.
Risk of over-abstraction if every small detail becomes an interface.
```

## Rules

```text
Domain has no external dependencies.
Application defines use cases and abstractions.
Infrastructure implements abstractions.
API and Worker are composition roots.
Application is organized by feature/use case.
```
