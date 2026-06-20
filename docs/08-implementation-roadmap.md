# 08 — Implementation Roadmap

## Phase 0 — Repository and architecture baseline

Deliver:

```text
Solution structure
Project references
ServiceDefaults
AppHost
Architecture tests
Basic CI build
```

Acceptance criteria:

```text
dotnet build passes
architecture tests enforce dependency rules
Aspire AppHost starts empty API and Worker
```

## Phase 1 — Persistence and upload foundation

Deliver:

```text
PostgreSQL
EF Core DbContext
Initial migrations
Document entity
DocumentVersion entity
IFileStorage abstraction
S3-compatible storage implementation
Upload endpoint
Status endpoint
```

Acceptance criteria:

```text
User uploads a file.
Original file is saved to object storage.
Document and version rows are saved.
Status endpoint returns uploaded document state.
```

## Phase 2 — CAP and processing state

Deliver:

```text
DotNetCore.CAP
CAP PostgreSQL storage
RabbitMQ transport for API + Worker dev mode
DocumentProcessingRun
DocumentProcessingStep
document.uploaded event
document.preprocess.requested event
Idempotency checks
Manual retry endpoint
```

Acceptance criteria:

```text
API publishes event durably.
Worker receives event.
Processing state is updated.
Repeated message does not duplicate work.
Failed step can be retried manually.
```

## Phase 3 — Docling preprocessing

Deliver:

```text
IDocumentPreprocessor
DoclingServePreprocessor
Docling CLI alternative if needed
Store Markdown artifact
Store JSON artifact
document.preprocessed event
```

Acceptance criteria:

```text
Uploaded PDF/DOCX is converted.
Markdown and JSON are stored in object storage.
DocumentVersion points to generated artifacts.
Processing status becomes Preprocessed.
```

## Phase 4 — Chunking and embeddings

Deliver:

```text
Chunking strategy
DocumentChunk table
pgvector extension
DocumentEmbedding table
IEmbeddingService
OpenAI-compatible embedding implementation
IVectorSearchService
PgVectorSearchService
```

Acceptance criteria:

```text
Markdown is chunked.
Embeddings are generated.
Chunks and embeddings are tenant-scoped.
Vector search returns relevant chunks for a query.
```

## Phase 5 — RAG API

Deliver:

```text
AskQuestion endpoint
Retriever
Prompt builder
LLM answer generator
Citations
RAG safety prompt
Authorization filters
```

Acceptance criteria:

```text
User asks a question.
System retrieves only authorized tenant chunks.
Answer includes citations.
No unauthorized document can appear in answer or retrieved chunks.
```

## Phase 6 — Document intelligence

Deliver:

```text
Classification
Summarization
Field extraction
Entity extraction
Relationship extraction tables
```

Acceptance criteria:

```text
Processed document has summary.
Processed document has classification.
Configured extraction schema produces structured fields.
Extracted entities are stored and queryable.
```

## Phase 7 — Operations and hardening

Deliver:

```text
Observability
Correlation IDs
Audit logs
Cleanup jobs
Quotas
Rate limits
Malware scanning hook
Admin processing dashboard
RAG evaluation tests
```

Acceptance criteria:

```text
Failed documents are visible.
Processing can be retried.
Logs contain correlation IDs.
Tenant quotas can be enforced.
RAG quality can be regression-tested.
```

## MVP cut recommendation

The first usable MVP should include:

```text
Upload
Object storage
PostgreSQL metadata
CAP + RabbitMQ
Docling preprocessing
Chunking
Embeddings
Vector search
AskQuestion with citations
Tenant isolation
Basic processing retry
```

Do not include graph DB in MVP unless a concrete graph query is already required.
