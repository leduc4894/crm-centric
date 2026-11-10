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

## Task 3 implementation (review pending)

Task 2 review fixes passed scoped independent review. Task 3 adds a local Git-base compatibility gate using pinned oasdiff 1.37.0, effective security/scope and webhook signature checks, conservative JSON Schema review detection, and mutation fixtures. Input snapshots and validated local references reject remote references; oasdiff runs in a digest-pinned container with `--network none --pull=never`. Added schemas require review until fixture validation can be demonstrated. See the Plan 3 verification document for artifact pins and limitations. Task 3 is pending its commit and independent SDD review; do not infer Task 4 SDK-surface enforcement, frontend, browser or P0 completion from it. Consult the Task 3 report and current Git state for actual verification and status.

Task 3 verification: final `PATH=<Node-24.22.2>/bin:$PATH LD_LIBRARY_PATH=/tmp/plan3-libs .venv/bin/python -m unittest discover -s tests/Contract -v` passed 62 tests in 165.137s. This includes real oasdiff fixtures, existing HMAC and contract checks, and local package consumers. `python3 scripts/contracts/compatibility.py --base-ref HEAD` passed against `7c7a972394ef41ae346c9dba91e8535e87fc8c6e` prior to commit. Task 3 commit follows this update; independent review is still required.

## Task 3 review remediation (pending scoped review)

Independent Task 3 review identified that referenced schema files outside the `*.schema.json` filename pattern were omitted from semantic comparison and hashes, referenced Path Items were omitted from effective security checks, and unsupported OpenAPI minor versions could pass unchanged. The fix hashes the full validated standalone-schema reference closure, compares referenced content conservatively (with schema annotations normalized where valid), resolves internal Path Item references, fails closed for external Path Item references, and restricts OpenAPI versions to supported 3.0/3.1 patches. Network isolation remains in place. Regression and full-suite results and the fix commit are recorded in the Plan 3 verification document and SDD task report; scoped review remains pending.

Task 3 review-fix final verification: `PATH=<Node-24.22.2>/bin:$PATH LD_LIBRARY_PATH=/tmp/plan3-libs .venv/bin/python -m unittest discover -s tests/Contract -v` passed 68 tests in 167.381s. The fix commit follows this update and requires scoped independent review before Task 3 is complete.

## Task 4 implementation (pending scoped review)

Task 3 passed scoped independent review. Task 4 adds separate base/current SDK regeneration from the resolved Git base and current contract snapshots. A base with an existing toolchain is regenerated using its own generator, configuration and pinned .NET image; a first bootstrap uses the selected current toolchain on both sides and reports `bootstrap-no-released-consumer`. Missing historical tooling, missing historical consumer artifacts and tool errors fail closed as `SDK_TOOLCHAIN_REVIEW_REQUIRED`. The gate compares TypeScript exported declarations and compiled .NET public types/members including nullability, and compiles the Git-base TS and C# consumer fixtures against candidate SDKs. Candidate changes to existing comparison policy or waivers require review. Reported toolchain hashes identify generator/configuration drift. Independent scoped review is still required before Task 4 is complete.

Changed files for Task 4: `scripts/contracts/compatibility.py` (baseline configuration and SDK-surface stage, reason classifications), `scripts/contracts/sdk_surface.py` (new: isolated base/candidate preparation, generation, surface comparison, baseline consumer compilation), `tools/contracts/surface/compare-typescript.cjs` and `tools/contracts/surface/dotnet/{Program.cs,Surface.csproj}` (new: public-surface comparators), `tests/Contract/test_sdk_surface.py` (new) and `tests/Contract/test_compatibility.py` (Task 4 regressions). The canonical contracts under `docs/contracts/` were not modified.

Task 4 final verification: `PATH=<Node-24.22.2>/bin:$PATH LD_LIBRARY_PATH=/tmp/plan3-libs .venv/bin/python -m unittest discover -s tests/Contract -v` exited 0 with 83 tests in 814.153s. `python3 scripts/contracts/compatibility.py --base-ref 817948f` exited 0 with `CONTRACT_COMPATIBLE`, `sdkBaseline: historical-toolchain` and `sdkToolchainChanged: false` in 2m13.682s. A temporary generator-only break was rejected with `SDK_SURFACE_INCOMPATIBLE` and `sdkToolchainChanged: true`, and a temporary .NET-only nullability break was rejected the same way, with the HTTP contract unchanged in both probes. Both probes were reverted; the worktree holds only the Task 4 changes.

Review remediation in this task: the closed SDK-toolchain error path dropped `sdkBaseline`/`sdkToolchain` from the report, and the new surface comparators emitted a reason token without a correlation ID. Both are fixed with regression cases (`test_missing_historical_consumer_artifacts_fail_closed`, `test_unreadable_declarations_report_reason_and_correlation`).

Known limitations: the gate proves public-surface compatibility and baseline-consumer compilation only, not runtime behaviour or release governance; the TypeScript comparison is conservative about additions that add a required member; the new comparison tooling under `tools/contracts/surface/` is only protected against candidate edits from the next commit onwards because it does not exist in base `817948f`.

## Task 4 review fix round 1 (pending scoped review)

Independent review of `6c7b801` returned no Critical finding and three Important findings, all fixed in a separate follow-up commit. The TypeScript comparator now has a closed error envelope (reason token plus correlation ID, no raw stack trace) and the gate distinguishes a candidate that declares no public surface (`SDK_SURFACE_INCOMPATIBLE`) from a tool failure (`SDK_TOOLCHAIN_REVIEW_REQUIRED`). The tautological generator-only case was renamed and tightened to assert the exact reason it can actually reach; generator-only break detection stays covered by the real-generator cases. The policy/waiver scan now covers `tools/contracts/`, `scripts/contracts/`, `docs/contracts/` and the repository root, including untracked files, without matching ordinary names such as `.gitignore`; editing `toolchain.json` or `generate.py` remains outside the policy-review path. A failed .NET baseline-consumer build is now separated into a genuine compile break and a restore/infrastructure failure. The bootstrap path now has a regression asserting one pinned configuration for both sides.

Changed files: `scripts/contracts/compatibility.py`, `scripts/contracts/sdk_surface.py`, `tools/contracts/surface/compare-typescript.cjs`, `tests/Contract/test_compatibility.py`, `tests/Contract/test_sdk_surface.py`, `docs/engineering/07-WAVE-0-PLAN-3-VERIFICATION.md` and this handoff. The canonical contracts were not modified.

Fix-round verification: `.venv/bin/python -m unittest tests.Contract.test_compatibility -v` exited 0 with 39 tests in 753.902s; the scanner suite together with the four policy-scan cases exited 0 with 10 tests in 35.480s; `python3 scripts/contracts/compatibility.py --base-ref 817948f` exited 0 with `CONTRACT_COMPATIBLE` in 2m35.527s. The full `tests/Contract` suite result for this fix round is recorded in the verification document and the SDD task report.

Task 5–7, frontend/browser tests, exact-SHA clean export, Wave 0/P0 and remote acceptance remain open. No merge, push or deploy authorized.
