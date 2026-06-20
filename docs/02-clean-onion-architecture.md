# 02 — Clean / Onion Architecture

## Recommendation

Use Onion/Clean Architecture at project boundaries and vertical slice architecture inside the Application layer.

```text
Domain
↑
Application
↑
Infrastructure
↑
API / Worker
```

More explicitly:

```text
DocumentRag.Domain
  no dependencies

DocumentRag.Application
  depends on Domain
  defines use cases and interfaces

DocumentRag.Infrastructure
  depends on Application + Domain
  implements persistence, messaging, storage, AI, preprocessing, and vector search

DocumentRag.Api
  depends on Application + Infrastructure
  composition root for HTTP API

DocumentRag.Worker
  depends on Application + Infrastructure
  composition root for background processing
```

## Why this fits this platform

The core workflow is stable:

```text
upload
preprocess
chunk
embed
classify
summarize
extract
answer
```

But the tools may change:

```text
MinIO / Garage / SeaweedFS / AWS S3 / Azure Blob-compatible adapter
Docling CLI / Docling Serve / another preprocessor
pgvector / Qdrant / Milvus / Azure AI Search
OpenAI / Azure OpenAI / local OpenAI-compatible endpoint
RabbitMQ / Kafka / Azure Service Bus
```

Clean boundaries allow those tools to be replaced without rewriting application workflows.

## Domain layer

Contains core business entities, value objects, enums, and domain rules.

Suggested contents:

```text
Document
DocumentVersion
DocumentStatus
DocumentProcessingStatus
DocumentProcessingStep
TenantId
DocumentId
VersionId
DocumentPermission
DomainEvents
```

Domain should not reference:

```text
EF Core
ASP.NET Core
CAP
RabbitMQ
MinIO
Docling
OpenAI
pgvector
HTTP clients
SDK clients
```

## Application layer

Contains use cases and abstractions.

Suggested vertical slices:

```text
Application/
  Documents/
    UploadDocument/
    GetDocumentStatus/
    DeleteDocument/
  Processing/
    PreprocessDocument/
    ChunkDocument/
    GenerateEmbeddings/
    ClassifyDocument/
    SummarizeDocument/
    ExtractFields/
  Rag/
    AskQuestion/
    SearchDocuments/
  Administration/
    RetryProcessingStep/
    ListFailedDocuments/
```

Application defines interfaces:

```csharp
public interface IFileStorage {}
public interface IDocumentPreprocessor {}
public interface IDocumentEventBus {}
public interface IEmbeddingService {}
public interface IChatCompletionService {}
public interface IVectorSearchService {}
public interface ICurrentTenant {}
public interface ICurrentUser {}
public interface IClock {}
```

## Infrastructure layer

Contains implementations:

```text
Persistence/
  AppDbContext
  EntityConfigurations
  Repositories

Storage/
  S3CompatibleFileStorage
  LocalFileStorage

Messaging/
  CapDocumentEventBus
  Subscribers

Preprocessing/
  DoclingServePreprocessor
  DoclingCliPreprocessor

AI/
  OpenAiCompatibleChatCompletionService
  OpenAiCompatibleEmbeddingService

Vector/
  PgVectorSearchService
```

## API layer

Should stay thin:

```text
Controllers
Minimal API endpoints
Authentication
Authorization
Request/response mapping
Swagger/OpenAPI
```

Controllers should call Application use cases. They should not call EF Core, MinIO, Docling, CAP, or OpenAI directly.

## Worker layer

Should also stay thin:

```text
CAP subscribers
Worker bootstrap
DI composition
Hosted service configuration
```

Subscribers should translate messages into Application commands.

## Avoid over-abstraction

Do not create an interface for everything on day one.

Good abstractions:

```text
File storage
Document preprocessor
Event bus
Embedding provider
Chat/LLM provider
Vector search
Current user/tenant
Clock
```

Avoid too early:

```text
IRepository for every table
Manager classes for every entity
Factories without clear value
Generic services with unclear ownership
```

## Architectural rule

If a class contains business workflow logic, it belongs in Application.

If a class talks to an external tool, SDK, database, message broker, or file system, it belongs in Infrastructure.
