# 03 — Solution Structure

## Proposed solution

```text
OpenRAG.sln

src/
  OpenRAG.Domain/
  OpenRAG.Application/
  OpenRAG.Infrastructure/
  OpenRAG.Api/
  OpenRAG.Worker/
  OpenRAG.AppHost/
  OpenRAG.ServiceDefaults/

tests/
  OpenRAG.UnitTests/
  OpenRAG.IntegrationTests/
  OpenRAG.ArchitectureTests/
  OpenRAG.RagEvaluationTests/
```

## Project purposes

### `OpenRAG.Domain`

Pure domain model.

```text
Entities/
ValueObjects/
Enums/
DomainEvents/
Rules/
```

### `OpenRAG.Application`

Use cases and interfaces.

```text
Abstractions/
  IFileStorage.cs
  IDocumentPreprocessor.cs
  IDocumentEventBus.cs
  IEmbeddingService.cs
  IChatCompletionService.cs
  IVectorSearchService.cs
  ICurrentTenant.cs
  ICurrentUser.cs
  IClock.cs

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

DTOs/
Validation/
```

### `OpenRAG.Infrastructure`

Concrete adapters.

```text
Persistence/
  AppDbContext.cs
  EntityConfigurations/
  Repositories/
  Migrations/

Storage/
  S3CompatibleFileStorage.cs
  LocalFileStorage.cs

Messaging/
  CapDocumentEventBus.cs
  Subscribers/

Preprocessing/
  DoclingServePreprocessor.cs
  DoclingCliPreprocessor.cs

AI/
  OpenAiCompatibleChatCompletionService.cs
  OpenAiCompatibleEmbeddingService.cs

Vector/
  PgVectorSearchService.cs

Security/
  PermissionEvaluator.cs

Observability/
  CorrelationIdMiddleware.cs
```

### `OpenRAG.Api`

HTTP boundary.

```text
Controllers/
  DocumentsController.cs
  RagController.cs
  ProcessingController.cs

Program.cs
OpenApi/
Auth/
```

### `OpenRAG.Worker`

Background processing boundary.

```text
Program.cs
Consumers/
  DocumentUploadedConsumer.cs
  DocumentPreprocessRequestedConsumer.cs
  DocumentChunkingRequestedConsumer.cs
  DocumentEmbeddingRequestedConsumer.cs
  DocumentClassificationRequestedConsumer.cs
```

### `OpenRAG.AppHost`

Aspire local development orchestrator.

Responsibilities:

```text
Start API
Start Worker
Start PostgreSQL
Start RabbitMQ
Start object storage container
Start Docling Serve container
Wire connection strings and environment variables
Expose dashboard and logs
```

### `OpenRAG.ServiceDefaults`

Shared Aspire service defaults.

Typical contents:

```text
OpenTelemetry
Health checks
Service discovery defaults
Resilience defaults
Logging defaults
```

## Dependency rules

Allowed references:

```text
Application -> Domain
Infrastructure -> Application, Domain
Api -> Application, Infrastructure, ServiceDefaults
Worker -> Application, Infrastructure, ServiceDefaults
AppHost -> Api, Worker
Tests -> target projects depending on test type
```

Forbidden references:

```text
Domain -> Application
Domain -> Infrastructure
Application -> Infrastructure
Application -> Api
Application -> Worker
Infrastructure -> Api
Infrastructure -> Worker
```

## Recommended architecture tests

Add architecture tests to enforce:

```text
Domain must not reference Infrastructure.
Application must not reference Infrastructure.
Application must not reference ASP.NET Core.
Domain must not reference EF Core.
API controllers must not directly reference AppDbContext.
Worker consumers must call Application handlers instead of containing workflow logic.
```
