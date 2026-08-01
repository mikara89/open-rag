# 12 — Production Readiness Roadmap

## Current baseline

The API-first MVP supports document upload, asynchronous preprocessing, chunking, optional document intelligence, pgvector-backed embedding search, RAG question answering, inspection endpoints, reprocessing, and deletion. Mock providers keep automated build and test validation independent of Docling, RabbitMQ, PostgreSQL, and external AI providers.

This is a production-oriented development baseline, not a production deployment. No UI or deployment topology is included in the MVP.

## Priority gaps

### Runtime and deployment

- Add a live smoke-test environment with disposable PostgreSQL/pgvector and RabbitMQ services. Keep the default build/test job dependency-free.
- Add Docker Compose or deployment manifests with explicit health checks, persistent volumes, configuration injection, and upgrade procedures.
- Add a deployment pipeline with environment promotion, migration gates, rollback guidance, and post-deployment verification.
- Implement an S3-compatible object storage provider behind `IFileStorage`; local filesystem storage remains development-only.

### Identity, authorization, and tenancy

- Add authentication and endpoint authorization.
- Derive tenant identity from trusted credentials instead of request-supplied values.
- Harden tenant isolation in every query, event, cache key, storage key, and operational tool.
- Add cross-tenant negative tests and authorization audit coverage.

### Reliability and operations

- Define backup and restore procedures for PostgreSQL, pgvector data, CAP state, and object storage; test recovery regularly.
- Add end-to-end observability for API requests, CAP messages, processing runs, provider calls, and retrieval quality.
- Add rate limiting, upload quotas, provider timeouts, retry budgets, and back-pressure controls.
- Document poison-message handling, replay procedures, retention, and orphaned artifact cleanup.

### CI quality and security

- Publish test results and code coverage, then set an agreed coverage policy for critical application paths.
- Add static application security testing and secret scanning.
- Add dependency vulnerability scanning and an update policy; fail CI according to an agreed severity threshold.
- Add container and deployment-manifest scanning once deployable artifacts exist.
- Add workflow linting and Markdown linting if their maintenance cost remains low.

## Suggested delivery order

1. Resolve known dependency advisories and establish coverage/security reporting.
2. Add authentication, authorization, and tenant-isolation hardening.
3. Add S3-compatible storage plus tested backup/restore procedures.
4. Create disposable integration infrastructure and a live smoke-test CI job.
5. Add deployment manifests and a gated deployment pipeline.
6. Exercise failure recovery, scaling, observability, and security acceptance tests before production use.

Each item should ship with documentation, automated validation where practical, and explicit rollback or recovery notes.
