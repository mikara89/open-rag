# 09 — Testing Strategy

## Test project structure

```text
tests/
  OpenRAG.UnitTests/
  OpenRAG.IntegrationTests/
  OpenRAG.ArchitectureTests/
  OpenRAG.RagEvaluationTests/
```

## Unit tests

Focus on pure business logic.

Examples:

```text
Document status transitions
DocumentVersion creation rules
Processing step retry rules
Object key generation
Chunking logic
Permission evaluation
Prompt input construction without calling LLM
```

## Integration tests

Use real dependencies where possible.

Recommended dependencies:

```text
PostgreSQL with pgvector
RabbitMQ
S3-compatible object storage test container
Docling fake/stub for most tests
Real Docling Serve for limited smoke tests
```

Important scenarios:

```text
Upload persists metadata and object.
CAP publishes event inside transaction.
Worker receives event from RabbitMQ.
Failed consumer retries safely.
Duplicate message does not duplicate chunks.
Tenant A cannot retrieve Tenant B chunks.
Deleted document is excluded from RAG.
```

## Architecture tests

Enforce dependency rules.

Examples:

```text
Domain must not reference Infrastructure.
Application must not reference Infrastructure.
Application must not reference ASP.NET Core.
Domain must not reference EF Core.
API controllers must not directly use AppDbContext.
Worker consumers must not contain workflow logic.
```

## Contract tests

Test external adapter contracts with fakes and real local containers.

Adapters:

```text
IFileStorage
IDocumentPreprocessor
IEmbeddingService
IChatCompletionService
IVectorSearchService
IDocumentEventBus
```

## RAG evaluation tests

Create a small golden dataset:

```text
documents/
questions/
expected citations/
expected answer facts/
forbidden answer facts/
```

Evaluate:

```text
retrieval recall
citation correctness
answer groundedness
tenant isolation
prompt-injection resistance
```

## Security tests

Minimum tests:

```text
cross-tenant document retrieval fails
cross-tenant vector retrieval fails
unauthorized document object cannot be opened
deleted document cannot be queried
document prompt injection is ignored
oversized upload is rejected
unsupported MIME type is rejected
```

## CI strategy

Early MVP CI:

```text
dotnet format
dotnet build
unit tests
architecture tests
```

Later CI:

```text
integration tests with containers
RAG golden-set tests
dependency vulnerability scan
container image scan
```
