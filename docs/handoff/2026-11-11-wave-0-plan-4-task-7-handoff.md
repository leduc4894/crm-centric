# Wave 0 Plan 4 Task 7 handoff

## Objective, authority and status
Implement required aggregate, real security entrypoint, isolated diagnostics and
negative/local rehearsal evidence. Parent assigned current checkout, local commits,
no worktree/subagents/remote changes. Starting clean at914f322 on branch
`feat/wave-0-plan-4-ci`; exact starting SHA verified in Task7 report. Read AGENTS,
Task7 brief, spec supply-chain/failure/acceptance sections and Task4–6 handoffs.
Using TDD and verification-before-completion skills. No new approvals, exceptions,
suppressions, remote writes, branch protection changes or independent trust.
Implementation is reviewable; Task7/Wave0/P0 acceptance remains BLOCKED.

References: [spec](../superpowers/specs/2026-11-11-wave-0-plan-4-ci-wave-exit-design.md),
[plan](../superpowers/plans/2026-11-11-wave-0-plan-4-ci-wave-exit.md),
[CI limits](../../tools/ci/README.md). Detailed ignored task report:
`.superpowers/sdd/2026-11-11-wave-0-plan-4-ci-wave-exit/task-7-report.md`.

## Changes
- Explicit five same-run named summary downloads; platform needs outcomes remain
  authoritative. Missing/skipped/cancelled/failed jobs and absent/duplicate/wrong
  identity/empty evidence fail. Whole workflow cancellation cannot manufacture a
  scheduled aggregate success; remote cancellation is not yet demonstrated.
- Completion helper captures setup/action/gate outcomes into safe diagnostic
  envelope; strips unknown output/payload, preserves only validated run-bound
  reports, fails if gate absent/skipped. Third-party missing reports never pass.
- Security placeholder replaced with actual repository Semgrep/Gitleaks/Trivy and
  guarded source inventory. Only complete pass scans plus inventory without gaps
  can succeed. Summary validator requires nonempty exact-run scan/source evidence.
- Dedicated pinned Python3.12.12 action for hash-locked Semgrep install, without
  altering pinned Python3.15.2 job runtime. Hosted availability unproven.
- Workflow lint rejects job privileges/environment/secrets overrides, repository
  secrets/PR expression interpolation, caches/unknown actions and broad artifact
  paths/retention. Only normalized reports and guarded source inventory publish.
- Existing unconditional independent trust block remains. Candidate-owned code
  and measured launcher hashes do not create independent authority.

Changed: `.github/workflows/wave0.yml`; scripts/ci/{completion,security,workflow,
summary}.py; tests/CI/{test_completion,test_security_job,test_workflow,
test_aggregate}.py; tools/ci/README.md; this handoff. No public API/schema/database
change. Rollback: revert Task7 commit; no data migration required.

## Actual verification checkpoint
- RED workflow9failures: eight unsafe mutations accepted and missing completion/
  security wiring. Subsequent RED: missing dedicated Semgrep runtime.
- RED completion missing module; later unsafe argument parser stderr.
- RED security missing module; later forged-green summaries with missing/zero/
  wrong-run scanner evidence accepted. Implemented then reran targeted tests.
- Latest focused system Python3.12 checks: workflow9 PASS, aggregate7 PASS,
  completion3 PASS, security3 PASS; all0skips. These are wrapper tests, not service
  acceptance or independent trust verification.
- `python3 -m scripts.ci.workflow`, `.local/ci-tools/actionlint .github/workflows/wave0.yml`,
  py_compile changed modules and `git diff --check`: PASS.
- Full pinned Python3.15.2 CI discovery RUNNING at checkpoint; result pending.
- Exact committed-SHA integration/security rehearsal PLANNED after code checkpoint
  commit, not yet run. Final results appended below; no pass claimed prematurely.

## Carried blockers / next steps
Task4 original backend failed CI_COMMAND_FAILED950.6699s and Compose failed
CI_COMMAND_FAILED2494.0634s, both0collected cases. Separate historical diagnostics
and old browser/contracts greens do not replace those failed jobs or prove this
SHA. Task5 real SAST/dependency/license/secret findings, Task6 images API/migrator/
Keycloak BLOCKED and PostgreSQL INCOMPLETE UNKNOWN remain. Source inventory has
10locks/444components and tool/unlocked coverage gaps; no scan clearance implied.
Fresh hosted scanner retrieval previously failed; hosted runtime/install remains
unproven. Independent trust CI_TRUST_NOT_PROVISIONED remains blocking. Remote PR,
main push, fork scheduler/cancel/setup/timeouts and enforcement NOT RUN. Merge
queue enablement UNKNOWN/OPEN; do not enable until merge_group event/revision
validation is implemented and reviewed. Remote DemoModule, duplicate/isolation,
outage/recovery and restore/P0 remain OPEN.

Next: finish full CI; commit code; rehearse actual scanners and real stack from
exact immutable source; retain reason/count/duration and distinguish failed gates
from diagnostic service checks. Review and fix findings without suppression;
provision independent verifier and conduct separately authorized remote runs.
