# 16 — Trusted Tenant Resolution

## Trust boundary

OpenRAG resolves the tenant for every HTTP API request exclusively from the validated JWT principal. The default claim name is `tenant_id`; `Authentication:Jwt:TenantIdClaimType` selects a different claim name when required by the identity provider. This setting selects the claim name, never a tenant value.

The configured tenant claim must occur exactly once, parse as a GUID, and not equal `00000000-0000-0000-0000-000000000000`. Missing, blank, malformed, empty, duplicate-identical, and duplicate-conflicting claims fail authorization. The external identity provider is responsible for authenticating the subject and assigning a trustworthy tenant claim.

`HttpContextCurrentTenant` is registered only by the API and exposes the validated claim through `ICurrentTenant`. It does not inspect headers, query strings, route values, or request bodies. There is no development tenant or configuration fallback.

## HTTP behavior

Every `/api` endpoint requires both a valid user identity and a valid tenant identity. Invalid or missing bearer credentials produce `401 Unauthorized`; a cryptographically valid token that fails the user or tenant claim contract produces `403 Forbidden`. Administrator endpoints additionally require the configured `admin` role.

There is no tenant-selection header. In particular, `X-Tenant-Id` is unsupported. Request DTOs, query parameters, and route parameters do not select the tenant. Extra JSON properties follow the normal serializer behavior but cannot change the effective tenant.

The RAG request body contains no tenant ID:

```json
{
  "question": "What is this document about?",
  "documentIds": null,
  "topK": 5,
  "model": "mock-chat"
}
```

The token supplies the tenant. Upload, list, detail, status, artifact, chunk, intelligence, delete, reprocess, and RAG handlers obtain it from `ICurrentTenant`.

## Background processing

Workers have no HTTP context, fake principal, mutable global tenant, or ambient tenant service. Each CAP document event contains a non-nullable `Guid TenantId`, and every CAP-to-Mediator processing command copies that value unchanged. Preprocess, chunk, intelligence, and embedding commands reject `Guid.Empty`; their handlers use `command.TenantId` for tenant-scoped repositories, provider requests, persisted entities, storage operations, and downstream events.

The implemented chain preserves the original upload tenant:

```text
document.uploaded
→ document.preprocess.requested
→ document.preprocessed
→ document.chunking.requested
→ document.chunked
→ document.intelligence.requested/generated (when enabled)
→ document.embeddings.requested/generated
→ document.ready
```

Upload object keys remain `tenants/{tenantId}/documents/{documentId}/versions/{versionId}/...`, where the tenant comes from the authenticated principal.

## Configuration and local tokens

Environment-variable form:

```text
Authentication__Jwt__TenantIdClaimType=tenant_id
```

The default normally needs no override. Local and smoke-test access tokens must contain exactly one non-empty GUID user claim (`sub` by default) and exactly one non-empty GUID tenant claim (`tenant_id` by default). The full smoke test also calls provider diagnostics and therefore needs the `admin` role. Use `Bearer <redacted>` in logs and documentation.

Tenant validation cannot be disabled. Changing the claim type without updating the issuer causes otherwise valid tokens to receive `403 Forbidden`.

## Remaining security work

Trusted tenant resolution establishes where tenant identity comes from; it does not prove that every resource belongs to that tenant or grant access to it. P0.4 remains responsible for complete resource authorization and repository/storage/vector isolation auditing. P0.5 remains responsible for disposable, adversarial cross-tenant integration infrastructure and end-to-end denial tests.

Trusted tenant resolution is complete, but full resource authorization, repository/storage/vector isolation auditing, and adversarial cross-tenant integration testing remain P0.4 and P0.5 work. OpenRAG is not yet production-safe for multitenant deployment.
