# 15 — JWT Authentication

## Scope and trust boundary

OpenRAG uses the ASP.NET Core JWT Bearer handler for authenticated API access. Every route under `/api` requires the `AuthenticatedUser` policy. `GET /api/system/providers` also requires the `Administrator` policy because it exposes internal provider diagnostics.

OpenRAG is a resource server only. It does not implement users, passwords, login, registration, token issuance, refresh tokens, or authentication cookies. An external standards-compliant identity provider authenticates users and issues signed access tokens; OpenRAG validates those tokens.

> **Authentication is implemented, but tenant identity is not yet trusted.** `DevelopmentCurrentTenant` and request-supplied RAG tenant selection remain temporary P0.3 blockers. The system is not production-safe for multitenant use.

P0.2 does not read `tenant_id` from JWT claims. It does not change the `TenantId` field currently accepted by `/api/rag/ask`, add tenant query filters, add document ACLs, or change background-event tenant propagation.

## Authentication and authorization flow

1. The client sends an access token in `Authorization: Bearer <token>`.
2. The JWT Bearer handler obtains signing metadata from the configured Authority and validates the token issuer, audience, signing key, signature, lifetime, and expiration.
3. Inbound claim mapping is disabled, so configured claim names remain predictable.
4. `AuthenticatedUser` requires an authenticated principal with exactly one configured user-ID claim containing a non-empty GUID.
5. `HttpContextCurrentUser` exposes that validated GUID through `ICurrentUser`.
6. `Administrator` also requires the configured role claim to contain `admin`.

Tokens are accepted only from the Authorization header. OpenRAG does not read access tokens from query strings or cookies and does not log raw tokens, signing keys, or authentication secrets.

## Configuration contract

Configuration section: `Authentication:Jwt`.

| Setting | Required | Default | Contract |
|---|---:|---|---|
| `Authority` | Yes | none | Absolute identity-provider URI; must use HTTPS when HTTPS metadata is required |
| `Audience` | Yes | none | Expected access-token audience |
| `RequireHttpsMetadata` | No | `true` | Keep `true` in production; `false` is only for isolated local development with an HTTP metadata endpoint |
| `UserIdClaimType` | No | `sub` | Exactly one claim with this name must contain a non-empty GUID |
| `RoleClaimType` | No | `role` | Claim used by role-based policies |
| `ClockSkewSeconds` | No | `60` | Allowed clock skew from 0 through 300 seconds |

Configuration is validated during startup with `IValidateOptions<T>`. The API fails fast when required values are absent, the Authority is not absolute, HTTPS policy is violated, claim names are blank, or clock skew is outside the allowed range.

Environment-variable names:

```text
Authentication__Jwt__Authority
Authentication__Jwt__Audience
Authentication__Jwt__RequireHttpsMetadata
Authentication__Jwt__UserIdClaimType
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
| Tenant ID constant | `tenant_id` | Reserved for P0.3; not consumed as trusted tenant identity in P0.2 |
| Role | `role` | Used by ASP.NET Core role evaluation |
| Administrator role | `admin` | Required for provider diagnostics |
| Authenticated policy | `AuthenticatedUser` | Applied to the `/api` route group |
| Administrator policy | `Administrator` | Added to `GET /api/system/providers` |

Missing, duplicate, malformed, or empty user-ID claims are never converted to `Guid.Empty`. Authorization denies the request, and direct `ICurrentUser.UserId` access without a valid authenticated context throws clearly.

## Endpoint behavior

| Request | Result |
|---|---:|
| Missing bearer token on `/api/*` | `401 Unauthorized` with `WWW-Authenticate: Bearer` |
| Malformed, expired, wrongly issued, wrongly targeted, or incorrectly signed token | `401 Unauthorized` with `WWW-Authenticate: Bearer` |
| Authenticated token without exactly one valid GUID user-ID claim | `403 Forbidden` |
| Authenticated non-admin token on `/api/system/providers` | `403 Forbidden` |
| Authenticated admin token with a valid GUID user ID | Endpoint executes |

The generated OpenAPI document defines a `Bearer` HTTP security scheme with JWT format and attaches it to protected `/api` operations. `GET /openapi/v1.json` is intentionally anonymous only in the Development environment and is not mapped in other environments.

Example request using a token acquired outside OpenRAG:

```bash
curl --fail-with-body \
  -H "Authorization: Bearer $TOKEN" \
  https://localhost:7063/api/documents
```

Never paste the value of `$TOKEN` into documentation, logs, issues, or pull-request descriptions. Redact authorization values as `Bearer <redacted>`.

## Testing

Integration tests exercise the real JWT Bearer handler with an ephemeral test-only signing key and in-memory authority metadata. Coverage includes missing and malformed tokens, invalid signatures, wrong issuers and audiences, expired tokens, user-ID claim failures, administrator policy behavior, endpoint authorization metadata, current-user mapping, configuration validation, and OpenAPI security metadata. The key is generated at test runtime and is not reusable configuration.

## Remaining P0.3 work

P0.3 must replace `DevelopmentCurrentTenant`, derive tenant identity from validated claims or server-side mappings, remove request-controlled tenant overrides, and preserve trusted tenant context in background events. Until those changes and their negative tests are complete, OpenRAG must not be deployed as a production multitenant system.
