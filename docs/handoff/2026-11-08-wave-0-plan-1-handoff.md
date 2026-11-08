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

## Final-review fix session (2026-11-08)

User explicitly authorized one final-review fix wave on the current feature
branch, no worktree/subagents, no merge/push/deploy. Read and checked this handoff,
the requirements spec, Task 6 brief and full ignored final-review report against
clean entry HEAD `2696930`.

Addressed F1 (listener identity) and F2 (bounded cleanup) in
`scripts/backend-smoke.sh`; added deterministic lifecycle regressions in
`tests/Integration/SmokeTests.cs`, updated Linux prerequisites/behavior in README
and appended current verification evidence. Prior export evidence is historical.
Controller owns final-review pending status and plan final checkbox; untouched.
Declined-to-judge review topics remain outside scope and P0 remains OPEN.

Observed RED: both new lifecycle cases fail against prior script (foreign
listener accepted; TERM-ignoring cleanup exceeds 35 seconds). Observed covering
GREEN: SmokeTests 4/4, no failures/skips. Contracts 32/32; shell syntax and diff
whitespace PASS. Full-suite and exact committed-source export results follow in
the evidence completion below; do not infer them from the earlier Task 6 run.

Final changed working-source checks: locked restore PASS; Release build PASS,
0 warnings/errors; full .NET suite PASS 24/24, zero failed/skipped (1m1s);
contracts PASS 32/32. Logs `/tmp/final-fix-{restore,build,full,contract}.log`.
Source fix commit follows this record. Next: export that exact commit, restore
locked/build/run covering SmokeTests there, verify matching final script hash,
then commit docs-only export evidence and return to controller. No runtime edits
are planned after the verified source commit; any such edit requires fresh checks.

### Fix-wave evidence completion

Source fix commit: `e54eb3e728eddad000f536cadaca47b5a71f2956`.
Clean export `/tmp/crm-final-fix-export.0DwEPq` of that exact commit, with fresh
NuGet cache, passed four locked project restores, Release build (0 warnings/errors)
and covering SmokeTests **4/4**, 0 failed/skipped (1m3s). Script SHA-256:
`471bae90aa92573bbad151045af4fc7ea45c6718fa8e5f1878378491d712cf3e`,
identical in committed source, working checkout and export. Exact commands/logs
are in the verification document's final script export section. Previous export
is historical; no full suite in export or remote CI claim.

This evidence-only commit changes verification and this handoff; no runtime/test
source changes after the verified source. Resolve final docs commit by subject
`docs(platform): record final smoke fix export evidence`. Ignored full report:
`.superpowers/sdd/2026-11-08-wave-0-plan-1-backend-postgresql/final-fix-report.md`.
No implementation blocker or uncommitted runtime work remains. Next action:
controller reviews F1/F2 disposition and owns final-review status/plan checkbox.
No merge/push/deploy; later waves and all P0 acceptance boundaries remain OPEN.
