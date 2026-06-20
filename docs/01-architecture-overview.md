# 01 — Architecture Overview

## Goal

Build a modular document intelligence and RAG platform that can ingest many file formats, normalize them, extract useful information, and provide secure search and question answering over tenant-scoped document collections.

## High-level flow

```text
Accept files in many formats
→ Store originals in S3-compatible object storage
→ Preprocess files with Docling
→ Generate normalized Markdown/JSON
→ Classify, summarize, extract fields, and extract entities
→ Store metadata and processing results in PostgreSQL
→ Store chunks and embeddings in pgvector
→ Optionally store relationships in graph DB or relational graph tables
→ Provide RAG search/Q&A API
```

## Main runtime components

```text
DocumentRag.Api
  ASP.NET Core HTTP API.
  Handles upload, status, search, RAG question answering, and administration endpoints.

DocumentRag.Worker
  .NET Worker Service.
  Handles CAP subscribers and background document processing.

DocumentRag.AppHost
  Aspire orchestration project for local development.
  Starts API, Worker, PostgreSQL, RabbitMQ, object storage, and Docling Serve.

PostgreSQL
  Main relational database.
  Stores metadata, tenants, users, permissions, processing state, extraction results, CAP message state, and pgvector embeddings.

S3-compatible object storage
  Stores original files, Docling Markdown, Docling JSON, extracted images, and extracted table artifacts.

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
| Object storage | S3-compatible abstraction |
| Preprocessing | Docling Serve preferred for containerized dev |
| AI services | OpenAI-compatible abstractions |
| Graph DB | Optional later |

## Important architecture principle

Application code must not depend directly on MinIO, Docling, CAP, RabbitMQ, OpenAI, pgvector implementation details, or external service SDKs.

Those dependencies belong in Infrastructure.

## Recommended runtime topology for development

```text
Aspire AppHost
├── DocumentRag.Api
├── DocumentRag.Worker
├── PostgreSQL + pgvector
├── RabbitMQ
├── S3-compatible object storage
└── Docling Serve
```

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

CAP in-memory transport should only be used when publishers and subscribers run inside the same process. If `DocumentRag.Api` and `DocumentRag.Worker` are separate processes, use RabbitMQ or another real broker even in local development.

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
