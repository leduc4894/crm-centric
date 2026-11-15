# Wave 0 Plan 4 Task 8 handoff

## Objective, authority and current status

Consolidate Tasks1–7 evidence and W0P4-01→12, prepare local review/rollback package,
and inspect governance read-only. Parent authorized current branch local docs and
commit; no worktree/subagents/push/merge/deploy/settings/secrets changes or remote
workflow execution. Started clean on `feat/wave-0-plan-4-ci` at
`413601ea17bc6d4e34dc2ecdd41ff8f8b85a64dc`. Read relevant tracked handoffs, local
reports and full plan/spec; verified workspace history and actual normalized image
records. Applied verification-before-completion skill for documentation claims.

**Documentation complete locally; Task8 remote acceptance unfulfilled. Wave0/P0 and
all RT-* OPEN.** Independent trust and actual security findings remain blocking;
no approvals/exceptions/waivers added. No public contract/schema/database changes.

## Completed work and decisions

[Verification ledger](../engineering/08-WAVE-0-PLAN-4-VERIFICATION.md) maps all12 IDs
with full revision identities, actual counts/durations, historical versus current
scope, tool versions/checksum sources, upstream DB freshness limits and exact7a
image config IDs. It preserves original failed backend/Compose jobs, initial
image/host-library failures, security findings, coverage gaps and missing final-SHA
full suite. It does not relabel diagnostic browser/backend greens as job success.

Updated CI documentation, acceptance matrix, backlog, spec/plan execution status.
Unchecked mixed acceptance requirements remain unchecked. Proposed check names,
old contracts.yml migration, bounded timeouts, artifact retention, review diff,
rollback and concrete next steps are documented. Spec originally proposed design;
local implementation was subsequently authorized; no remote authority inferred.

Read-only GitHub via gh: repository leduc4894/crm-centric; main/protection endpoint
exit1 (raw body not published), rulesets and rules/branches/main returned[].
Governance/merge queue UNKNOWN/OPEN, never VERIFIED. No remote workflow triggered.

## Verification actually run for Task8

- `python3 -m scripts.ci.workflow`: PASS, workflow contract.
- `.local/ci-tools/actionlint .github/workflows/wave0.yml`: PASS, exit0.
- Python local-link existence check of changed Markdown plus new ledger: PASS.
- Python W0P4-ID inventory assertion: PASS12/12.
- `git diff --check`: PASS.
- Full CI, backend/Compose/Contract/browser/security, cold-start, remote PR/main/
  fork/cancellation, DemoModule/duplicate/isolation/outage/restore: NOT RUN in this
  documentation-only task. Prior results are attributed to their exact scopes.

Changed tracked files: docs/engineering/{02-REPO-CI-CD,03-ACCEPTANCE-MATRIX,
08-WAVE-0-PLAN-4-VERIFICATION}.md; docs/roadmap/01-BACKLOG.md; Plan4 spec/plan;
this handoff. Ignored local Task8 report contains execution details. Commit subject
`docs: record Wave 0 CI verification and handoff`; resolve exact resulting hash
from git history/report. No uncommitted implementation changes.

## Blockers and next steps

Remediate secret/license/vulnerability findings privately without broad baseline
or suppression. Complete tool/unlocked dependency coverage and unknown image
classification. Provision independently owned verifier/key/inventory/isolation
through separately authorized review. Diagnose original backend/Compose failures;
run full exact final candidate gates with fresh DBs and guarded artifacts. Prove
hosted provisioning, then obtain separate remote authorization and execute actual
PR/main/fork/job/workflow-cancel evidence; bind reviewed candidate/tree/merge method
to final main SHA. Inspect check contexts before any separately authorized rule
change. No closure until required evidence passes and is reviewed. P0 runtime
DemoModule/concurrent duplicates/tenant isolation/outage/recovery/restore remain.

Rollback: revert this documentation commit for Task8; no migration. Workflow/code
rollback requires scoped reverts and separately inspecting any required-check
context compatibility; do not reset DB volumes or disable checks to get green.
