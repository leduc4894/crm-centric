# Wave 0 — Plan 1 handoff

## Objective and status
Implement backend/PostgreSQL foundation per approved spec/plan. Tasks 1–5 implemented and task-reviewed; Task 6 smoke/docs implemented with local verification. Clean tracked-source export smoke passed; final controller review remains pending. Wave 0/P0 are OPEN.

## References and decisions
- Spec: [backend/PostgreSQL design](../superpowers/specs/2026-11-08-wave-0-plan-1-backend-postgresql-design.md).
- Plan: [implementation tasks](../superpowers/plans/2026-11-08-wave-0-plan-1-backend-postgresql.md).
- User agreed to divide Wave 0 into four plans and requested the Plan 1 spec, then requested its implementation plan.
- User explicitly authorized implementing the full plan with SDD, on a new branch in the existing checkout, without worktrees. One implementer at a time; read-only task and final reviewers.
- User requested adding the handoff rule to `AGENTS.md` before coding; this rule is now recorded there.

## Workspace
- Active branch: `feat/wave-0-plan-1-backend-postgresql`; implementation base `45141e8`.
- SDD recovery ledger (ignored): `.superpowers/sdd/2026-11-08-wave-0-plan-1-backend-postgresql/progress.md`. Read this and git history before resuming; do not repeat completed tasks.
- Tasks 1–5 implemented and task-reviewed: `71c9959`, `7f713dc`, `d320f06`, `91ef2ec`, `2f889bd`. Task 6 tested source: `bd16017ce6bebee98601c112037d61b03bd71572` (`docs(platform): record backend foundation verification`), based on `2f889bd`. The following `docs(platform): finalize clean-export smoke evidence` commit is docs-only; resolve final SHA via git history.
- SDK installed at `/tmp/crm-dotnet-10.0.403`; export DOTNET_ROOT and prepend it to PATH. Python tooling in `.venv`.
- Controller explicitly delegated the existing modified handoff to the Task 6 implementer, including it in the Task 6 commit. Existing full-plan SDD authorization is preserved. No merge, push or deployment.

## Verification
- Final working-source commands: `dotnet restore CrmPlatform.sln --locked-mode` PASS;
  `dotnet build CrmPlatform.sln -c Release --no-restore` PASS (0 warnings/errors);
  `dotnet test CrmPlatform.sln -c Release --no-build --no-restore` PASS 22/22,
  0 failed/skipped (45s). Real PostgreSQL; includes success/failure script process cleanup.
- `.venv/bin/python -m unittest discover -s tests/Contract -v` PASS 32/32 (1.642s).
  `bash -n scripts/backend-smoke.sh` and `git diff --check` PASS.
- Docker 29.9.3; SDK 10.0.403; PostgreSQL 18.6 pinned tag/digest. Full versions,
  source references, W0P1-01→10, historical RED/GREEN and rollback limitations:
  [verification](../engineering/05-WAVE-0-PLAN-1-VERIFICATION.md).
- Clean committed-source export: locked restore PASS (fresh NuGet directory),
  Release build PASS (0 warnings/errors), focused process smoke 2/2 PASS, zero
  skips (32s); final script hash matched committed source. See verification for
  exact export path/commands/source SHA. No remote CI,
  P0/Wave 0, tenant/business/OIDC/remote E2E/restore/DR success claimed.

## Task 6 changes and decisions
- New `scripts/backend-smoke.sh` and `tests/Integration/SmokeTests.cs`.
- Updated README, acceptance matrix, verification, spec status, plan execution
  choice/checklists and this handoff. No dependency/business contract changes.
- Caller provisions new dedicated DB/admin-owned baseline plus distinct non-owner
  migrator/app identities. Script never creates/drops DB/roles; fixture alone
  supplies disposable test provisioning. No concurrent migrators supported.
- Process output suppressed; credentials enter env only, never argv/logs. Smoke
  checks real live/ready HTTP 200 and trap cleanup. Failure gives reason/correlation.
- Down→Up and stop/start evidence are not production rollback/DR acceptance.

## Blockers and next steps
No current implementation blocker. Final read-only task/final reviewer steps are
controller-owned and pending; implementer self-review does not replace them.
1. Candidate and clean-export verification completed; evidence-only commit follows.
   No uncommitted runtime work remains; ignored task report lives in SDD ledger.
2. Controller reviews Task 6/final branch and updates final-review status/evidence.
   Continue later Wave 0 plans only under their scope; no merge/push/deploy here.
