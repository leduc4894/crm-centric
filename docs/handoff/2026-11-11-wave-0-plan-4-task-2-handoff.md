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
