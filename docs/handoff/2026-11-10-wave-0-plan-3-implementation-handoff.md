# Wave 0 Plan 3 — implementation handoff

## Objective and authority

Implement [Plan 3](../superpowers/plans/2026-11-10-wave-0-plan-3-frontend-contract-toolchain.md) under the [design](../superpowers/specs/2026-11-10-wave-0-plan-3-frontend-contract-toolchain-design.md), on a new branch without a worktree. The user authorized implementation; do not merge, push or deploy. One coding agent at a time. The prior specification handoff remains relevant: [Plan 3 spec handoff](2026-11-10-wave-0-plan-3-spec-handoff.md).

## Current state

Branch `feat/wave-0-plan-3-frontend-contract-toolchain`, base `5257abf`. Task 1 source and verification are being prepared for the plan's Task 1 commit; consult `git log`/`git status` for the current commit and any uncommitted work. Task 1 still requires the SDD read-only task review and any required fixes before it can be marked complete. Tasks 2–7 have not been implemented.

The generator selection, pinned artifacts, prerequisites, coverage and current evidence are in [Plan 3 verification](../engineering/07-WAVE-0-PLAN-3-VERIFICATION.md). Corvus.Json.Cli 5.8.8 supplies C# generated clients; openapi-typescript 7.13.0 plus openapi-fetch 0.17.0 supplies typed TS clients. Generated C# and TypeScript SDKs, provenance, lockfiles, generation/check entrypoints and Task 1 tests are in the pending changes. No canonical contract was changed.

Completed verification before handoff: pinned `npm ci` passed; both C# outputs built with locked restore and zero compiler warnings/errors; both TS packages built and strict rule fixture compiled; C# rule and TS HTTP boundary fixtures passed; a canonical validator accepted valid remote rule and rejected a missing action installation; a staged clean index export passed the generated-check. Focused tests observed RED then GREEN for tree/ref tampering, safe reference resolution, no bytecode mutation on check, and checkout-stable newlines. Final `.venv/bin/python -m unittest discover -s tests/Contract -v` passed 38 tests (69.850 s); `python3 scripts/contracts/check.py` returned `SDK_GENERATED_CURRENT`. Consult the verification document and SDD task report for exact evidence; do not infer Task 1 review or later tasks from these checks.

## Remaining work

1. Review Task 1 against its brief, fix any material findings with regression tests, and verify the generated SDKs and provenance from the commit.
2. Complete Task 2 independent packaged consumer and wire tests, then Tasks 3–7 in order. Task 3/4 compatibility and security gates, Task 5/6 frontend and browser behavior, and Task 7 aggregate/clean export are not complete.
3. Record updated evidence and handoff at the end of subsequent work. The operations health routes are implemented in the existing backend; the business OpenAPI is pre-runtime. Remote DemoModule E2E, tenant isolation, outage/recovery, restore drills, full Wave 0/P0 acceptance and remote CI remain open.
