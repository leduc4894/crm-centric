# Wave 0 Plan 4 Task 2 handoff

## Objective and authorization
Implement scoped policy-transition boundary under parent delegation, on `feat/wave-0-plan-4-ci`, one coding agent, no worktree or remote operations. Read AGENTS.md, Task 1 handoff and trust spec; checked clean workspace. References: [plan](../superpowers/plans/2026-11-11-wave-0-plan-4-ci-wave-exit.md), [spec](../superpowers/specs/2026-11-11-wave-0-plan-4-ci-wave-exit-design.md), [trust proposal](../ci/policy-transition-trust.md).

## Completed locally
- Immutable verified-transition snapshots; exact signed payload binding revision, tree, hashes, files, event/merge method, evidence references, reviewer and expiry. Safe errors include reason token/correlation ID.
- External trust-capability interface; default missing capability is BLOCKED. No candidate verifier/key discovery or CLI bypass.
- Separate policy identity preflight extracted from compatibility gate; existing CLI stays fail closed. Separate unprivileged dual evaluation API checks both policies against identical contract byte snapshots and retains both normalized outcomes. Arbitrary evaluator diagnostics are suppressed into a closed safe result.
- Eight focused tests cover valid approvals, first push, reverse rollback, forgery, waiver, missing capability, expired/wrong bindings/snapshots, preflight equality and either evaluator failing.

## Remaining work / decisions
Operational transition is BLOCKED. Signing algorithm/backend, key ownership, remote ruleset/required workflow, trusted object/snapshot adapter and actual dual-policy Plan 3 runner orchestration are OPEN and require separately approved provisioning. The local API is a proposal, not independent infrastructure. Tests use a synthetic signature fixture and evaluator functions; they do not prove real dual-policy semantic toolchain compatibility. No workflow can safely enable transitions merely by importing this candidate-controlled module. Full Task 2 integration remains incomplete until those trusted runners exist. No base or contract contents were changed. No public contract diff or data migration. Rollback is reverting the local commit.

## Changed files and state
`scripts/ci/policy_transition.py`, `scripts/contracts/compatibility.py`, `tests/CI/test_policy_transition.py`, trust proposal and this handoff. Commit subject: `feat(ci): verify reviewed policy transitions`; resolve hash from git log. Local detailed report under ignored `.superpowers/sdd/2026-11-11-wave-0-plan-4-ci-wave-exit/task-2-report.md`.

## Verification (actual)
- RED: focused tests against API stubs failed (7 tests, 10 NotImplementedError subcase errors); subsequent preflight test failed for missing API (8 tests, 1 import error).
- GREEN: `python3 -m unittest discover -s tests/CI -v`: PASS, 20 tests (8 transition, 12 revision).
- `python3 -m unittest discover -s tests/Local -v`: PASS, 20 tests.
- `python3 -m unittest discover -s tests/Contract -v`: FAIL, 59 tests, 15 failures, 2 import errors, 7 skips. Matches prior Task 1 failures: absent openapi_spec_validator and unusable node/toolchain; compatibility reaches SDK_TOOLCHAIN_REVIEW_REQUIRED and generation fixture setup fails. Full failing identities recorded in detailed report. No claim of regression suite passing.
- `python3 -m py_compile scripts/ci/policy_transition.py scripts/contracts/compatibility.py tests/CI/test_policy_transition.py`: PASS.
- `git diff --check`: PASS before commit.
- Remote CI, actual signed external transition, backend/browser/Compose, DemoModule duplicate/isolation/outage/recovery/restore: NOT RUN.

Next: review API and trust assumptions; provision toolchain to rerun full regressions; owner-approved independent verifier/snapshot/semantic runners before any production passing transition route. P0 and Wave 0 acceptance remain OPEN.

## Review fix round 1
Reviewer correctly identified that the initial implementation lacks a real dual-policy Plan 3 passing path and delegated snapshot provenance. Added `GitSnapshotCapability.from_git` to read policy/contract bytes and candidate tree directly from exact Git objects. It disables replacement objects/lazy fetch, captures diagnostics, bounds subprocess time, rejects symlink/non-blob entries and missing policy scope, and ignores dirty worktree files. Trust-anchor and complete policy inventory provisioning still require the independent coordinator; this local adapter is not itself remote enforcement. Added safe result evidence with both policy hashes and base/candidate contract hashes on success and closed evaluator paths.

TDD RED: focused tests reported two expected missing APIs (GitSnapshotCapability and old_policy_hashes), 10 tests total. GREEN: full CI suite PASS, 22 tests. New real temporary-Git test verifies committed old/new snapshots survive dirty worktree tampering, binds the resulting tree, and rejects missing/unsafe scope. Evidence test verifies success and incompatible results carry all four hash sets. Compile and diff checks PASS.

Finding 1 remains OPEN: no actual immutable dual-policy Plan 3 evaluator runner or positive real semantic integration test has been implemented. Plan 3's existing scripts hardcode their policy and preflight; bypassing that from candidate-controlled orchestration would contradict the trust requirement. The existing callback API is only an interface fixture until an approved independent runner exists. Operational transitions remain BLOCKED and no CLI bypass was added. No remote provisioning was attempted.

Fix-round regression results: `python3 -m unittest discover -s tests/Local -v` PASS, 20 tests; `python3 -m unittest discover -s tests/Contract -v` FAIL, 59 tests, 15 failures, 2 errors, 7 skips, matching the prior unprovisioned toolchain failures. Full names appended to local report. Commit subject `fix(ci): derive transition snapshots from Git objects`; exact SHA in git log. Actual independent dual-policy semantic and remote acceptance checks NOT RUN.

## Review fix round 2 implementation
Replaced the prior interface-only limitation with an actual local `Plan3SnapshotEvaluator` and separate `semantic_worker.py`. The worker imports the selected immutable snapshot's semantic evaluator and SDK checker, not candidate code in the verifier. Extracted `evaluate_contract_snapshots` from the ordinary compatibility gate; the existing CLI still runs its unchanged preflight first. The shared semantic path runs real schema/security/wire checks, pinned oasdiff, TypeScript/.NET generation/public-surface checks and historical consumer compilation. Baseline generator/consumer policy stays fixed; current policy is selected per evaluation. SDK preparation now includes historical consumer fixtures for non-Git byte snapshots.

The new integration fixture materializes the current reviewed evaluator/toolchain and canonical contracts into a real temporary Git base/head/merge graph, then obtains immutable policy/contract inputs via GitSnapshotCapability. It signs exact policy hash changes and tests compatible outcomes, removal of an existing canonical operation under both policies, and a missing actual tool asset on either evaluator. Callback-only tests remain narrow unit coverage; this is separate real-tool integration.

Independent production workflow/key/inventory/isolation provisioning remains OPEN, operational transitions BLOCKED, and CLI has no transition option. The subprocess worker is not an OS sandbox; production must place it in a disposable unprivileged job without verifier/signing credentials or secrets. No remote repository actions.

Test environment repair: `.venv` already contains validator dependencies. Pinned Node 24.22.2 initially could not start because host libatomic was missing. Extracted libatomic from the already available pinned .NET image into ignored `.local/plan3-runtime/`; used explicit LD_LIBRARY_PATH and pinned Node24 plus `.venv/bin` in PATH. No dependency or lockfile edits. First semantic integration run then found a missing isolated TypeScript scanner dependency root; fixed by installing the selected snapshot's pinned npm lock offline with scripts disabled. Intermediate integration tests correctly failed with CONTRACT_TOOL_ERROR; no success claimed for those runs.

Round 2 final verification (all commands completed):

- With the explicit pinned runtime environment above, `python3 -m unittest discover -s tests/CI -v`: PASS, 23 tests, 387.677s; latest signed Git-attested compatible/breaking fixtures and actual tools, zero skips.
- Same environment, `python3 -m unittest discover -s tests/Contract -p test_compatibility.py -v`: PASS, 39 tests, 1024.499s, zero skips. Includes ordinary CLI policy/waiver rejection, schema/security/wire changes, generator-only breaks and consumer failures. One run only; not repeated.
- Same environment, `python3 -m unittest discover -s tests/Contract -p test_sdk_surface.py -v`: PASS, 6 tests, 77.552s, zero skips.
- `python3 -m py_compile scripts/ci/policy_transition.py scripts/ci/semantic_worker.py scripts/contracts/compatibility.py scripts/contracts/sdk_surface.py tests/CI/test_policy_transition.py`: PASS.
- `git diff --check`: PASS before commit.
- Full Contract discovery and Local suite were not repeated in this round; earlier full Contract failures describe the prior system-Python/unusable-node environment, not these passing pinned-runtime targeted regressions. Backend/frontend/browser/Compose and remote acceptance drills NOT RUN.

The earlier finding that no real semantic runner exists is now addressed locally. Remaining blockers concern independent remote trust/key/ruleset/isolation provisioning only; no production passing transition route is enabled. Changed files in round 2: transition runner, new semantic worker, extracted compatibility semantic routine, SDK snapshot preparation, transition tests, trust document, this handoff. Commit subject `feat(ci): run real semantics from immutable policy snapshots`; exact hash in local git history/report. No public wire-contract/schema changes, database migrations or application runtime changes; rollback is revert of scoped commits. Next: parent review of actual runners and evidence, followed by separately authorized trust provisioning; P0/Wave 0 acceptance remains OPEN.
