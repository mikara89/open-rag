# ADR 0006 — Use Disposable PostgreSQL/pgvector Containers for Live Integration Tests

## Status

Accepted

## Context

Unit tests, handler tests, model inspection, and generated SQL review cannot prove that production migrations apply, PostgreSQL composite constraints reject cross-tenant relationships, or pgvector executes the intended predicates and distance ranking. SQLite differs from PostgreSQL, and EF InMemory does not enforce the relational and extension behavior at this trust boundary.

## Decision

Use `Testcontainers.PostgreSql` with the explicitly pinned `pgvector/pgvector:0.8.2-pg17-bookworm` image in a dedicated `OpenRAG.LiveIntegrationTests` project. Apply production migrations to an empty unique database, use the real EF repositories, unit of work, pgvector service, authenticated API, Worker consumer/application composition, object-key policy, and local filesystem provider.

Substitute only nondeterministic external AI/document-processing boundaries and capture CAP publications without adding RabbitMQ. Reset application tables and storage between tests while preserving migrations, schema, and extensions. Keep Testcontainers out of production projects and upload only safe diagnostics.

## Consequences

The live suite requires Docker and takes longer than dependency-free tests, so it has a dedicated CI job and local script. In return it validates actual PostgreSQL/pgvector behavior and catches migration, schema, SQL, and composition failures that test doubles cannot expose. The suite does not validate production object storage, a full broker round trip, deployment infrastructure, or operations.
