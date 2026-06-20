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

## Recommended event flow

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
    string ContentHash,
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
