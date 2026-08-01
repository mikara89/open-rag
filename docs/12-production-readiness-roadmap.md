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
- **Status:** Planned

### P0.3 — Trusted tenant resolution

- **Objective:** Resolve tenant identity only from authenticated, trusted claims or server-side mappings.
- **Acceptance criteria:** Request-supplied tenant identifiers cannot override the authenticated tenant; background events preserve trusted tenant context; negative tests cover spoofing attempts.
- **Dependencies:** P0.2 authentication foundation.
- **Status:** Planned

### P0.4 — Authorization and retrieval isolation

- **Objective:** Enforce authorization and tenant isolation across document lifecycle, storage, processing, vector retrieval, and RAG responses.
- **Acceptance criteria:** Every protected operation applies policy and tenant filters; storage and vector queries cannot cross tenants; denial behavior is consistent and audited.
- **Dependencies:** P0.2 authentication foundation and P0.3 trusted tenant resolution.
- **Status:** Planned

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

- The `Microsoft.OpenApi` High-severity advisory `GHSA-v5pm-xwqc-g5wc` (`CVE-2026-49451`) was remediated by pinning the owning API project to `Microsoft.OpenApi` 2.11.0. NuGet vulnerability auditing, including transitive packages, now runs locally and in CI; it fails closed when audit data is unavailable and blocks High and Critical findings.
- Surface coverage summaries in PR review and set an agreed coverage policy for critical application paths; raw Cobertura output is already retained as a CI artifact.
- Add static application security testing and secret scanning.
- Define a dependency-update policy and decide whether Moderate findings should become blocking; the current NuGet audit reports Low and Moderate findings but blocks High and Critical findings.
- Add container and deployment-manifest scanning once deployable artifacts exist.
- Add workflow linting and Markdown linting if their maintenance cost remains low.

## Suggested delivery order

1. Resolve known dependency advisories and establish a coverage policy plus security scanning.
2. Add authentication, authorization, and tenant-isolation hardening.
3. Add S3-compatible storage plus tested backup/restore procedures.
4. Create disposable integration infrastructure and a live smoke-test CI job.
5. Add deployment manifests and a gated deployment pipeline.
6. Exercise failure recovery, scaling, observability, and security acceptance tests before production use.

Each item should ship with documentation, automated validation where practical, and explicit rollback or recovery notes.
