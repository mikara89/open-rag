# Security Policy

## Supported development status

OpenRAG is under active development and is not production-ready. The `main` branch is the only maintained development line; no released version currently receives production security support or a guaranteed patch window.

## Reporting a vulnerability

Do not open a public issue, discussion, or pull request containing vulnerability details.

GitHub private vulnerability reporting is not enabled for this repository as of 2026-08-01, so the repository currently has no confirmed private reporting channel. Maintainers should enable private vulnerability reporting under **Settings → Security → Code security and analysis** before inviting public use. Once enabled, reporters should use **Security → Advisories → Report a vulnerability**.

Until a private channel is configured, contact the repository owner through a trusted private channel if one is already available to you. Do not send sensitive details through an unverified address or public forum.

Include, when safe:

- The affected commit, branch, or version.
- The affected endpoint, component, configuration, or dependency.
- Reproduction steps or a minimal proof of concept.
- Expected impact, prerequisites, and whether tenant boundaries are involved.
- Suggested mitigations or fixes, if known.
- A safe way for the maintainer to follow up.

Never include real secrets, API keys, access tokens, connection strings, customer documents, personal data, or unsanitized sensitive logs. Use synthetic data and redact identifiers while preserving the information needed to reproduce the issue.

## Current security limitations

- JWT Bearer authentication protects every `/api` endpoint. Validated tokens must contain exactly one non-empty GUID user-ID claim (`sub` by default).
- `GET /api/system/providers` additionally requires the administrator role (`admin` by default).
- The Development-only OpenAPI document remains anonymous at `/openapi/v1.json`; it is not mapped outside Development.
- Tenant resolution is still development-only and is not rooted in the authenticated principal. `DevelopmentCurrentTenant` remains registered, and `/api/rag/ask` still accepts request-supplied tenant selection.
- Document-level authorization and production-grade tenant isolation are not complete. Authentication must not be interpreted as production-safe multitenancy.
- The project has not completed production security hardening and must not be treated as production-ready.

Authentication configuration, claim contracts, 401/403 behavior, and secret-handling expectations are documented in [docs/15-authentication.md](docs/15-authentication.md). Never include raw bearer tokens, signing keys, or identity-provider secrets in issues, pull requests, logs, or committed settings.

## Response expectations

The maintainer will acknowledge and triage reports when practicable, may request additional sanitized information, and will coordinate remediation and disclosure based on severity and project capacity. No fixed acknowledgement or resolution SLA is promised while the project remains in development.
