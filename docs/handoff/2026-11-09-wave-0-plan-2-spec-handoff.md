# Wave 0 Plan 2 — spec handoff

## Objective and authorization
User requested continuing with the Plan 2 spec after Plan 1 was merged/pushed. This task writes design documentation only; no approval to implement Plan 2 or publish these new docs is inferred.

## Status and references
- Proposed [Plan 2 spec](../superpowers/specs/2026-11-09-wave-0-plan-2-local-stack-observability-design.md): local Compose stack, Keycloak, fake OIDC test fixture, structured logs/correlation/trace.
- Current branch `main`, initial HEAD `e624514`; working tree was clean at task start.
- Read [Plan 1 integration handoff](2026-11-09-wave-0-plan-1-integration-handoff.md), actual API/migrator/config/tests, roadmap, identity and operations docs.
- Plan 2 detailed topology is author proposal, not an already approved user design: separate CRM/Keycloak PostgreSQL containers, serialized bootstrap/migration, secrets lifecycle, loopback ports, no Wave 1 auth behavior.
- Builder review completed: no important unanswered questions preventing implementation planning. No builder-requested scope changes.

## Changed files and verification
- New spec and this handoff only. No application, dependency, Compose or container changes. Local documentation commit subject: `docs: specify Wave 0 Plan 2 local stack and observability`; resolve resulting SHA from history. No push performed.
- `git diff --check` and staged `git diff --cached --check`: PASS, including both new documents. No runtime test claim.
- Runtime/build/integration/Compose tests: NOT RUN; this task is design-only. Previous Plan 1 pass counts are historical, not Plan 2 evidence.
- Consulted official Docker, Keycloak and .NET documentation; references included in spec. Exact versions/digests remain builder choices requiring verification during implementation.

## Next steps
1. User reviews/approves spec; then write implementation plan with writing-plans skill.
2. Do not start application coding, Docker builds, dependency installs or Plan 2 deployment from this draft alone. Wave 0/P0 acceptance remains OPEN.
