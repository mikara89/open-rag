# 15 — JWT Authentication

## Scope and trust boundary

OpenRAG uses the ASP.NET Core JWT Bearer handler for authenticated API access. Every route under `/api` requires the `AuthenticatedUser` policy. `GET /api/system/providers` also requires the `Administrator` policy because it exposes internal provider diagnostics.

OpenRAG is a resource server only. It does not implement users, passwords, login, registration, token issuance, refresh tokens, or authentication cookies. An external standards-compliant identity provider authenticates users and issues signed access tokens; OpenRAG validates those tokens.

P0.3 extends the authentication foundation with trusted tenant resolution. The API accepts tenant identity only from the validated principal, using `tenant_id` by default. It has no tenant-selection header, query parameter, route value, request-body field, configured tenant value, or development fallback. P0.4 uses that identity as the tenant-level resource boundary across repositories, storage, processing, vector retrieval, and RAG. See [trusted tenant resolution](16-trusted-tenant-resolution.md) and [authorization and retrieval isolation](17-authorization-and-isolation.md).

> The tenant is the current authorization boundary; `CreatedByUserId` remains audit metadata, not an ownership ACL. P0.5 live adversarial infrastructure remains planned, so OpenRAG is not yet production-safe for multitenant use.

## Authentication and authorization flow

1. The client sends an access token in `Authorization: Bearer <token>`.
2. The JWT Bearer handler obtains signing metadata from the configured Authority and validates the token issuer, audience, signing key, signature, lifetime, and expiration.
3. Inbound claim mapping is disabled, so configured claim names remain predictable.
4. `AuthenticatedUser` requires exactly one configured user-ID claim and one configured tenant-ID claim, each containing a non-empty GUID.
5. `HttpContextCurrentUser` exposes the user GUID through `ICurrentUser`; `HttpContextCurrentTenant` exposes the tenant GUID through `ICurrentTenant`.
6. `Administrator` independently requires authenticated JWT, valid user and tenant identities, and the configured `admin` role.

Tokens are accepted only from the Authorization header. OpenRAG does not read access tokens from query strings or cookies and does not log raw tokens, signing keys, or authentication secrets.

## Configuration contract

Configuration section: `Authentication:Jwt`.

| Setting | Required | Default | Contract |
|---|---:|---|---|
| `Authority` | Yes | none | Absolute HTTP(S) identity-provider URI; must use HTTPS when HTTPS metadata is required |
| `Audience` | Yes | none | Expected access-token audience |
| `RequireHttpsMetadata` | No | `true` | Keep `true` in production; `false` is only for isolated local development with an HTTP metadata endpoint |
| `UserIdClaimType` | No | `sub` | Exactly one claim with this name must contain a non-empty GUID |
| `TenantIdClaimType` | No | `tenant_id` | Exactly one claim with this name must contain a non-empty GUID |
| `RoleClaimType` | No | `role` | Claim used by role-based policies |
| `ClockSkewSeconds` | No | `60` | Allowed clock skew from 0 through 300 seconds |

`TenantIdClaimType` selects the claim name only; tenant values are assigned by the identity provider and carried in tokens. OpenRAG has no configured tenant value and no option to disable tenant validation.

Configuration is validated during startup with `IValidateOptions<T>`. The API fails fast when required values are absent, the Authority is not an absolute HTTP(S) URI, HTTPS policy is violated, claim names are blank, or clock skew is outside the allowed range. Schemes such as FTP, file, and LDAP are rejected even when HTTPS metadata is explicitly disabled.

Environment-variable names:

```text
Authentication__Jwt__Authority
Authentication__Jwt__Audience
Authentication__Jwt__RequireHttpsMetadata
Authentication__Jwt__UserIdClaimType
Authentication__Jwt__TenantIdClaimType
Authentication__Jwt__RoleClaimType
Authentication__Jwt__ClockSkewSeconds
```

For local development, API user secrets avoid putting identity-provider configuration in tracked files:

```powershell
dotnet user-secrets set "Authentication:Jwt:Authority" "https://idp.example.com" --project src/OpenRAG.Api
dotnet user-secrets set "Authentication:Jwt:Audience" "openrag-api" --project src/OpenRAG.Api
```

Do not commit a signing key, client secret, access token, or real identity-provider configuration. The identity provider owns signing-key protection and rotation. Its published metadata must expose the active verification keys; deployment operators are responsible for choosing a provider and rotation policy compatible with the API availability requirements.

## Claims and policies

| Contract | Default value | Behavior |
|---|---|---|
| User ID | `sub` | Must occur exactly once and parse as a non-empty GUID |
| Tenant ID | `tenant_id` | Must occur exactly once and parse as a non-empty GUID |
| Role | `role` | Used by ASP.NET Core role evaluation |
| Administrator role | `admin` | Required for provider diagnostics |
| Authenticated policy | `AuthenticatedUser` | Applied to the `/api` route group |
| Administrator policy | `Administrator` | Added to `GET /api/system/providers` |

Missing, blank, duplicate-identical, duplicate-conflicting, malformed, or empty user/tenant GUID claims are never converted to `Guid.Empty`. Authorization denies the request. Direct `ICurrentUser.UserId` or `ICurrentTenant.TenantId` access without the corresponding valid authenticated context throws clearly.

## Endpoint behavior

| Request | Result |
|---|---:|
| Missing bearer token on `/api/*` | `401 Unauthorized` with `WWW-Authenticate: Bearer` |
| Malformed, expired, wrongly issued, wrongly targeted, or incorrectly signed token | `401 Unauthorized` with `WWW-Authenticate: Bearer` |
| Authenticated token without exactly one valid GUID user-ID claim | `403 Forbidden` |
| Authenticated token without exactly one valid GUID tenant-ID claim | `403 Forbidden` |
| Authenticated non-admin token on `/api/system/providers` | `403 Forbidden` |
| Authenticated admin token with valid user and tenant GUIDs | Endpoint executes |

The generated OpenAPI document defines a `Bearer` HTTP security scheme with JWT format and attaches it to protected `/api` operations. `GET /openapi/v1.json` is intentionally anonymous only in the Development environment and is not mapped in other environments.

Example request using a token acquired outside OpenRAG:

```bash
curl --fail-with-body \
  -H "Authorization: Bearer $TOKEN" \
  https://localhost:7063/api/documents
```

Never paste the value of `$TOKEN` into documentation, logs, issues, or pull-request descriptions. Redact authorization values as `Bearer <redacted>`.

## Testing

Integration tests exercise the real JWT Bearer handler with an ephemeral test-only signing key and in-memory authority metadata. Coverage includes missing and malformed tokens, invalid signatures, wrong issuers and audiences, expired tokens, user/tenant claim failures, duplicate claims, custom tenant claim configuration, administrator policy behavior, endpoint authorization metadata, current-user/current-tenant mapping, spoofed header/query/body tenant inputs, configuration validation, and OpenAPI security metadata. The key is generated at test runtime and is not reusable configuration.

## Remaining authorization work

Trusted tenant resolution and P0.4 code-level resource isolation are complete. Per-user ACLs are not implemented. P0.5 still must prove cross-tenant denial through real PostgreSQL/pgvector, object storage, API requests, and Worker processing before production multitenant deployment.
