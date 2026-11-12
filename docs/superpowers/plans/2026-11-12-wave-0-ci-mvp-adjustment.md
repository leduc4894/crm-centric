# Wave 0 CI MVP Adjustment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. One coding agent at a time; reviewers read-only. Do not use a worktree.

**Goal:** Make local CI implement the agreed MVP scope while preserving honest security findings and evidence.

**Architecture:** Keep revision provenance, five named jobs and platform-result aggregation. Separate deferred test-image evidence from required test execution, and remove unconditional independent-trust exit; leave policy changes blocked by the existing policy-identity preflight. Required secret/dependency scanning remains fail-closed on scanner errors, unreviewed findings and incomplete relevant coverage. Do not create or approve exceptions. Remote evidence requires separate authorization.

**Tech Stack:** GitHub Actions pinned SHAs, Python 3.15.2, .NET 10, Node 24, Docker/PostgreSQL and existing pinned scanners.

**Spec:** `docs/engineering/09-WAVE-0-CI-MVP-SCOPE.md`.

## Global Constraints

- No credentials/tokens/raw payload in logs/artifacts; no PR secrets/write privileges or pull_request_target.
- Preserve exact event base/candidate validation, contract compatibility, required test identities/counts and platform `needs` checks.
- User has not approved a particular finding, exception, severity threshold or repository rules/settings change; never silently approve one.
- Policy transitions remain blocked without independently trusted verifier; no policy baseline bypass.
- Image/SBOM full enforcement and independent trusted workflow move to hardening, not false success or deletion of evidence.
- No push/merge/remote settings/deploy without separate authorization; P0/RT-* remain OPEN. Preserve unrelated untracked Plan 3 handoff.

## Review Focus

1. Deferred image scanning cannot skip required backend/Compose/browser tests or turn failure/skips into pass (Task 1).
2. Unchanged policy may run, changed policy still fails without independent approval (Task 1).
3. Secret/dependency finding, scanner failure, missing coverage or forged candidate approval remains non-success (Task 2).
4. Required job missing/cancelled/wrong revision and incomplete report remain non-success (Task 1).
5. Untrusted PR has read-only privileges and cannot leak diagnostics (Tasks 1–2).

### Task 1: Separate MVP execution from deferred image enforcement

**Files:** `.github/workflows/wave0.yml`, `scripts/ci/jobs.py`, `scripts/ci/summary.py`, `scripts/ci/aggregate.py`, test fixture image hooks and `tests/CI/test_workflow.py`, `test_summary.py`, `test_aggregate.py`, relevant Local/integration tests.

**Interfaces:** Preserve `RunIdentity`, `CiSummary`, five required jobs and their reporting. Image scans may run as optional hardening evidence but cannot be required for MVP test execution; do not claim image clearance. Existing contract policy preflight remains mandatory.

- [ ] Write negative/positive tests for absent deferred image evidence with complete suite passing, mandatory test fail/skip/missing/wrong-SHA failing, real test execution no scanner dependency, and unchanged vs changed policy fail-closed. Run focused tests RED.
- [ ] Implement explicit MVP mode in job/fixture/report path; remove unconditional `CI_TRUST_NOT_PROVISIONED` only for MVP aggregate, retain existing policy-transition blocker. Keep image hardening evidence optional and honestly marked not evaluated, not passed. No security privileges/caches added.
- [ ] Run focused tests, workflow lint and relevant real fixture tests; document limits and commit `ci: separate MVP gates from image hardening`.

### Task 2: Required scan evidence and triage

**Files:** `scripts/ci/security.py`, `scripts/ci/scan.py`, `scripts/ci/summary.py`, `tools/ci/`, `tests/CI/test_security_job.py`, `test_scan.py`, `tests/Security/`, evidence docs.

**Interfaces:** Security job produces run-bound safe summary; secret and dependency scan status is separate from deferred SAST/license/image/tool hardening coverage. Fail closed if required scanner fails, history is missing, relevant dependency ecosystem is not covered, or unreviewed finding blocks under existing policy.

- [ ] Test required scanner missing/stale/malformed, unverified secret, introduced-then-deleted secret, vulnerability, missing dependency coverage, and candidate-supplied untrusted waiver all fail; safe findings retain reason/correlation without raw output. Test deferred tool/license/SAST coverage reports OPEN rather than becoming a forged success. RED focused tests.
- [ ] Implement scoped required-scan path without blanket exception, suppression, weakened severity or modified baseline. Record classified digest findings and unresolved dependency/image/license findings separately; no finding is deemed approved by this task.
- [ ] Run focused/real scanner fixtures and exact candidate scan; document actual findings and blocking status, commit `ci: report MVP security scans and deferred coverage`.

### Task 3: Rehearsal and evidence

**Files:** `docs/engineering/08-WAVE-0-PLAN-4-VERIFICATION.md`, `docs/engineering/09-WAVE-0-CI-MVP-SCOPE.md`, `docs/handoff/`, plan status.

- [ ] Run full real local backend, Compose, contracts, browser and required security jobs on exact candidate, capturing counts and failures; do not replace failed/missing jobs with selective tests.
- [ ] Record SHA, commands, versions, findings, scope/triage and actual per-job status. Remote PR CI, protection and settings remain NOT RUN/UNKNOWN until separately authorized; do not mark Wave 0, P0 or RT-* PASS.
- [ ] Review entire diff; run appropriate full suite, `git diff --check` and document actual outcomes. Commit `docs: record MVP CI rehearsal and outstanding decisions`.
