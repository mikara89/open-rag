# 04 — Local Development with Aspire

## Decision

Use Aspire as the default local development orchestrator.

Aspire should start and connect:

```text
DocumentRag.Api
DocumentRag.Worker
PostgreSQL
RabbitMQ
S3-compatible object storage
Docling Serve
Optional local OpenAI-compatible model endpoint
```

## Why Aspire

Aspire gives the project a single code-first place to describe a distributed local environment. It models resources such as services, databases, queues, containers, and cloud dependencies, and gives developers one command and dashboard for local orchestration.

## AppHost project

Add:

```text
src/DocumentRag.AppHost/
src/DocumentRag.ServiceDefaults/
```

The AppHost owns dev-time resource orchestration only. It must not contain business logic.

## Recommended local modes

### Mode A — Simple single-process dev

Use when testing basic API flows only.

```text
API hosts CAP consumers
PostgreSQL for application DB and CAP storage
CAP transport: InMemory
No separate Worker process
```

This mode is useful for fast debugging, but it does not validate real distributed messaging.

### Mode B — Realistic distributed dev

Use as the default team development mode.

```text
DocumentRag.Api process
DocumentRag.Worker process
PostgreSQL for application DB and CAP storage
RabbitMQ for CAP transport
S3-compatible object storage
Docling Serve
```

This mode catches real messaging, worker, connection, retry, and orchestration issues.

## Important CAP transport rule

Do not use CAP in-memory transport when API and Worker are separate processes.

The in-memory queue is process-local. If the API publishes a message and the Worker is a separate process, the Worker will not receive the message.

## Proposed AppHost sketch

This is intentionally a sketch. Package names and exact APIs should be adjusted to the Aspire version used in the project.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var db = postgres.AddDatabase("documentrag");

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

var objectStorage = builder.AddContainer("object-storage", "minio/minio")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEnvironment("MINIO_ROOT_USER", "minioadmin")
    .WithEnvironment("MINIO_ROOT_PASSWORD", "minioadmin")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "s3")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
    .WithDataVolume();

var docling = builder.AddContainer("docling", "quay.io/docling-project/docling-serve")
    .WithHttpEndpoint(port: 5001, targetPort: 5001, name: "http");

var api = builder.AddProject<Projects.DocumentRag_Api>("api")
    .WithReference(db)
    .WithReference(rabbitmq)
    .WithReference(objectStorage)
    .WithReference(docling)
    .WaitFor(db)
    .WaitFor(rabbitmq)
    .WaitFor(objectStorage)
    .WaitFor(docling);

var worker = builder.AddProject<Projects.DocumentRag_Worker>("worker")
    .WithReference(db)
    .WithReference(rabbitmq)
    .WithReference(objectStorage)
    .WithReference(docling)
    .WaitFor(db)
    .WaitFor(rabbitmq)
    .WaitFor(objectStorage)
    .WaitFor(docling);

builder.Build().Run();
```

## Object storage note

MinIO can be used as a local S3-compatible object storage option, but the application should not depend on MinIO directly.

Use:

```text
IFileStorage
S3CompatibleFileStorage
```

This keeps the door open for:

```text
AWS S3
Azure Blob adapter
Garage
SeaweedFS
Ceph RGW
Other S3-compatible stores
```

## Configuration names

Recommended logical connection names:

```text
ConnectionStrings__documentrag
ConnectionStrings__rabbitmq
ObjectStorage__Endpoint
ObjectStorage__AccessKey
ObjectStorage__SecretKey
ObjectStorage__BucketName
Docling__BaseUrl
Ai__Chat__BaseUrl
Ai__Embedding__BaseUrl
```

## Database migrations

Use one of these approaches:

```text
1. Aspire EF migration support in AppHost
2. Dedicated migration worker/service
3. Manual dotnet ef database update for early MVP
```

Recommended for MVP:

```text
Use Aspire/AppHost migration support if stable in the chosen Aspire version.
Otherwise, create a small migration runner hosted service.
```

## Developer startup command

Preferred:

```bash
aspire run
```

Fallback:

```bash
dotnet run --project src/DocumentRag.AppHost
```

## Local development checklist

- Docker or compatible container runtime installed.
- Aspire CLI installed.
- PostgreSQL starts with persistent volume.
- RabbitMQ management UI reachable.
- Object storage console reachable.
- Docling Serve reachable.
- API can upload a file.
- Worker receives CAP event.
- Original file saved to object storage.
- Metadata saved to PostgreSQL.
- Processing state visible through API.
