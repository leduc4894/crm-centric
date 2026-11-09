# Wave 0 Plan 2 — spec and implementation-plan handoff

## Objective and authorization
User requested continuing with the Plan 2 spec after Plan 1 was merged/pushed. User subsequently requested the implementation plan. This work writes documentation only; implementation and publication of Plan 2 are not yet authorized.

## Status and references
- Proposed [Plan 2 spec](../superpowers/specs/2026-11-09-wave-0-plan-2-local-stack-observability-design.md): local Compose stack, Keycloak, fake OIDC test fixture, structured logs/correlation/trace.
- Current branch `main`; spec commit `71345f1`, planning began from that clean HEAD.
- New [implementation plan](../superpowers/plans/2026-11-09-wave-0-plan-2-local-stack-observability.md): eight sequential tasks covering logs/trace, fake OIDC, runtime images, secrets/bootstrap, Keycloak, Compose lifecycle, failure/isolation gates, and final clean-source evidence.
- Read [Plan 1 integration handoff](2026-11-09-wave-0-plan-1-integration-handoff.md), actual API/migrator/config/tests, roadmap, identity and operations docs.
- User requested moving from spec to plan; this is approval to prepare planning documentation. Detailed implementation choices are recorded in the plan, including: separate CRM/Keycloak PostgreSQL containers, serialized bootstrap/migration, secrets lifecycle, loopback ports, no Wave 1 auth behavior.
- Builder review completed: no important unanswered questions preventing implementation planning. No builder-requested scope changes.

## Changed files and verification
- Spec already committed as `71345f1`. Planning changes: new plan and this handoff only; no application/dependency/Compose/container changes. Local planning commit subject `docs: plan Wave 0 Plan 2 local stack and observability`; resolve resulting SHA from history. Neither documentation commit has been pushed in these tasks.
- `git diff --check` and staged `git diff --cached --check`: PASS for planning documentation, including the new plan. Self-review performed against spec coverage, interfaces, five Review Focus cases, and all W0P2-01 through W0P2-12. No runtime test claim.
- Runtime/build/integration/Compose tests: NOT RUN; this task is design-only. Previous Plan 1 pass counts are historical, not Plan 2 evidence.
- Consulted official Docker, Keycloak and .NET documentation; references included in spec. Exact versions/digests remain builder choices requiring verification during implementation.

## Next steps
1. User reviews plan and requests execution. Recommended method: sequential SDD as in Plan 1; execution method/branch choice must be preserved when supplied, without repeated confirmation between tasks.
2. Do not start application coding, Docker builds, dependency installs or Plan 2 deployment from these documents alone. Wave 0/P0 acceptance remains OPEN.
