# ADR 0003 — Use DotNetCore.CAP with PostgreSQL Storage

## Status

Accepted

## Context

The document processing workflow is asynchronous and multi-step. The system needs durable message state and reliable event publishing around database changes.

## Decision

Use DotNetCore.CAP as the event bus abstraction implementation.

Use PostgreSQL for CAP storage in all environments, including development.

Use transport depending on mode:

```text
Single-process dev: CAP in-memory transport is acceptable.
API + Worker dev: RabbitMQ.
Production: RabbitMQ, Kafka, Azure Service Bus, or another supported broker.
```

## Consequences

Positive:

```text
Message state is durable.
Development behavior is closer to production when PostgreSQL storage is always used.
CAP helps implement outbox-style publishing.
Transport can be swapped later.
```

Negative:

```text
Requires care with transaction boundaries.
Requires idempotent consumers.
In-memory transport can mislead developers if used with separate Worker process.
```

## Required rules

```text
Publish events in the same transaction as database state changes.
Do not put large document content into messages.
Every consumer must be idempotent.
Every event must include TenantId, DocumentId, VersionId, ProcessingRunId, and CorrelationId.
```
