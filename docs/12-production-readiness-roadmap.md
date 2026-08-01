# 12 — Production Readiness Roadmap

## Current baseline

The API-first MVP supports document upload, asynchronous preprocessing, chunking, optional document intelligence, pgvector-backed embedding search, RAG question answering, inspection endpoints, reprocessing, and deletion. Mock providers keep automated build and test validation independent of Docling, RabbitMQ, PostgreSQL, and external AI providers.

This is a production-oriented development baseline, not a production deployment. No UI or deployment topology is included in the MVP.

## P0 — Trust boundary and repository controls

### P0.1 — Trusted CI and PR governance

- **Objective:** Prove dependency-free GitHub CI on a pull request and establish the repository files and documented controls needed for PR-based development.
- **Acceptance criteria:** A feature-branch PR runs green `Build, test, and format` and `Documentation checks` jobs; test-result and coverage artifacts are downloadable; CODEOWNERS, issue forms, the PR template, security policy, and governance guidance are present; recommended `main` protection is enabled and verified after explicit maintainer authorization.
- **Dependencies:** Existing build/test/format scripts and GitHub Actions access. Repository-setting changes require separate maintainer approval.
- **Status:** Complete

### P0.2 — Authentication foundation

- **Objective:** Establish a supported authentication mechanism and trusted principal model for API requests.
- **Acceptance criteria:** Authentication is required on protected endpoints; invalid or missing credentials are rejected; identity configuration and key rotation are documented and tested.
- **Dependencies:** P0.1 trusted CI and an approved identity-provider design.
- **Status:** Complete — JWT Bearer validation and policy enforcement protect every `/api` route; current-user mapping, administrator authorization, startup validation, OpenAPI metadata, and negative token cases are covered. The final P0.2 validation run passed all 367 tests, dependency auditing, format checks, and documentation checks.

### P0.3 — Trusted tenant resolution

- **Objective:** Resolve tenant identity only from authenticated, trusted claims or server-side mappings.
- **Acceptance criteria:** Request-supplied tenant identifiers cannot override the authenticated tenant; background events preserve trusted tenant context; negative tests cover spoofing attempts.
- **Dependencies:** P0.2 authentication foundation.
- **Status:** Complete — HTTP tenant identity is resolved only from exactly one validated non-empty GUID JWT claim; request spoofing cannot override it; the development fallback is removed; CAP events and Worker commands preserve tenant context explicitly. The final P0.3 validation run passed all 403 tests, dependency auditing, format checks, and documentation checks.

> Trusted tenant resolution and P0.4 code-level authorization/isolation are complete. The later P0.4.1 architectural improvement is also complete. P0.5 live adversarial infrastructure remains planned.

### P0.4 — Authorization and retrieval isolation

- **Objective:** Enforce authorization and tenant isolation across document lifecycle, storage, processing, vector retrieval, and RAG responses.
- **Acceptance criteria:** Every protected operation applies policy and tenant filters; storage and vector queries cannot cross tenants; denial behavior is consistent and audited.
- **Dependencies:** P0.2 authentication foundation and P0.3 trusted tenant resolution.
- **Status:** Complete — Tenant-level authorization is enforced through explicit repository contracts, complete nested-resource validation, canonical object-key ownership, tenant-inclusive database relationships, parameterized pgvector retrieval, RAG filter preauthorization, and fail-closed result validation before LLM calls. Typed Problem Details and focused unit, architecture, model, and dependency-free integration tests cover denial behavior. P0.5 live infrastructure remains separate.

### P0.4.1 — Mediator pipeline foundation

- **Objective:** Add narrow, reusable message validation, trusted execution-context guards, structured logging scopes, and application telemetry as a later architectural improvement without hiding completed P0.4 resource authorization or persistence isolation.
- **Acceptance criteria:** Every request is classified; API and Worker context contracts remain separate; validators short-circuit deterministically; safe telemetry and scope metadata are tested; host composition and actual Mediator wrapping order are proven.
- **Dependencies:** Completed P0.4 authorization/isolation foundation and existing scoped Mediator 3.0.2 composition.
- **Status:** Complete — 17 requests have explicit command/query categories and 16 primitive validators. The API order is telemetry → authenticated context → logging → validation → handler; invalid production context accessors are normalized before logging. The Worker order remains telemetry → logging → explicit tenant → validation → handler. No resource authorization, transactions, CAP behavior, or HTTP exception mapping moved into pipelines.

### P0.5 — Cross-tenant security integration tests

- **Objective:** Prove that one tenant cannot read, mutate, retrieve, reprocess, or delete another tenant's data.
- **Acceptance criteria:** Automated integration tests exercise API, persistence, vector retrieval, background processing, and storage boundaries with positive and negative tenant cases.
- **Dependencies:** P0.2 through P0.4 plus disposable integration-test infrastructure.
- **Status:** Planned

## Priority gaps

### Runtime and deployment

- Add a live smoke-test environment with disposable PostgreSQL/pgvector and RabbitMQ services. Keep the default build/test job dependency-free.
- Add Docker Compose or deployment manifests with explicit health checks, persistent volumes, configuration injection, and upgrade procedures.
- Add a deployment pipeline with environment promotion, migration gates, rollback guidance, and post-deployment verification.
- Implement an S3-compatible object storage provider behind `IFileStorage`; local filesystem storage remains development-only.

### Identity, authorization, and tenancy

- Prove the completed P0.4 resource controls through P0.5 disposable live PostgreSQL/pgvector, object-storage, API, and Worker infrastructure.
- Design users, tenant membership, sharing, and per-user document ACLs before expanding authorization below the tenant boundary.

### Reliability and operations

- Define backup and restore procedures for PostgreSQL, pgvector data, CAP state, and object storage; test recovery regularly.
- Add end-to-end observability for API requests, CAP messages, processing runs, provider calls, and retrieval quality.
- Add rate limiting, upload quotas, provider timeouts, retry budgets, and back-pressure controls.
- Document poison-message handling, replay procedures, retention, and orphaned artifact cleanup.

### CI quality and security

- The `Microsoft.OpenApi` High-severity advisory `GHSA-v5pm-xwqc-g5wc` (`CVE-2026-49451`) was remediated by pinning the owning API project to `Microsoft.OpenApi` 2.11.0. NuGet vulnerability auditing, including transitive packages, now runs locally and in CI; it fails closed when audit data is unavailable and blocks High and Critical findings.
- Surface coverage summaries in PR review and set an agreed coverage policy for critical application paths; raw Cobertura output is already retained as a CI artifact.
- Add static application security testing and secret scanning.
- Define a dependency-update policy and decide whether Moderate findings should become blocking; the current NuGet audit reports Low and Moderate findings but blocks High and Critical findings.
- Add container and deployment-manifest scanning once deployable artifacts exist.
- Add workflow linting and Markdown linting if their maintenance cost remains low.

## Suggested delivery order

1. Resolve known dependency advisories and establish a coverage policy plus security scanning.
2. Complete resource authorization and tenant-isolation hardening on the trusted-tenant foundation.
3. Add S3-compatible storage plus tested backup/restore procedures.
4. Create disposable integration infrastructure and a live smoke-test CI job.
5. Add deployment manifests and a gated deployment pipeline.
6. Exercise failure recovery, scaling, observability, and security acceptance tests before production use.

Each item should ship with documentation, automated validation where practical, and explicit rollback or recovery notes.
