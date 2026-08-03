# 17 — Authorization and Retrieval Isolation

## Authorization model

The tenant is the current resource-authorization boundary. Any authenticated user with a valid trusted tenant claim may operate on resources belonging to that tenant. `CreatedByUserId` is audit metadata only; it is not a document-owner ACL.

P0.4 is complete but does not add users, memberships, invitations, sharing, document ACLs, or per-user ownership restrictions. Provider diagnostics remains administrator-only. P0.4.1 added narrow host pipelines, and P0.4.2 added Results for expected HTTP outcomes; every resource authorization and isolation check documented here remains explicit in its handler, repository, storage, vector, or RAG boundary. P0.5 remains responsible for live adversarial tests using disposable PostgreSQL/pgvector, object storage, API, and Worker infrastructure.

## Denial contract

| Condition | HTTP result |
|---|---:|
| Missing or invalid JWT | 401 |
| Authenticated principal without exactly one valid user or tenant GUID claim | 403 |
| Missing administrator role for provider diagnostics | 403 |
| Malformed caller input | 400 Problem Details |
| Resource absent or outside the authenticated tenant | Identical 404 Problem Details |
| Valid request conflicts with resource state | 409 Problem Details |
| Persisted/provider isolation invariant fails | Generic 500 Problem Details |

Problem responses use stable `https://openrag.dev/problems/*` types, a trace ID, and safe application-error codes. Expected 400/404/409 responses originate from Result mapping rather than exception handling. They do not expose tenant ownership, object keys, SQL, stack traces, document content, prompts, or vectors. Normal 404 responses are telemetry `rejected` outcomes and are not logged as unexpected failures.

## Isolation inventory

Every `/api` operation inherits `AuthenticatedUser`, which requires both `ValidUserIdentityRequirement` and `ValidTenantIdentityRequirement`. Provider diagnostics additionally requires `Administrator`. Tenant identity comes only from `ICurrentTenant`, backed by the authenticated principal.

| Operation | Policy / tenant source | Repository and predicates | Storage / vector / provider boundary | Failure and focused proof |
|---|---|---|---|---|
| List documents | AuthenticatedUser / `ICurrentTenant` | `ListAsync(tenantId, ...)`; count and page share the tenant query | None | Tenant-only fake and handler tests |
| Upload | AuthenticatedUser / `ICurrentTenant` | New document/version use trusted tenant; repository validates child identity before tracking | Canonical source key built before `SaveAsync`; returned key must match | 400 input; invariant 500; upload key tests |
| Document detail | AuthenticatedUser / `ICurrentTenant` | `GetByIdWithVersionsAsync(tenantId, documentId)` | Returns artifact-presence flags, never keys | Missing/foreign generic 404 tests |
| Document status | AuthenticatedUser / `ICurrentTenant` | Tenant/document lookup plus tenant-scoped runs/steps | Returns presence/error flags, never keys or internal errors | Missing/foreign generic 404 tests |
| Delete | AuthenticatedUser / `ICurrentTenant` | Tenant-scoped tracking load includes versions; child cleanup uses tenant/document/version | All persisted source/Markdown/JSON keys validated before DB mutation; only validated keys deleted | Generic 404; 409 while processing; corrupt key blocks mutation/storage |
| Reprocess | AuthenticatedUser / `ICurrentTenant` | Tenant-scoped tracking document/version; existing generated data is preserved until worker replacement succeeds | Source/Markdown keys validated before event publication | Generic 404; invalid state 409; no selected stage is 400 before lookup/mutation; denial makes no event/storage call |
| Markdown artifact | AuthenticatedUser / `ICurrentTenant` | `GetVersionAsync(tenant, document, version)` | Markdown key validated before `OpenReadAsync` | Missing/foreign identical 404; corrupt key 500 without read |
| JSON artifact | AuthenticatedUser / `ICurrentTenant` | `GetVersionAsync(tenant, document, version)` | JSON key validated before `OpenReadAsync` | Missing/foreign identical 404; corrupt key blocked |
| List chunks | AuthenticatedUser / `ICurrentTenant` | Authorizes tenant/document/version, then `ListByVersionAsync` with same full identity for count/page | None | Mismatched nested identity returns generic 404 |
| Get chunk | AuthenticatedUser / `ICurrentTenant` | Tenant/document/version authorization then `GetByIdForVersionAsync(tenant, document, version, chunk)` | None | Mismatched combinations return generic 404 |
| Document intelligence | AuthenticatedUser / `ICurrentTenant` | Tenant/document/version authorization and intelligence lookup with full identity | No document content sent by read endpoint | Missing/foreign/mismatched generic 404 |
| RAG without `DocumentIds` | AuthenticatedUser / `ICurrentTenant` | No filter lookup; vector request always carries trusted tenant | Parameterized vector query joins document/version/chunk full identity; results revalidated before prompt | Foreign/invalid result causes generic 500 and no chat call |
| RAG with `DocumentIds` | AuthenticatedUser / `ICurrentTenant` | Deduplicate, cap at 100, then one `GetExistingIdsAsync(tenant, ids)` before embedding | Only fully authorized IDs reach vector query; every result must remain in filter | Any missing/foreign ID causes identical 404 before embedding/search/chat |
| Worker preprocessing | Command `TenantId` copied from CAP | Scoped version/document/run/step lookups; returned identities checked | Source key checked; preprocessor checks source and generates canonical artifact keys | Foreign scope is documented no-op; no storage/provider/save/event |
| Worker chunking | Command tenant | Scoped version/document/run/step/chunk/embedding operations; full identities checked; old embeddings/chunks replaced only after chunker success | Markdown/JSON keys checked before storage; chunks use command tenant | Foreign scope no-op; chunker failure preserves old retrieval rows and publishes no success event |
| Worker intelligence | Command tenant | Scoped run/document/version/step/intelligence operations; full identities checked | Artifact keys checked before reads; only validated content reaches provider | Foreign scope no-op; no storage/provider/persistence/event |
| Worker embeddings | Command tenant | Scoped run/document/version/chunks/embeddings; every chunk identity checked; old embeddings replaced only after all provider calls succeed | Only validated chunks reach embedding provider | Foreign scope no-op; partial provider failure preserves old embeddings and publishes no success event |

Intentional Worker “not found” outcomes are idempotent no-ops. They never retry with an unscoped lookup.

## Repository and database boundary

Tenant-owned read contracts accept `tenantId` explicitly. Version reads require tenant, document, and version; chunk reads require tenant, document, version, and chunk; processing reads require tenant plus the complete run relationship. No production code uses `FindAsync` or `IgnoreQueryFilters`. Application handlers do not access `AppDbContext`; raw SQL is allowlisted only in the audited vector service.

Add/range implementations reject empty or mixed tenant/document/version identity before EF tracks entities. The migration `EnforceTenantRelationshipIsolation` adds these database relationships:

- `Documents` alternate key `(TenantId, Id)`.
- `DocumentVersions` alternate key `(TenantId, DocumentId, Id)` and composite FK to documents.
- `DocumentChunks` alternate key `(TenantId, DocumentId, VersionId, Id)` and composite FK to versions.
- `DocumentEmbeddings` composite FK `(TenantId, DocumentId, VersionId, ChunkId)` to chunks.
- `DocumentIntelligence` and processing runs composite FKs to versions.
- Processing steps composite FK `(TenantId, DocumentId, VersionId, ProcessingRunId)` to runs.

Upgrade and rollback SQL were generated and inspected in both directions. Upgrade preserves valid rows but fails closed if inconsistent legacy relationships exist. PostgreSQL must validate constraints and build unique/supporting indexes, so the migration takes table/index locks; schedule a maintenance window for populated databases, preflight for orphaned/mismatched rows, back up first, and monitor lock duration. Rollback removes the composite constraints/indexes and restores the former document-ID-only version FK, weakening isolation.

## Object ownership boundary

Document storage keys use this ordinal canonical prefix:

```text
tenants/{tenantId:D}/documents/{documentId:D}/versions/{versionId:D}/
```

Source objects use `original/{safeFileName}`; generated artifacts use `docling/document.md` and `docling/document.json`. Empty IDs, absolute paths, backslashes, empty/`.`/`..` segments, wrong suffixes, and wrong tenant/document/version prefixes are rejected. Persisted keys are treated as untrusted data and validated before every document `SaveAsync`, `OpenReadAsync`, or `DeleteAsync` boundary.

## Vector and LLM boundary

The pgvector implementation uses `Database.SqlQuery` with `FormattableString`; Npgsql parameters carry tenant ID, document-ID array, provider, model, dimensions, embedding version, vector, deleted status, and limit. Total count, compatible count, and result retrieval share the same ownership joins and relevant predicates. The final chunk join matches tenant, document, version, and chunk, and owning document/version joins reject orphaned or deleted scope.

Vector results carry tenant, document, version, and chunk identity internally. Before building citations, retrieved DTOs, context, or the chat request, the RAG handler rejects foreign tenants, IDs outside an authorized filter, empty identifiers, and duplicate full identities. On violation it logs structured identifiers without content, throws `IsolationViolationException`, and never calls the chat model.

## Logging and remaining boundary

Security logs use structured trace/correlation and resource identifiers. They do not contain access tokens, signing keys, raw content, embedding vectors, complete prompts, response previews, or full storage keys. A foreign-resource request does not log the actual owner at normal information level.

P0.4 provides code-level, unit, architecture, model, and dependency-free integration proof. It does not claim real PostgreSQL pgvector execution or full cross-tenant live-system proof. Those scenarios remain P0.5, which is still planned; OpenRAG remains not production-ready.
