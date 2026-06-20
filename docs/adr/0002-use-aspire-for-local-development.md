# ADR 0002 — Use Aspire for Local Development

## Status

Accepted

## Context

The system is distributed even during development:

```text
API
Worker
PostgreSQL
RabbitMQ
object storage
Docling Serve
optional local AI endpoint
```

Without orchestration, developers would need several terminals, custom scripts, and manual connection string management.

## Decision

Use Aspire AppHost as the default local development orchestrator.

## Consequences

Positive:

```text
One place to model the local stack.
One command to run the distributed app.
Built-in dashboard and observability.
Consistent service discovery and connection configuration.
Good fit for API + Worker + database + broker + containers.
```

Negative:

```text
Adds Aspire-specific project and local tooling.
Production deployment still needs its own deployment model.
Some resources may require generic containers or custom Aspire hosting integration.
```

## Important local development rule

Use RabbitMQ when API and Worker are separate processes.

Use CAP in-memory transport only when API and consumers run in the same process.
