# 13 — Documentation Review Checklist

Use this checklist during PR review. A PR does not need every documentation category, but the author and reviewer should decide explicitly which categories changed.

## Architecture docs

- [ ] New components, dependencies, boundaries, and data flows are reflected in the architecture overview and relevant ADRs.
- [ ] Implemented behavior is distinguished from future recommendations.
- [ ] Runtime topology and service-dependency diagrams remain accurate.

## Configuration docs

- [ ] New or changed settings include their path, type, default, valid values, and whether they are required.
- [ ] Provider-specific requirements and startup validation errors are documented.
- [ ] Local, CI, and production configuration differences are clear.

## API endpoint docs

- [ ] New or changed endpoints include method, route, request shape, response shape, and important status codes.
- [ ] Tenant and authorization behavior is described.
- [ ] Examples do not contain real credentials or sensitive data.

## Pipeline docs

- [ ] Event names, ordering, retry behavior, idempotency, and failure states match the implementation.
- [ ] Processing-status and reprocess behavior is updated when a stage changes.
- [ ] Data cleanup or regeneration requirements are called out.

## Smoke test docs

- [ ] The smoke script covers changed critical-path behavior, or a reason for omission is recorded.
- [ ] Prerequisites, provider modes, commands, expected results, and cleanup are current.
- [ ] Live dependencies are clearly separated from mock-only automated checks.

## Troubleshooting docs

- [ ] New failure modes include symptoms, likely causes, diagnostic steps, and recovery actions.
- [ ] Log and status fields referenced by the docs still exist.
- [ ] Destructive recovery commands include a data-loss warning and a safer alternative when available.

## Security and secrets docs

- [ ] Authentication, authorization, tenant isolation, input handling, or logging changes are documented.
- [ ] JWT Authority, Audience, claim types, role values, HTTPS metadata, and clock-skew defaults match the runtime configuration.
- [ ] Protected endpoints, intentionally anonymous endpoints, and administrator-only endpoints match endpoint metadata and OpenAPI output.
- [ ] 401 (authentication failure) and 403 (authenticated principal fails policy) behavior is documented accurately.
- [ ] Tenant claims are described as trusted identity only after validation (authenticated principal, configured claim name, exactly one non-empty GUID), and are not confused with P0.4 resource authorization.
- [ ] API examples contain no tenant-selection header, query parameter, route value, or request-body field.
- [ ] Worker documentation preserves tenant identity explicitly through CAP events and processing commands without ambient context.
- [ ] Secret names and injection methods are documented without including secret values.
- [ ] Logs, fixtures, examples, and screenshots contain no raw API keys, tokens, or personal data.

## Migration notes

- [ ] Database changes include forward-migration, compatibility, data-loss, rollback, and backup considerations.
- [ ] Generated EF migration and model snapshot changes were reviewed together.
- [ ] Reprocessing or backfill steps are explicit and testable.

## Production readiness notes

- [ ] The production-readiness roadmap reflects newly closed or discovered gaps.
- [ ] Operational dependencies, health checks, observability, backup/restore, and scaling impacts are covered.
- [ ] CI, deployment, and rollout instructions match the supported workflow.

## Review outcome

- [ ] Documentation updates are complete.
- [ ] No documentation change is needed; the PR explains why.
