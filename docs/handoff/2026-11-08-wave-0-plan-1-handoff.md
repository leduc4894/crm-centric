# Wave 0 — Plan 1 handoff

## Objective and status
All six tasks implemented on `feat/wave-0-plan-1-backend-postgresql` using SDD: one coding subagent at a time, read-only review after each task, whole-branch review and scoped fix re-review. Plan 1 local criteria W0P1-01–10 have evidence. Wave 0 as a whole and P0 remain OPEN.

## Authorization and references
- User explicitly authorized full implementation on a new branch in the existing checkout, without worktrees, using SDD. No further execution approval is needed for this completed plan.
- [Spec](../superpowers/specs/2026-11-08-wave-0-plan-1-backend-postgresql-design.md), [plan](../superpowers/plans/2026-11-08-wave-0-plan-1-backend-postgresql.md), [verification and limitations](../engineering/05-WAVE-0-PLAN-1-VERIFICATION.md).
- Implementation base `45141e8`; task commits `71c9959`, `7f713dc`, `d320f06`, `91ef2ec`, `2f889bd`, `bd16017`, `2696930`; final smoke fix `e54eb3e`, export evidence `32dace8`.
- Final closure commit is documentation-only, subject `docs(platform): close Plan 1 reviews and handoff`; resolve its SHA from git history. No merge, push or deployment performed.

## Delivered
- Pinned .NET backend solution, separate PostgreSQL migrator and restricted application identity; initial technical schema/history only.
- Anonymous `/health/live` and `/health/ready`, three-second readiness dependency budget, exact migration-set checking, safe Problem Details and server correlation.
- Real PostgreSQL Testcontainers coverage for migrations, role privileges, failures, non-destructive disposable rollback, outage/recovery, cancellation and secret-safe diagnostics.
- Independent operational OpenAPI and process smoke script. Script requires Python 3 and readable Linux `/proc`, verifies child listener ownership, and bounds shutdown using a three-second KILL watchdog.
- Updated README/provisioning guidance, acceptance matrix, evidence, plan and spec status. No business tables/endpoints, frontend, Compose or OIDC implementation.

## Final verification
Controller independently ran on `32dace8` (same runtime source as `e54eb3e`):
- `dotnet restore CrmPlatform.sln --locked-mode`: PASS.
- `dotnet build CrmPlatform.sln -c Release --no-restore`: PASS, zero warnings/errors.
- `dotnet test CrmPlatform.sln -c Release --no-build --no-restore`: PASS 24/24, zero failed/skipped, 1m1s.
- `.venv/bin/python -m unittest discover -s tests/Contract -v`: PASS 32/32, 1.790s.
- `bash -n scripts/backend-smoke.sh` and `git diff --check`: PASS.

Final clean-source export `e54eb3e` with fresh NuGet cache: locked restore/build PASS, focused SmokeTests 4/4 PASS (not a full-suite export run). Final script SHA-256 `471bae90aa92573bbad151045af4fc7ea45c6718fa8e5f1878378491d712cf3e` matches committed, working and exported files. Detailed commands, paths and historical RED/GREEN are in verification.

## Review disposition
- All six task reviews approved; whole-branch review found F1 (false smoke success on foreign healthy listener) and F2 (unbounded cleanup).
- Fix wave added two deterministic lifecycle regressions: both failed against prior script and passed with the fix. Existing process smoke cases still exercise real API/PostgreSQL.
- Scoped final re-review: F1 ADDRESSED, F2 ADDRESSED, no new breakage or open findings. Controller independently confirmed final full suite above.
- Tenant/OIDC/business authorization, events/duplicates/remote DemoModule, restore/DR, concurrent migrators/rolling compatibility, general ACL repair, frontend/Compose/SDK/remote CI/full telemetry and fresh advisory audit were explicitly outside this review's Plan 1 scope. Their gates remain open.

## Decisions recorded during execution
1. Existing checkout/new branch, no worktree: explicit user instruction overrides skill default. Cost if wrong: less isolation; tracked workspace started clean.
2. Agents inherited the session model: tool instructions disallow overriding model without user request. Cost if wrong: less model-tier cost optimization; review gates retained.
3. Use non-owner, non-superuser migrator with explicit database CREATE grant; administrator provisions dedicated DB and PUBLIC ACL baseline. Database owners retain CREATE despite REVOKE, so the original owner-based fixture could not prove DDL denial. Cost if wrong: provisioning/review rework; actual SQL privilege tests cover the boundary.
4. Virtual readiness check supports injecting unexpected errors in tests without a product debug endpoint. Cost if wrong: a small test seam to maintain.
5. Task 3 regressions were baseline GREEN on already-correct Task 2 behavior; no artificial RED was manufactured. Cost if wrong: missed defect; real DB assertions and independent review mitigate it.

## Environment and next steps
- SDK `/tmp/crm-dotnet-10.0.403` is temporary; set DOTNET_ROOT and prepend it to PATH or install matching pinned SDK. Contract Python tooling is in `.venv`; SDK 10.0.403, Docker 29.9.3, PostgreSQL 18.6 image/digest recorded in verification.
- Runtime/test source is committed; closure commit contains only docs. Use git status/history to verify workspace before continuing. SDD scratch may be removed after closure; this tracked handoff and verification are the durable recovery record.
- Keep branch for review/integration; no merge/push/deploy authorization inferred. Next development scope is Wave 0 Plan 2 (local stack and observability), with its own design/plan.
- No current Plan 1 blocker. Full Wave 0/P0, remote CI, remote DemoModule E2E, tenant isolation and DR acceptance are not claimed.
