# 05 — Processing Pipeline and CAP

## Decision

Use DotNetCore.CAP as the application event bus abstraction implementation.

Use PostgreSQL for CAP storage in all environments, including development.

Use a real broker such as RabbitMQ when API and Worker are separate processes.

## Why CAP

CAP gives an outbox-style event publishing model and durable message state. This is useful because document ingestion is a multi-step workflow where each step must survive process crashes and retries.

## Event principles

Events must contain:

```text
IDs
tenant context
storage references
correlation metadata
processor/run metadata
```

Events must not contain:

```text
large file bytes
full document text
large Markdown
large JSON
embedding arrays
sensitive content unless necessary
```

## Implemented event flow (MVP)

```text
document.uploaded
    ↓
document.preprocess.requested
    ↓
document.preprocessed
    ↓
document.chunking.requested
    ↓
document.chunked
    ↓
document.intelligence.requested   ← classification, summary, keywords, entities
    ↓
document.intelligence.generated
    ↓
document.embeddings.requested
    ↓
document.embeddings.generated
    ↓
document.ready
```

If `Intelligence.Enabled = false`, intelligence is skipped and the flow goes directly from chunked → embeddings.requested.

## Recommended future event flow (post-MVP)

```text
document.uploaded
    ↓
document.preprocess.requested
    ↓
document.preprocessed
    ↓
document.chunking.requested
    ↓
document.chunked
    ↓
document.embedding.requested
    ↓
document.embedded
    ↓
document.classification.requested
    ↓
document.classified
    ↓
document.summary.requested
    ↓
document.summary.completed
    ↓
document.extraction.requested
    ↓
document.extraction.completed
    ↓
document.ready
```

## Example event contract

```csharp
public sealed record DocumentPreprocessRequestedEvent(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    Guid ProcessingRunId,
    string OriginalObjectKey,
    string FileName,
    string MimeType,
    string CorrelationId,
    DateTimeOffset OccurredAt
);
```

## Required metadata on every event

```text
TenantId
DocumentId
VersionId
ProcessingRunId
CorrelationId
OccurredAt
```

`TenantId` originates from the validated JWT tenant claim on the initial API request. The Worker has no HTTP tenant resolver or development fallback. Each CAP consumer copies the event tenant unchanged into the next processing command or event; preprocess, chunk, intelligence, and embedding handlers reject an empty tenant and use the command tenant for every scoped operation.

See [trusted tenant resolution](16-trusted-tenant-resolution.md) for the API/Worker trust boundary. This explicit propagation does not replace the P0.4 resource-authorization and isolation audit.

Optional but recommended:

```text
CausationId
UserId
TraceId
ProcessorVersion
InputHash
```

## Transaction rule

When a use case changes PostgreSQL state and publishes an event, both operations must happen in the same transactional boundary.

Example:

```text
UploadDocumentHandler
  1. Save original file to object storage.
  2. Begin database transaction.
  3. Insert Document.
  4. Insert DocumentVersion.
  5. Insert ProcessingRun.
  6. Publish document.uploaded through CAP inside the transaction.
  7. Commit.
```

If the transaction fails, the event must not be published.

If the transaction succeeds, the event must be durably available to CAP.

## Idempotency rule

Every consumer must be safe to execute more than once.

Use idempotency key:

```text
TenantId + DocumentId + VersionId + ProcessingStep + InputHash + ProcessorVersion
```

Before running a processing step:

```text
Check if the same step already completed for the same input hash and processor version.
If completed, skip work and publish the next event if needed.
If in progress and not stale, skip or reschedule.
If failed and retry allowed, continue.
```

## Processing state model

Minimum states:

```text
Uploaded
PreprocessingRequested
Preprocessing
Preprocessed
ChunkingRequested
Chunking
Chunked
EmbeddingRequested
Embedding
Embedded
ClassificationRequested
Classifying
Classified
SummaryRequested
Summarizing
SummaryCompleted
ExtractionRequested
Extracting
ExtractionCompleted
Ready
Failed
PartiallyReady
Deleted
```

## Failure handling

Each processing step should track:

```text
Status
AttemptCount
MaxAttempts
StartedAt
CompletedAt
LastErrorCode
LastErrorMessage
LastErrorAt
WorkerId
ProcessorVersion
InputHash
OutputHash
```

## Retry strategy

Use layered retry:

```text
CAP retry for transient message handling failures
Application-level retry tracking for document processing steps
Manual retry endpoint for failed documents
```

Avoid infinite retries for bad documents.

## Worker design

For MVP:

```text
One Worker project
Multiple CAP subscribers
Limited concurrency
```

Later split into:

```text
Preprocessing worker
Embedding worker
LLM extraction worker
Cleanup worker
```

## Consumer rule

CAP consumers should be thin:

```csharp
public sealed class DocumentPreprocessRequestedConsumer
{
    [CapSubscribe("document.preprocess.requested")]
    public Task Handle(DocumentPreprocessRequestedEvent message, CancellationToken ct)
    {
        return sender.Send(new PreprocessDocumentCommand(...), ct);
    }
}
```

The workflow belongs in Application handlers, not in CAP subscriber classes.

## Reprocessing a document

Use the reprocess endpoint after changing preprocessing, chunking, or embedding settings for an existing document.

### Endpoint

```
POST /api/documents/{documentId}/reprocess
```

Request body:

```json
{
  "forcePreprocess": true,
  "forceChunk": true,
  "forceEmbeddings": true
}
```

Response:

```json
{
  "documentId": "33333333-3333-3333-3333-333333333333",
  "versionId": "44444444-4444-4444-4444-444444444444",
  "status": "Processing",
  "correlationId": "abc123..."
}
```

### Behavior

| Flag | Action |
|------|--------|
| `forcePreprocess` | Clears old preprocessing references, enqueues preprocessing via `document.preprocess.requested` |
| `forceChunk` | Deletes existing chunks for the version, enqueues chunking |
| `forceEmbeddings` | Deletes existing embeddings for the version, enqueues embedding generation |

The event chain preserves the existing flow:

```
Preprocess → Chunk → Intelligence (when enabled) → Embed → Ready
```

### Regenerating embeddings after the pgvector migration

`MigrateEmbeddingVectorToPgvector` changes `document_embeddings.Vector` from serialized `bytea` data to PostgreSQL's native `vector` type. Back up the database before applying the migration. Legacy `bytea` values should be treated as regenerable: a migration may require old embedding rows to be removed rather than converted.

After the schema migration, regenerate embeddings for each affected document through the normal pipeline:

```http
POST /api/documents/{documentId}/reprocess
Content-Type: application/json

{
  "forcePreprocess": false,
  "forceChunk": false,
  "forceEmbeddings": true
}
```

Using the endpoint preserves processing history and embedding metadata. Do not write replacement vectors directly to the database.

## Idempotency and Retries (MVP)

### Handler safety guarantees

All processing handlers (Preprocess, Chunk, Embeddings) are safe for duplicate event delivery:

- **Missing document/version → no-op.** If the document or version has been deleted by the time a delayed/retried event arrives, the handler logs a warning and returns a no-op status (e.g., `DocumentNotFound`, `VersionNotFound`, `DocumentDeleted`). No exception is thrown.

- **Step already completed → skip.** Each handler checks if the processing step for this run is already `Completed`. If so, it returns immediately without re-running the work.

- **Clean-slate chunking.** Before creating new chunks, the handler deletes all existing chunks *and embeddings* for the version. This ensures a retry or rerun always starts from a consistent state — no duplicate chunks, no orphaned embeddings referencing deleted chunks.

- **Clean-slate embeddings.** Before generating new embeddings, the handler deletes all existing embeddings for the version. Duplicate handler calls do not produce duplicate embeddings.

- **Document marked Failed on error.** If preprocessing or chunking fails, both the version and the document are marked `Failed`. Previously only the version was marked failed; the document now reflects the failure for better visibility.

- **Attempt count tracks retries.** Each call to `step.Start()` increments `AttemptCount`. A retry of a previously failed step (same run) will show `AttemptCount >= 2`.

- **Source files preserved.** Original uploaded files are never deleted on failure. Only generated artifacts (chunks, embeddings) are cleaned up during retries.

### Delete safety

- **Delete rejects in-flight documents.** A document with status `Processing` cannot be deleted.
- **Delete cascades data.** Embeddings → chunks → document, in a single transaction.
- **Events after delete no-op.** If a document is deleted while a processing event is still in flight (e.g., CAP retry), the handler detects the deleted status and returns a no-op.
- **Best-effort storage cleanup.** After the DB transaction commits, the handler attempts to delete generated Docling artifacts (Markdown + JSON) from physical storage. Failures are logged but never fail the operation.

### Reprocess is the manual retry mechanism

CAP retries are automatic for transient failures (e.g., RabbitMQ connectivity), but application-level retries for bad documents or provider errors are manual via `POST /api/documents/{id}/reprocess`:

- Reprocess creates a **new** processing run.
- The `forcePreprocess`, `forceChunk`, `forceEmbeddings` flags control which pipeline stages restart.
- Chunks/embeddings are deleted before recreation when their respective force flags are set.
- The original file and document record are preserved.

### What happens when...

| Scenario | Behavior |
|----------|----------|
| Same event delivered twice | Step-completion check skips the second delivery |
| Preprocessing succeeds, chunking fails | Version marked Failed. Document marked Failed. Reprocess to retry. |
| Chunks exist before chunking starts | Old chunks and embeddings are deleted, then new chunks are created |
| Embeddings exist before embedding starts | Old embeddings are deleted, then new embeddings are created |
| Document deleted while event in flight | Handler detects Deleted status and returns a no-op |
| Storage write succeeds, DB save fails | Storage artifact may be orphaned (logged as TODO for future compensation) |
| CAP retries a failed handler | Step attempt count increments; no duplicate data created |

The first event published depends on flags:
- `forcePreprocess=true` → `document.preprocess.requested`
- `forcePreprocess=false, forceChunk=true` → `document.chunking.requested`
- `only forceEmbeddings=true` → `document.embeddings.requested`

### Idempotency

- The original uploaded file is never deleted.
- The document record is preserved.
- The same document and version are reused.
- Calling reprocess again while processing returns a 409-style conflict.
- Deleting chunks/embeddings when none exist is safe (no-op).

### Use cases

- Changed preprocessing provider (e.g. Mock → Docling)
- Updated Docling options or extraction schema
- Different chunking provider or parameters
- New embedding model or provider
- Regenerate legacy embeddings after migrating from `bytea` to pgvector

## Processing history and troubleshooting

### Checking processing status

```
GET /api/documents/{documentId}/status
```

The response includes `processingRuns` with full history:

```json
{
  "processingRuns": [
    {
      "runId": "...",
      "reason": "InitialUpload",
      "status": "Completed",
      "startedAt": "2026-06-20T12:00:00Z",
      "completedAt": "2026-06-20T12:00:05Z",
      "correlationId": "abc123...",
      "steps": [
        {
          "name": "Preprocess",
          "status": "Completed",
          "attemptCount": 1,
          "startedAt": "2026-06-20T12:00:00Z",
          "completedAt": "2026-06-20T12:00:02Z",
          "errorMessage": null
        }
      ]
    }
  ]
}
```

### Common failures

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| Preprocess step failed | Docling Serve unavailable | Check Docling Serve is running and accessible |
| Preprocess step failed | Invalid/unreadable file | Upload a supported file format |
| Chunk step failed | Markdown artifact missing or corrupt | Reprocess with `forcePreprocess: true` |
| Embed step failed | Embedding provider unavailable | Check LM Studio/Ollama/OpenAI endpoint |
| Document stuck in Processing | CAP/RabbitMQ connectivity | Check RabbitMQ is running and accessible |
| Document stuck in Processing | Worker not running | Start the Worker process |
| All steps "Pending" | Event not published | Check CAP outbox and message broker |

### Troubleshooting steps

1. Check document status: `GET /api/documents/{id}/status`
2. Inspect the latest `processingRuns` entry for failed steps
3. Look at `errorMessage` on failed steps for details
4. Check Worker logs for correlation ID
5. Verify all infrastructure services are running (Aspire dashboard)
6. Reprocess: `POST /api/documents/{id}/reprocess`
