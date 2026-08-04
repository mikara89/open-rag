# 19 — Live Cross-Tenant Security Tests

## Status and purpose

P0.5 verifies the P0.4–P0.4.2 tenant boundary against disposable live infrastructure. It does not redesign authorization and does not make OpenRAG production-ready.

The suite's principal assertion is that Tenant B cannot read, retrieve, mutate, reprocess, delete, prompt with, or otherwise observe Tenant A data. Missing and foreign resources remain indistinguishable: 404 `application/problem+json`, `resource.not_found`, and the same safe response structure after normalizing `traceId`.

## Fixture architecture

`tests/OpenRAG.LiveIntegrationTests` uses one xUnit collection fixture with parallel execution disabled. The fixture starts `pgvector/pgvector:0.8.2-pg17-bookworm` through `Testcontainers.PostgreSql` 4.13.0, creates a unique database, verifies PostgreSQL 17 and pgvector 0.8.2, and applies every production EF Core migration with `MigrateAsync`. `EnsureCreated`, SQLite, EF InMemory, fake repositories, and mocked vector execution are forbidden by architecture guards.

The API runs through `WebApplicationFactory` with real JWT validation, policies, tenant resolution, Mediator pipelines, Result-to-HTTP mapping, EF repositories, unit of work, pgvector service, object-key policy, and `LocalFileStorage`. The Worker provider uses the production Worker registration extension, actual CAP consumer classes, the Worker Mediator pipeline, explicit message tenants, and the same real persistence and storage implementations.

Only external nondeterministic boundaries are substituted: document preprocessing, embeddings, chat completion, document intelligence, and CAP publication capture. Their deterministic responses use three-dimensional vectors and tenant-specific test markers. No external AI, Docling, hosted storage, RabbitMQ, or secrets are required.

## Isolation and reset

Each scenario uses recognizable fixed tenant/user identities and unique document, version, chunk, embedding, intelligence, run, and step IDs. Tenant A and Tenant B content have distinct test-only markers. Between tests, the fixture truncates the seven OpenRAG application tables with identity restart and cascade, clears provider/event captures, and clears its unique `artifacts/live-tests/<run-id>/storage` directory. Migrations, schema, and extensions remain intact.

The real filesystem provider creates canonical source, Markdown, and JSON keys. Tests cover own-tenant reads plus foreign keys, wrong document/version/tenant components, traversal, absolute paths, backslashes, and invalid suffixes. Rejected operations compare a safe manifest containing only relative path, length, and SHA-256.

## Automated matrix

The suite covers:

- empty-database migration, model compatibility, extension/version checks, and tenant-inclusive SQLSTATE 23503 constraints;
- every exposed real repository read, list, count, authorization lookup, update/delete-by-version path, and positive control;
- authenticated document list/detail/status/artifact/chunk/intelligence/reprocess/delete endpoints, missing/foreign equivalence, state conflicts, and mutation side effects;
- JWT rejection and header/query/route/body tenant-spoofing attempts;
- live pgvector tenant/document/compatibility/deleted predicates, relationship identity, ranking, TopK, and concurrent tenant searches;
- RAG unfiltered and authorized retrieval, fail-closed foreign/mixed/missing filters, captured prompt isolation, citations, and a controlled invalid-vector invariant;
- actual Worker consumers for preprocess, chunk, intelligence, and embeddings, including foreign no-ops, a positive pipeline, database/cancellation exceptions, terminal provider/storage failures, and event-publication rollback;
- bounded concurrent reprocess/delete, delete/artifact-read, and A/B vector-search cases without timing sleeps.

Worker tests stop at the actual consumer-to-Mediator-to-handler boundary. A full CAP transport round trip is intentionally omitted because the separated API/Worker topology uses RabbitMQ and P0.5 does not add a broker solely for testing. CAP publication is exercised through a deterministic capture/failure boundary; message tenant propagation and consumer composition are still covered.

## Running locally

Prerequisites are Docker, enough disk space for the pinned image, and the repository's .NET 10 SDK. No external services are required.

```powershell
pwsh ./scripts/test-live-cross-tenant.ps1
```

Equivalent direct command:

```bash
dotnet test tests/OpenRAG.LiveIntegrationTests/OpenRAG.LiveIntegrationTests.csproj --configuration Release --logger trx --results-directory artifacts/live-test-results --collect "XPlat Code Coverage"
```

## CI and diagnostics

The `live-cross-tenant-tests` GitHub Actions job runs on pull requests to `main` and pushes to `main`. It verifies Docker, restores, runs both dependency-audit gates, builds Release, runs the dedicated project, and uploads TRX and Cobertura artifacts. On failure it uploads safe container state, image metadata, migration/version and row-count diagnostics, and the storage manifest. Diagnostics exclude JWTs, passwords, document contents, prompts, and object contents.

## Limitations and remaining work

The suite validates code-level tenant isolation with PostgreSQL/pgvector, local filesystem storage, API, RAG, and Worker composition. Local filesystem storage is not production object storage. Full broker transport, deployment, backup/restore, operational hardening, broader penetration/security testing, memberships, sharing, and per-user ACLs remain future work.

Code-level tenant isolation is now covered by disposable live PostgreSQL/pgvector, filesystem-storage, API, RAG, and Worker tests. Remaining production-readiness work still includes production object storage, deployment, backup/restore, operational hardening, and broader security testing.
