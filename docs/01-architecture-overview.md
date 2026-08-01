# 01 — Architecture Overview

## Goal

Build a modular document intelligence and RAG platform that can ingest many file formats, normalize them, extract useful information, and provide secure search and question answering over tenant-scoped document collections.

## High-level flow

```text
Accept files in supported formats
→ Store originals through IFileStorage (local filesystem in the MVP)
→ Preprocess files with a mock provider or Docling Serve
→ Generate normalized Markdown/JSON
→ Classify, summarize, extract fields, and extract entities
→ Store metadata and processing results in PostgreSQL
→ Store chunks and embeddings in pgvector
→ Optionally store relationships in graph DB or relational graph tables
→ Provide RAG search/Q&A API
```

## Main runtime components

```text
OpenRAG.Api
  ASP.NET Core HTTP API.
  Handles upload, status, search, RAG question answering, and administration endpoints.

OpenRAG.Worker
  .NET Worker Service.
  Handles CAP subscribers and background document processing.

OpenRAG.AppHost
  Aspire orchestration project for local development.
  Starts API, Worker, PostgreSQL/pgvector, RabbitMQ, and Docling Serve.

PostgreSQL
  Main relational database.
  Stores document metadata, processing state, extraction results, CAP message state, and pgvector embeddings.

IFileStorage
  Stores original files and generated Markdown/JSON artifacts.
  The MVP implementation uses local filesystem storage; an S3-compatible provider is planned for production readiness.

Docling Serve / Docling CLI
  Converts source documents into normalized Markdown/JSON.

LLM/embedding provider
  External or local OpenAI-compatible endpoint behind application abstractions.
```

## Core architecture decisions

| Area | Decision |
|---|---|
| Backend | .NET / ASP.NET Core |
| Architecture style | Onion/Clean Architecture + vertical slices |
| Local orchestration | Aspire |
| Queue abstraction | DotNetCore.CAP |
| CAP storage | PostgreSQL |
| CAP local realistic transport | RabbitMQ |
| CAP simple single-process transport | In-memory |
| Metadata DB | PostgreSQL |
| Vector search | pgvector first |
| File storage | Local filesystem behind `IFileStorage` for the MVP; S3-compatible provider later |
| Preprocessing | Docling Serve preferred for containerized dev |
| AI services | OpenAI-compatible abstractions |
| Graph DB | Optional later |

## Important architecture principle

Application code must not depend directly on MinIO, Docling, CAP, RabbitMQ, OpenAI, pgvector implementation details, or external service SDKs.

Those dependencies belong in Infrastructure.

## Recommended runtime topology for development

```text
Aspire AppHost
├── OpenRAG.Api
├── OpenRAG.Worker
├── PostgreSQL + pgvector
├── RabbitMQ
├── Local filesystem storage
└── Docling Serve
```

PostgreSQL must provide the pgvector extension. The AppHost pins the development image to `pgvector/pgvector:pg17`.

## Recommended runtime topology for production

```text
API service
Worker service(s)
Managed/self-hosted PostgreSQL with pgvector
Message broker: RabbitMQ / Kafka / Azure Service Bus
S3-compatible object storage
Docling preprocessing service or worker-side processor
LLM / embedding provider
Observability stack
```

## Key warning

CAP in-memory transport should only be used when publishers and subscribers run inside the same process. If `OpenRAG.Api` and `OpenRAG.Worker` are separate processes, use RabbitMQ or another real broker even in local development.

## Document lifecycle API

### List documents

```
GET /api/documents?pageNumber=1&pageSize=20&status=Ready&search=README
```

Returns paginated, tenant-filtered document list with chunk/embedding counts.

### Upload document

```
POST /api/documents/upload
Content-Type: multipart/form-data
```

### Get document detail

```
GET /api/documents/{documentId}
```

Returns document metadata, latest version with artifact presence flags, and chunk/embedding counts. Returns 404 for missing or wrong-tenant documents.

### Get document processing status

```
GET /api/documents/{documentId}/status
```

Returns detailed processing status with per-version step tracking.

### Reprocess document

```
POST /api/documents/{documentId}/reprocess
{ "forcePreprocess": true, "forceChunk": true, "forceEmbeddings": true }
```

Triggers full or partial reprocessing. Use after changing preprocessing, chunking, or embedding settings.

The pgvector migration changes stored embeddings from a serialized `bytea` value to a native `vector` column. Back up existing databases before applying it. Legacy embeddings may need to be removed during migration and regenerated with this endpoint using `forceEmbeddings: true`.

### Delete document

```
DELETE /api/documents/{documentId}
```

Cascading delete: removes embeddings, chunks, and document/versions. Returns 204 No Content. Rejects deletion while processing.

### Ask RAG question

```
POST /api/rag/ask
{ "question": "...", "tenantId": "...", "topK": 5, "model": "mock-chat" }
```

### Inspect artifacts and chunks

```
GET /api/documents/{id}/versions/{vid}/artifacts/markdown   → text/markdown
GET /api/documents/{id}/versions/{vid}/artifacts/json       → application/json
GET /api/documents/{id}/versions/{vid}/chunks?pageNumber=&pageSize=&search=
GET /api/documents/{id}/versions/{vid}/chunks/{chunkId}     → chunk + embedding metadata
```

These endpoints help debug retrieval quality by inspecting extracted Markdown/JSON and generated chunks. Chunk list supports search by content, section title, and page number.
