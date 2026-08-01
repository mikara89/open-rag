# OpenRAG

OpenRAG is an API-first .NET document intelligence and retrieval-augmented generation platform. The MVP implements document lifecycle APIs, an asynchronous CAP processing pipeline, provider abstractions, document intelligence, and server-side pgvector retrieval. It does not include a UI or production deployment manifests.

## Target direction

The platform accepts files, stores originals through `IFileStorage`, preprocesses them with mock or Docling Serve providers, stores normalized Markdown/JSON, extracts intelligence, stores metadata in PostgreSQL, stores embeddings in pgvector, and exposes RAG search/Q&A APIs. The MVP storage provider uses the local filesystem; S3-compatible storage is a production-readiness gap.

## Documentation index

| Document | Purpose |
|---|---|
| [Architecture overview](docs/01-architecture-overview.md) | High-level system architecture and main decisions |
| [Clean/Onion architecture](docs/02-clean-onion-architecture.md) | Onion/Clean Architecture boundaries and dependency rules |
| [Solution structure](docs/03-solution-structure.md) | .NET solution and project structure |
| [Local development with Aspire](docs/04-local-development-with-aspire.md) | Aspire-based local development environment |
| [Processing pipeline and CAP](docs/05-processing-pipeline-and-cap.md) | CAP events, workers, transaction, and idempotency rules |
| [Data storage model](docs/06-data-storage-model.md) | PostgreSQL, pgvector, object storage, and optional graph model |
| [Security and RAG safety](docs/07-security-and-rag-safety.md) | Tenant isolation, ACL, RAG safety, file safety, and audit requirements |
| [Implementation roadmap](docs/08-implementation-roadmap.md) | MVP phases and build order |
| [Testing strategy](docs/09-testing-strategy.md) | Unit, integration, architecture, and RAG evaluation tests |
| [Configuration and secrets](docs/10-configuration-and-secrets.md) | Provider configuration, API key resolution, and secrets handling |
| [MVP local run](docs/11-mvp-local-run.md) | Local run, smoke test, and MVP acceptance checklist |
| [Production-readiness roadmap](docs/12-production-readiness-roadmap.md) | Remaining gaps before production use |
| [Documentation review checklist](docs/13-documentation-review-checklist.md) | PR guidance for deciding which docs must change |
| [GitHub governance](docs/14-github-governance.md) | Recommended branch protection and emergency procedures |
| [JWT authentication](docs/15-authentication.md) | JWT Bearer configuration, claim contract, policies, and local usage |
| [Trusted tenant resolution](docs/16-trusted-tenant-resolution.md) | JWT tenant claims, API trust boundary, and Worker propagation |
| [Security policy](SECURITY.md) | Supported status, vulnerability reporting, and known security limitations |
| [ADR 0001](docs/adr/0001-use-clean-onion-with-vertical-slices.md) | Architecture style |
| [ADR 0002](docs/adr/0002-use-aspire-for-local-development.md) | Aspire local development |
| [ADR 0003](docs/adr/0003-use-cap-with-postgresql-storage.md) | CAP with PostgreSQL storage |

## Key decisions

- Backend: .NET / ASP.NET Core.
- Architecture: Onion/Clean Architecture with vertical slice use cases.
- Local development orchestration: Aspire AppHost.
- Database: PostgreSQL.
- CAP message storage: PostgreSQL.
- CAP dev transport:
  - In-memory only for single-process development.
  - RabbitMQ for realistic API + Worker development.
- Vector search: pgvector first (implemented via `Pgvector.EntityFrameworkCore`).
- File storage: local filesystem behind `IFileStorage` for the MVP; an S3-compatible provider is planned.
- Preprocessing: mock or Docling Serve providers behind `IDocumentPreprocessor`.
- AI providers: OpenAI-compatible interfaces behind abstractions.

## MVP capabilities

| Feature | Endpoint |
|---------|----------|
| Upload document | `POST /api/documents/upload` |
| Processing pipeline | Preprocess → Chunk → Intelligence → Embed → Ready |
| Document list | `GET /api/documents` |
| Document detail | `GET /api/documents/{id}` |
| Document status | `GET /api/documents/{id}/status` |
| Delete document | `DELETE /api/documents/{id}` |
| Reprocess document | `POST /api/documents/{id}/reprocess` |
| Markdown artifact | `GET .../artifacts/markdown` |
| JSON artifact | `GET .../artifacts/json` |
| List chunks | `GET .../chunks` |
| Chunk detail | `GET .../chunks/{chunkId}` |
| RAG ask | `POST /api/rag/ask` |
| Provider diagnostics | `GET /api/system/providers` |
| Document intelligence | `GET .../versions/{versionId}/intelligence` |
| Authentication | JWT Bearer on every `/api` endpoint; exactly one non-empty GUID `sub` and `tenant_id` claim required by default |
| Administrator policy | `GET /api/system/providers` requires the `admin` role |
| Development OpenAPI | Anonymous `GET /openapi/v1.json` only in Development |
| Config validation | Startup-time `IValidateOptions<T>` |
| Secret handling | Env vars, user secrets, never logged |

## PostgreSQL and pgvector compatibility

Runtime processing and RAG retrieval require PostgreSQL with the pgvector extension. Aspire uses `pgvector/pgvector:pg17`; CI intentionally does not start PostgreSQL because the automated tests use isolated models and fakes.

The `MigrateEmbeddingVectorToPgvector` EF migration changes the embedding column from `bytea` to native `vector`. Back up any existing database first. Treat legacy `bytea` embedding values as regenerable data: they may need to be dropped during migration and recreated through `POST /api/documents/{id}/reprocess` with `forceEmbeddings: true`.

## Security boundary

Every `/api` endpoint requires a validated JWT Bearer token. The default user-ID claim is `sub` and the default tenant claim is `tenant_id`; each must occur exactly once and contain a non-empty GUID. `GET /api/system/providers` additionally requires the `admin` role. Tenant identity comes only from the validated claim—there is no tenant header, request-body selection, or development fallback. Configure `Authentication:Jwt:Authority` and `Authentication:Jwt:Audience` through environment variables or user secrets before starting the API. See [JWT authentication](docs/15-authentication.md) and [trusted tenant resolution](docs/16-trusted-tenant-resolution.md).

> **Trusted tenant resolution is complete, but multitenant authorization is not.** Full resource authorization, repository/storage/vector isolation auditing, and adversarial cross-tenant integration testing remain P0.4 and P0.5 work. The system is not yet production-safe for multitenant use.

## Quick validation

```bash
# Same NuGet audit, Release build, test/coverage, format, and documentation checks as GitHub CI
./scripts/ci-local.ps1

# High/Critical NuGet vulnerability audit, including transitive dependencies
./scripts/dependency-audit.ps1

# Restore, NuGet audit, Release build/test, TRX, coverage, and format checks
./scripts/verify.ps1

# Documentation only
./scripts/docs-check.ps1

# API smoke test (requires running services)
./scripts/mvp-smoke-test.ps1 -Token $env:OPENRAG_ACCESS_TOKEN
```

GitHub Actions runs dependency-free validation for pull requests to `main` and pushes to `main`, fails when NuGet vulnerability data is unavailable or when High or Critical vulnerabilities are reported (including transitive packages), and retains TRX test results and Cobertura coverage as separate artifacts. The live MVP smoke test is deliberately manual until CI has disposable service infrastructure.
