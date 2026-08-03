# 18 — Hybrid Result and Application Error Model

## Status

P0.4.2 — Hybrid Result and application error model: **Complete**.

P0.4, P0.4.1, and P0.4.2 are complete. P0.5 live adversarial infrastructure remains planned, so this status does not claim production-safe multitenancy.

## Rule and taxonomy

```text
Expected HTTP application outcome → Result<T>
Unexpected or technical failure → exception
```

| Classification | Examples | Representation |
|---|---|---|
| Expected application outcome | Primitive validation, missing/foreign resource, invalid nested relationship, invalid state, business conflict | `Result<T>.Failure` |
| Technical failure | Database, storage, provider/network, transaction, CAP publishing | Exception |
| Isolation/security invariant | Trusted identity mismatch, corrupt object ownership, invalid vector identity | `IsolationViolationException` |
| Cancellation | Request or provider cancellation | `OperationCanceledException` |
| Programming/unexpected state | Defects and impossible state | Exception |

No arbitrary exception is caught and converted to a Result.

## Migrated-handler inventory

| Message | Success response | Previous expected exceptions | Unexpected/invariant exceptions retained | HTTP success | HTTP failure | Isolation implication | Migration |
|---|---|---|---|---:|---|---|---|
| `UploadDocumentCommand` | `UploadDocumentResponse` | Request validation | Storage, DB, transaction, CAP, cancellation, object-key and trusted-context isolation | 201 | 400 | Trusted tenant/user and canonical returned storage key remain mandatory | Complete |
| `DeleteDocumentCommand` | `DeleteDocumentResponse` | Validation, not found, processing conflict | DB/transaction/storage, cancellation, tenant/object-key isolation | 204 | 400/404/409 | Missing and foreign lookup are the same 404; keys validated before mutation | Complete |
| `ReprocessDocumentCommand` | `ReprocessDocumentResponse` | Validation, not found, deleted/processing/artifact conflicts | DB/transaction/CAP, cancellation, tenant/object-key isolation | 202 | 400/404/409 | No event or mutation follows rejection; lookup remains tenant scoped | Complete |
| `ListDocumentsQuery` | `ListDocumentsResponse` | Pagination validation | Repository/database and cancellation | 200 | 400 | Repository and enrichment counts remain tenant scoped | Complete |
| `GetDocumentDetailQuery` | `GetDocumentDetailResponse` | Validation, not found | Repository/database, cancellation, nested/persisted identity isolation | 200 | 400/404 | Foreign and missing document are indistinguishable | Complete |
| `GetDocumentStatusQuery` | `GetDocumentStatusResponse` | Validation, not found | Repository/database, cancellation, nested run/step identity isolation | 200 | 400/404 | All document/version/run/step reads retain the trusted tenant | Complete |
| `GetMarkdownArtifactQuery` | Markdown text | Validation, not found | Storage, cancellation, object-key isolation | 200 | 400/404 | Missing version/artifact and foreign resource share one 404 | Complete |
| `GetJsonArtifactQuery` | JSON text | Validation, not found | Storage, cancellation, object-key isolation | 200 | 400/404 | Missing version/artifact and foreign resource share one 404 | Complete |
| `ListDocumentChunksQuery` | `ListDocumentChunksResponse` | Validation, not found | Repository/database, cancellation, returned-identity isolation | 200 | 400/404 | Full tenant/document/version relationship is required | Complete |
| `GetDocumentChunkQuery` | `GetDocumentChunkResponse` | Validation, not found | Repository/database, cancellation, returned-identity isolation | 200 | 400/404 | Invalid nested combinations use the generic 404 | Complete |
| `GetDocumentIntelligenceQuery` | `DocumentIntelligenceResponse` | Validation, not found | Repository/database, cancellation, returned-identity isolation | 200 | 400/404 | Document, version, and intelligence identities are revalidated | Complete |
| `AskQuestionQuery` | `AskQuestionResponse` | Validation, missing/foreign filter | Embedding/vector/chat/provider, cancellation, trusted-tenant and vector-result isolation | 200 | 400/404 | Filter authorization precedes embedding; invalid results fail before chat | Complete |

`GetProvidersDiagnosticsQuery` intentionally remains exception-based. `PreprocessDocumentCommand`, `ChunkDocumentCommand`, `GenerateIntelligenceCommand`, and `GenerateEmbeddingsCommand` remain exception-based Worker messages.

## Stable external errors

The shared error types are `Validation`, `NotFound`, and `Conflict`. Representative codes include:

```text
request.document_id_required
request.version_id_required
request.chunk_id_required
request.page_number_invalid
request.page_size_invalid
request.question_required
request.top_k_invalid
request.reprocess_stage_required
request.document_filter_invalid
resource.not_found
document.processing
document.deleted
processing.invalid_state
```

The API maps them to the existing Problem Details families:

| Error type | Status | Problem type | Title |
|---|---:|---|---|
| Validation | 400 | `https://openrag.dev/problems/request-validation` | The request is invalid. |
| NotFound | 404 | `https://openrag.dev/problems/resource-not-found` | Resource not found. |
| Conflict | 409 | `https://openrag.dev/problems/resource-conflict` | The request conflicts with the resource state. |

Problem bodies add an `errors` array with stable code, safe message, and optional target. They retain the trace ID. The internal Result wrapper and semantic enum are not serialized.

## HTTP compatibility matrix

| Endpoint | Previous success | New success | Previous expected failure | New expected failure | Problem type/title changed? | Public body change? |
|---|---:|---:|---|---|---|---|
| `GET /api/documents` | 200 | 200 | 400 | 400 | No | Stable error codes added on failure |
| `POST /api/documents/upload` | 201 | 201 | 400 | 400 | No | Stable error codes added on failure |
| `GET /api/documents/{id}/status` | 200 | 200 | 400/404 | 400/404 | No | Stable error codes added on failure |
| `POST /api/documents/{id}/reprocess` | 202 | 202 | 400/404/409 | 400/404/409 | No | Stable error codes added on failure |
| `GET /api/documents/{id}` | 200 | 200 | 400/404 | 400/404 | No | Stable error codes added on failure |
| `DELETE /api/documents/{id}` | 204 | 204 | 400/404/409 | 400/404/409 | No | Stable error codes added on failure |
| Markdown artifact | 200 text | 200 text | 400/404 | 400/404 | No | Stable error codes added on failure |
| JSON artifact | 200 JSON | 200 JSON | 400/404 | 400/404 | No | Stable error codes added on failure |
| List chunks | 200 | 200 | 400/404 | 400/404 | No | Stable error codes added on failure |
| Get chunk | 200 | 200 | 400/404 | 400/404 | No | Stable error codes added on failure |
| Document intelligence | 200 | 200 | 400/404 | 400/404 | No | Stable error codes added on failure |
| `POST /api/rag/ask` | 200 | 200 | 400/404 | 400/404 | No | Stable error codes added on failure |

Expected status semantics and Problem Details type/title semantics are preserved. Authentication 401 and authorization 403 remain middleware/policy responses. Isolation and unexpected failures remain generic 500 responses. OpenAPI describes public success payloads and Problem Details, never `Result<T>`.

## Telemetry

```text
successful Result → openrag.message.outcome=success, Activity Ok
failed Result     → openrag.message.outcome=rejected, stable error type/code
exception         → openrag.message.outcome=error, Activity Error
cancellation      → openrag.message.outcome=cancelled
```

Error messages, user questions, document content, prompts, vectors, storage paths, and tenant ownership details are not telemetry tags.

## Worker and CAP compatibility

Worker validation still throws `RequestValidationException`. Worker commands do not return Result and CAP consumers have not been changed. Retryable infrastructure, unexpected, and cancellation failures still escape the consumer. Existing idempotent no-op and deliberately persisted terminal-failure responses remain unchanged. No Result failure can make a failed Worker operation complete normally and be acknowledged.

See [ADR 0005](adr/0005-use-a-hybrid-result-model-for-expected-api-outcomes.md) for the decision and rollback boundary.
