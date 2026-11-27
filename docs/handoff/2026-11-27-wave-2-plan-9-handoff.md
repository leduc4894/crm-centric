# Wave 2 Plan 9 handoff — 2026-11-27

Objective: user reported Plan 8 complete and asked for Plan 9 Wave 2 exit plan. Reviewed branch `wave-2` at `5a62017`, [Plan 8 handoff](2026-11-27-wave-2-plan-8-handoff.md), [Wave 2 design](../superpowers/specs/2026-11-27-wave-2-crm-contacts-design.md), acceptance matrix, actual workflow and job/coverage definitions. Plan 8 scoped review completed with an unexplained boolean test failure on an earlier concurrent run; rerun passed, root cause remains unproven. Full hosted suite and benchmark not run; actual-base compatibility review-required on last recorded candidate, security FAIL/deferred, Wave 2/P0 OPEN.

Status: [Plan 9](../superpowers/plans/2026-11-27-wave-2-plan-9-exit.md) drafted, not executed. It requires auditable GitHub CI full-suite discovery, real-provider browser, candidate-specific governance, 50k contacts/50 authenticated users benchmark with documented environment, and an evidence-based exit decision. The plan proposes a benchmark workload and requires environment availability; neither benchmark nor CI result is claimed. Full suite must run on GitHub CI; local tests only focused when fixing issues. No app code, workflow, contract or security policy changed; no CI, tests, benchmark, push or PR performed during authoring. Plan and this handoff are uncommitted on `wave-2`; whitespace/status checks performed before handoff.

Next: review plan; execute only when requested, preserving actual gate results, exact candidate/base identities, security deferral and P0 acceptance boundaries. No credentials or sensitive payloads are recorded here.

User requested committing the Plan 9 document and this handoff on `wave-2`. Staged whitespace verification and final status are checked with the commit. No implementation, test suite, CI, benchmark or PR ran in this documentation-only session; Plan 9 remains unexecuted.

## Execution started — preliminary CI and inventory

User requested implementation of Plan 9. Started with clean `wave-2` at
`e3876b57c8669d5bfeae302575fe287a0eb122f7`, merge base/main
`db04aeaecd7f4b6d083335a71418788d3c4561ca`. Pushed the previously absent
`origin/wave-2` and created draft PR #4 to trigger preliminary CI (no merge):
https://github.com/leduc4894/crm-centric/pull/4 . GitHub reported PR head
`e3876b57c8669d5bfeae302575fe287a0eb122f7`, base
`db04aeaecd7f4b6d083335a71418788d3c4561ca`, merge
`71bcb4b04341a6627ac174af41013f05a9ca7e10`; run
https://github.com/leduc4894/crm-centric/actions/runs/46022837693 was in progress
when last checked. No results, counts or tested checkout identity are claimed.
This is preliminary, not the final candidate; changes need a new run.

Created [preliminary exit inventory](../engineering/16-WAVE-2-EXIT-VERIFICATION.md).
`jobs.py` covers Local, Contract, integration category filters, Vitest and split
Playwright, but no workflow step invokes `tests/CI`; the required inventory lacks
explicit Wave 2 integration and boolean test IDs. Browser merging explicitly requires
the real-provider and contacts cases. Do not infer discovery or success from source
alone. Earlier boolean failure remains unexplained. Security remains last known
FAIL/deferred; actual-base governance and independent review remain open. No
benchmark harness, synthetic dataset, provisioned benchmark environment or benchmark
run exists from this session. No code or trust-policy changes made; no local tests or
full suite run. `gh pr view` initially requested unsupported field `baseRefOid` and
failed; `gh api` subsequently supplied base/head/merge. `git diff --check` is the
only local verification for this documentation checkpoint (see next entry for result).

Next: inspect completed run and sanitized artifacts, preserve failures, then implement
Task 1 CI discovery/inventory fail-closed coverage with targeted RED/GREEN and the
existing independent review path; implement Task 2 harness/tests and reproducible
benchmark environment, execute scenarios or document the actual environment blocker;
rerun hosted suite against the final candidate, obtain candidate-specific compatibility
review, and finish the acceptance/roadmap evidence and exit decision. Do not treat the
preliminary PR as final evidence or close Wave 2/P0.

## CI failure investigation and fixes

User requested CI fixes. Original PR run 46022837693 failed all six jobs; it
checked out merge `71bcb4b04341a6627ac174af41013f05a9ca7e10`, base
`db04aeaecd7f4b6d083335a71418788d3c4561ca`, head `e3876b5`. Safe summaries
are under `.local/plan9-run-46022837693/` (artifacts expire); backend and
Compose failed dotnet tests, web failed generated check, contract ran 137 tests
with 135 pass and two fail, security reported `CI_SCAN_FINDINGS` (105 blocked
secret records and one blocked vulnerability record), aggregate failed. No
security finding was waived or independently adjudicated.

`check.py` reproduced `SDK_GENERATED_MISMATCH`: only the configuration hash
in four SDK provenance files was stale following the Form lockfile change.
Generated the files and reran the check: `SDK_GENERATED_CURRENT` (PASS).
Commit `e61209a` updates those four files. The two failing contract tests
reproduced locally as `SDK_TOOLCHAIN_REVIEW_REQUIRED`; their historical
fixture omitted the consumer files required by the gate. Commit `4ffe1b1`
adds the real consumer fixture to both tests; focused unittest run passed 2/2
without skips after failing 2/2. Both commits were pushed with the earlier
inventory commit and triggered run
https://github.com/leduc4894/crm-centric/actions/runs/46026514267 on head
`4ffe1b17d9d0136d65a80b472405e806ab37982e`. At last inspection security
failed and web reached compatibility but failed `CONTRACT_POLICY_REVIEW_REQUIRED`
(correlation `e649e0c2-b7cd-41eb-80c2-a91c21cfdea7`); other jobs were still
running. Do not reuse prior approval or alter policy to force a pass.

Migration/bootstrap test assertions expected 5 or 7 migrations although Wave 2
adds 17 total. An initial focused run failed 6/18 using a stale `--no-build`
migrator, so that run is not candidate verification. After full solution build,
focused `dotnet test` filtering MigrationTests and LocalBootstrapTests passed
18/18, zero skips; commit `892d225` updates exact migration IDs and counts. No
full suite was run locally. Compose cold-stack targeted test ran against the
old built fixture and failed because it expected 5 and observed 17, confirming
the same defect in `ComposeFixture.Ready`; the focused green rerun is pending.
At this checkpoint only the Compose fixture change and this handoff are
uncommitted, `892d225` is ahead of origin; see subsequent checkpoint for final
status. Further work: finish the Compose focused rerun and commit/push if green;
review the new hosted run's safe artifacts and diagnose any remaining failures;
prepare actual-base independent contract review and triage scanner findings
without weakening gates. Plan 9 benchmark, complete hosted candidate suite and
exit decision remain open.

Compose focused verification completed: `dotnet build CrmPlatform.sln -c Release
--no-restore` succeeded, then `dotnet test tests/Integration/Integration.csproj
-c Release --no-build --no-restore --filter
'FullyQualifiedName~LocalStackTests.Cold_up_needs_no_host_sdk'` passed 1/1,
zero skips (73 seconds). The previous old-fixture run failed 1/1, expected 5,
actual 17. Commit `1cdf1c8` fixes the fixture; it and `892d225` were pushed to
`origin/wave-2`. This focused case does not establish that all Compose failures
are fixed. Hosted runs and security/governance results must still be inspected.
