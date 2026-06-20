# OpenRAG Architecture Documentation Pack

This documentation pack defines the proposed architecture and implementation plan for a modular .NET RAG/document intelligence platform.

## Target direction

The platform accepts files in many formats, stores originals in S3-compatible object storage, preprocesses them with Docling, stores normalized Markdown/JSON, extracts intelligence, stores metadata in PostgreSQL, stores embeddings in pgvector, and exposes RAG search/Q&A APIs.

## Documentation index

| Document | Purpose |
|---|---|
| `docs/01-architecture-overview.md` | High-level system architecture and main decisions |
| `docs/02-clean-onion-architecture.md` | Onion/Clean Architecture boundaries and dependency rules |
| `docs/03-solution-structure.md` | Proposed .NET solution/project structure |
| `docs/04-local-development-with-aspire.md` | Aspire-based local development environment |
| `docs/05-processing-pipeline-and-cap.md` | CAP events, workers, transaction, and idempotency rules |
| `docs/06-data-storage-model.md` | PostgreSQL, pgvector, object storage, and optional graph model |
| `docs/07-security-and-rag-safety.md` | Tenant isolation, ACL, RAG safety, file safety, and audit requirements |
| `docs/08-implementation-roadmap.md` | MVP phases and build order |
| `docs/09-testing-strategy.md` | Unit, integration, architecture, and RAG evaluation tests |
| `docs/adr/0001-use-clean-onion-with-vertical-slices.md` | ADR for architecture style |
| `docs/adr/0002-use-aspire-for-local-development.md` | ADR for Aspire local development |
| `docs/adr/0003-use-cap-with-postgresql-storage.md` | ADR for CAP with PostgreSQL storage |

## Key decisions

- Backend: .NET / ASP.NET Core.
- Architecture: Onion/Clean Architecture with vertical slice use cases.
- Local development orchestration: Aspire AppHost.
- Database: PostgreSQL.
- CAP message storage: PostgreSQL.
- CAP dev transport:
  - In-memory only for single-process development.
  - RabbitMQ for realistic API + Worker development.
- Vector search: pgvector first.
- Object storage: S3-compatible abstraction behind `IFileStorage`.
- Local object storage: MinIO or another S3-compatible container, but avoid coupling the domain to MinIO.
- Preprocessing: Docling Serve preferred for containerized dev; Docling CLI acceptable for simple local tests.
- AI providers: OpenAI-compatible interfaces behind abstractions.
