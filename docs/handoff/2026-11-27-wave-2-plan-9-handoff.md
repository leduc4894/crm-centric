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

## Hosted CI results checked

User asked to check completed CI. Run
https://github.com/leduc4894/crm-centric/actions/runs/46027476925 attempt 1
failed on PR head `1cdf1c878b1e840ec63f0eda768731fab8aa35a5`, actual merge
`648ce2fbcc3044d08006e96ab6e990932139680f`, base
`db04aeaecd7f4b6d083335a71418788d3c4561ca`. Compose PASS 18/18, zero skips.
Contract unittest suite PASS 137/137, zero skips; contracts-sdk job then failed
at `contract-compatibility` (`CI_COMMAND_FAILED`, correlation
`aafdb77d-8fd4-4039-9a51-8961975917cc`), without a more specific safe reason in
its log. Web failed at compatibility `CONTRACT_POLICY_REVIEW_REQUIRED`
(correlation `eb5570d0-1003-4f98-b967-c075a6e69a8d`), before browser suite
results. Backend failed during dotnet tests; safe output says only
`OTHER_TESTS_FAILED` / `OTHER` (correlation
`60aef185-a366-4f6d-8cd3-720785460748`); completion summary has zero test
counts and does not identify the failed test, so root cause remains unknown.
Security failed `CI_SCAN_FINDINGS` with 105 blocked Gitleaks records and one
blocked Trivy record (correlation `28e1fff4-cc8d-4b81-a3d5-161ca2bfdb46`).
Aggregate failed `CI_REPORT_INVALID` (correlation
`5d667397-aaf7-415d-a6a7-41c81ab38e8f`). Prior run 46026514267 was cancelled;
no cause or actor was investigated. Safe artifacts downloaded to
`.local/plan9-run-46027476925/` and GitHub retains them temporarily. No tests,
fixes, CI runs, policy changes or pushes were performed during this check.

Next: systematically identify backend failure with safe diagnostics and focused
local tests; independently triage security findings and prepare exact-candidate
compatibility review, without weakening gates. Group all actionable fixes and
verify locally before pushing once, as user requested; then run hosted CI for the
new candidate. `tests/CI` hosted coverage, benchmark and Wave 2/P0 exit remain
open.

## Backend diagnosis and grouped candidate

User asked to fix non-security CI, push and monitor; aggregate must still fail if
security fails. Focused PostgreSQL test run across tenant/contact/tag/field/
timeline schema classes: 49 passed, five `ContactEmailSchemaTests` failed, zero
skipped. All five failed during setup: latest `DatabaseRoleGrants.ApplyAsync`
requires newer tag, field and timeline schema, but the email migration fixture
intentionally starts at `20261127000000_ContactFoundation`. Commit `d8fc9eb`
grants only the historical contact access needed by that fixture, without
changing application grants. Focused `dotnet test ... --filter
'FullyQualifiedName~ContactEmailSchemaTests'` then passed 5/5, zero skips.
This reproduces an actual defect but the previous hosted output did not identify
its failing class, so it is not proof that it was the only backend failure.

Commit `23fc3e3` extends bounded diagnostics with fixed integration-class
identifiers; no raw test text is emitted. New diagnostic test failed before the
change and the focused `python3 -m unittest discover -s tests/CI -p
test_job_processes.py -v` passed 22/22, zero skips. `git diff --check` passed.
Actual-base compatibility locally returned `CONTRACT_POLICY_REVIEW_REQUIRED`,
correlation `65620b5e-5844-4377-89f3-8fce76e26b08`; preflight stopped before
semantic comparisons. No security policy or compatibility approval was changed;
previous candidate-specific approval does not transfer. A fresh candidate-specific
maintainer decision and actual-base evaluation are still required, and cannot be
self-approved by the implementer. Full test suite remains reserved for hosted CI.

## Hosted run 46031836021 and provisional compatibility preparation

Run https://github.com/leduc4894/crm-centric/actions/runs/46031836021 attempt 1
finished FAILURE on head `71959b35c08738d16a03e1f4f54e4f0b21075ce0`, actual
merge `7dee3cfa002e64d75f00e0d88aee292938462d43`, base
`db04aeaecd7f4b6d083335a71418788d3c4561ca`. Backend PASS 423/423 (396
integration, 27 Local), Compose PASS 18/18, all zero skips. Contract unittest
suite PASS 137/137 zero skips, but job failed later at compatibility. Web failed
`CONTRACT_POLICY_REVIEW_REQUIRED` before browser tests (correlation
`f6b0db28-1a30-41cc-a76b-3bdefc748b1f`); security and aggregate FAIL. Artifacts
are `.local/plan9-run-46031836021/`; job summaries apply only to this merge.

User explicitly authorized *preparing* an evaluation and presenting its results,
not activating an approval. A disposable clone `.local/plan9-evaluation-spike`
uses the existing scoped evaluator with candidate/tree constants altered only in
that clone. On the exact head and base it followed the evaluator's simulated
reviewed-breaking branch (correlation `e3f69d3e-e311-476d-8b37-f1a95c4588c3`):
TypeScript and .NET SDK surfaces both incompatible, both historical consumers and
packaged contact consumer checks passed. This simulation does not constitute an
actual gate verdict, independent review, acceptance, or authorization to deploy.
Review package: [candidate review](../engineering/17-WAVE-2-CANDIDATE-REVIEW-PACKAGE.md).
No real gate or trusted baseline was changed; no approval self-issued. Before
activation, require review and explicit approval for an exact scoped mechanism,
preserve the old approval scope, negative cases and semantic checks, and rerun CI
on the new candidate. The browser suite has not run on the latest candidate;
`tests/CI` hosted coverage and benchmark remain open. Full suite local was not run.

## Security delta and candidate scope work

User asked whether security introduced new findings. Compared exact hosted runs
`46027476925` and `46031836021`: Trivy had the same nine finding IDs and
verdicts (one blocked); Gitleaks had 105 blocked in each, with seven IDs added
and seven removed. Reproduced all 105 IDs for both exact merge trees using pinned
Gitleaks 8.30.1 and the same policy. Compared rule, path and SHA-256 of the
unredacted finding value solely in process memory; all seven changed-ID records
matched one-to-one, zero unmatched or ambiguous. No secrets, raw scanner reports
or fingerprints were printed or saved. The changed opaque IDs reflect record
metadata changes; within this comparison no new security finding was found.
This is not clearance: 105 secret findings and one vulnerability remain blocked;
a later candidate must be scanned independently.

User asked to continue after the candidate review package. Preparing a separate
candidate-owned scope for product tree `71959b35c08738d16a03e1f4f54e4f0b21075ce0`,
base `db04aeaecd7f4b6d083335a71418788d3c4561ca`, retaining original Plan 3 scope
and expiry, exact tree and coverage checks, archive verification and semantic
comparison/consumer checks. This is not independent enforcement. New focused
scope tests initially errored for missing implementation, then passed alongside
old scope tests 16/16 with zero skips; the real actual-base check is still
running at this checkpoint. Security verdict remains FAIL; `tests/CI` hosted
coverage, benchmark and P0 exit remain open. See next checkpoint for exact
commands, gate outcome, candidate and CI results.

Candidate-scoped implementation check: actual-base
`python3 scripts/contracts/compatibility.py --base-ref
db04aeaecd7f4b6d083335a71418788d3c4561ca --mvp-reviewed-policy` in the prepared
worktree returned `CONTRACT_BREAKING_REVIEWED`, correlation
`670a3090-61b4-471b-a248-22be8a86b132`; both SDK surfaces incompatible, both
historical consumers and packaged contact consumers passed. This is local,
candidate-owned, not independent enforcement. New scope tests pass 6/6; combined
old/new scope tests passed 16/16 before adding the final preflight case. A first
archive test failed because it included the tool-created `.git` and `.local`
entries; corrected the test to examine the raw archive before materialization,
then it passed. No external reviewer was dispatched; user instructions permit
only one coding agent. Hosted CI and browser checks still need the new candidate.
