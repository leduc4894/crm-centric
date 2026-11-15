# Wave 0 Plan 4 Task 4 handoff

## Objective and authority
Implement locked backend/Compose/contracts/browser CI jobs and safe evidence under
parent delegation. Current branch `feat/wave-0-plan-4-ci`; one coding agent, no
worktree/subagents or remote actions. Read Task4 brief/report, Task3 handoff and
verified clean workspace/process/artifact assumptions on resume.

References: [plan](../superpowers/plans/2026-11-11-wave-0-plan-4-ci-wave-exit.md),
[spec](../superpowers/specs/2026-11-11-wave-0-plan-4-ci-wave-exit-design.md),
[CI report/trust requirements](../../tools/ci/README.md).

## Completed implementation
Earlier Task4 commits: `4dd2894874acc5ebc7ce2a81edc7b602295b716b` initial jobs;
`a4ff22a` Python pin/trust blocker; `437e4ce4074532b5dd25309f776c93e86c681154`
exact harness binding/private normalization; `e186fb4b759e59c2f7e58e37a793ff580272ec3c`
browser OS provisioning separation. Current follow-up adds command-owned process
sessions, TERM/grace/KILL timeout/interruption cleanup, safe SIGINT/SIGTERM tokens,
failed native TRX collection, and fresh per-job Compose resource labels with
label-filtered, independently re-inspected cleanup including stopped containers.
Default local ownership is ineligible; no broad prune or project-prefix deletion.

Changed files in follow-up: scripts/ci/jobs.py, compose.yaml,
tests/CI/test_job_processes.py, tests/CI/test_job_resources.py, tools/ci/README.md,
this handoff.
No public contract, database migration or dependency lock changes. Rollback is
revert of this follow-up; previous failures and external trust blockers persist.
Commit is identifiable by subject `fix(ci): clean owned processes and Compose resources`;
its exact SHA is recorded in the ignored Task4 report after commit.

## Actual evidence
Exact e186 candidate, base437e4ce4074532b5dd25309f776c93e86c681154:
- Full CI: PASS49,0skips (1049.231s); original earlier session, not new follow-up.
- Browser actual stack/Playwright: PASS58=54Vitest+4browser,0fail/skip,1113.3042s.
- Contracts runner: PASS87,0fail/skip,3175.5879s. Both successful normalized job
  artifacts independently parse_report revalidated on resume against exact identity.
- Backend original runner: FAIL CI_COMMAND_FAILED,950.6699s,zero collected cases;
  separate private same-filter diagnostic PASS70/70,730.7874s,does not replace it.
- Compose original runner: FAIL CI_COMMAND_FAILED,2494.0634s,zero collected cases.
  Old producer discarded failing TRX; cause unproven, no raw logs exposed.
- No prior Python/dotnet/testhost job processes remain active on resume.

Follow-up verification:
- RED real TERM-ignoring descendant survived timeout; missing helper/label tests
  failed. Explicit non-job/local cleanup refusal also observed RED before fix.
- Required Python3.15.2 focused job tests: PASS12,0skips.
- Real disposable Docker checks: PASS owned stopped container/network/volume removed;
  foreign fixtures preserved, then separately cleaned by exact recorded identities.
- `dotnet build CrmPlatform.sln -c Release --no-restore`: PASS pinned10.0.403.
- `python -m unittest discover -s tests/Local -v`: PASS20 on existing venv; separate
  evidence, never called an original backend job pass.
- Workflow contract, actionlint, py_compile and git diff --check: PASS.
- Narrower required Python3.15.2 regression: PASS56,0skips; initial attempt had
  a test observer /proc disappearance race, fixed and rerun successfully.
- New full job rehearsals and full follow-up CI suite: NOT RUN; narrower regression
  excludes unchanged expensive dual-policy real semantic case and is documented
  in local Task4 report. e186 full49 remains historical evidence only.

## Limits and concrete next steps
Commit this follow-up then review; record exact final SHA and re-run actual jobs on
that candidate. Diagnose/reproduce backend and Compose failures using the new safe
failed-TRX retention. CI_TRUST_NOT_PROVISIONED independent verification, Task5
security, hosted setup and remote required-check/cancellation evidence remain OPEN.
Compose labels cover this manifest; Testcontainers retains its own Ryuk lifecycle.
SIGKILL cannot execute finalizers. No claim of Wave0/P0 completion or acceptance;
remote DemoModule, duplicates, isolation, outage/recovery/restore drills still required.
