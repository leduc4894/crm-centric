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

## Task 1 review fixes

The independent Task 1 review identified two Important findings: missing Python/toolchain prerequisites did not always produce safe structured errors, and provenance omitted locally referenced files. Regression tests first reproduced both failures. Both entrypoints now report missing PyYAML or required toolchain keys as `SDK_TOOLCHAIN_UNAVAILABLE` with correlation ID; provenance includes hashes for the validated transitive local reference closure. The changed-reference regression generates twice and verifies a nested input hash changes while unchanged inputs retain their hashes. These fixes require the SDD scoped re-review before Task 1 is marked complete; Tasks 2–7 remain planned.

## Task 2 implementation (pending review)

Task 1's two review fixes passed scoped re-review. Task 2 adds independent npm/NuGet packaged consumers, their lock and HTTP wire assertions under `tests/ClientConsumers/`, plus `tests/Contract/test_sdk_consumers.py`. Both SDK languages are tested through captured HTTP, including status/problem/correlation, ETag/If-Match, 412/428 no retry, encoding, cancellation, anonymous operations after a synthetic business bearer, and valid AutomationRule wire variants. Package audit checks for server/web references; canonical invalid-rule fixture remains separate. NuGet pack ZIP timestamps are normalized to permit repeatable locked package restoration; generated SDK files and contracts were not edited.

Verification: Node 24.22.2 and host libatomic workaround with `.venv/bin/python -m unittest discover -s tests/Contract -v` passed 42 tests in 148.030s, including packaged consumers. A standalone packaging run passed with locally installed artifacts and locked NuGet restore. A first attempt at a NuGet consumer lock failed NU1403 due to nondeterministic package timestamps; archive timestamp normalization followed by fresh locked restore passed. Task 2 requires independent SDD review before marking complete. Tasks 3–7, real browser and Wave 0/P0 acceptance remain open. Branch `feat/wave-0-plan-3-frontend-contract-toolchain`; Task 2 commit follows this update.

## Task 2 review remediation (in progress)

Task 2 independent review raised four important issues: null-field assertion accepted missing email, assertion diagnostics could reveal a synthetic bearer on failure, generated .NET health responses dropped content type, and generated mutation responses did not expose the correlation response header. The remediation adds explicit failure-path tests and a non-generated, versioned metadata-handler facade packaged with each C# SDK. Generator provenance includes the facade template. The fix commit and scoped review remain pending.

Review-fix verification: the 42-test contract suite passed in 143.261 seconds with the pinned Node and host libatomic workaround, including packaged-consumer tests and generated-check. The Task 2 fix commit follows this update; scoped independent review is still required before proceeding.
