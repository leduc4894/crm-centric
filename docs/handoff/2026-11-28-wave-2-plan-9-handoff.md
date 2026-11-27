# Wave 2 Plan 9 handoff — 2026-11-28

Continuation of [2026-11-27 handoff](2026-11-27-wave-2-plan-9-handoff.md),
[Plan 9](../superpowers/plans/2026-11-27-wave-2-plan-9-exit.md) and
[candidate review](../engineering/17-WAVE-2-CANDIDATE-REVIEW-PACKAGE.md).
Objective: user requested all CI jobs green except security, live monitoring,
and checking whether security findings are new. Preserve aggregate's real fail
when security fails; Wave 2/P0 are not declared complete.

On `wave-2`, latest pushed head `d7a6db11a24094f65b1ca93afc89423d249f03ad`,
run https://github.com/leduc4894/crm-centric/actions/runs/46036157557: web
failed policy review because CI creates `revision.json` in the root; local
checkout without that artifact passed. Exact hosted merge product tree matches
reviewed scope. Prepared a bounded fix validating an untracked regular
checkout-bound revision file and rejecting tracked, duplicate-key, forged and
symlink files. Focused old/new scope tests passed 18/18; actual-base gate with
revision file returned `CONTRACT_BREAKING_REVIEWED` (correlation
`a0fb46e5-c44a-4704-9c3e-fc9bfc139501`), with historical and packaged consumer
checks passed and SDK surface breaks explicitly reported. Scope is candidate-owned,
not independent enforcement. See previous handoff for detailed earlier evidence.

Security comparison: run 46036157557 versus 46031836021 has 105 blocked Gitleaks
findings in each, with 14 changed opaque IDs all matched one-to-one by rule, file
and secret fingerprint calculated only in process memory; no new unmatched
finding was found. Trivy finding IDs/verdicts were unchanged, one remains blocked.
No secret values or fingerprints were printed or saved. This is not security
clearance and must be rechecked on any later candidate.

At writing, run 46036157557 is still running backend/Compose; contracts and web
failed, security failed. The revision fix and handoff edits are uncommitted; do
not push while the previous run is active. Next: inspect completed run for any
additional failures, finish focused verification, commit grouped fixes, push
once, monitor CI to completion and compare new security findings. No full suite
was run locally, no waiver or deployment made. Update this handoff with actual
results and branch status at session end.

Run 46036157557 completed: backend PASS 423/423, Compose PASS 18/18;
contracts-sdk ran 143/143 contract tests but failed compatibility, web failed
compatibility, security failed and aggregate failed. Both suites had zero skips.
Safe summaries: `.local/plan9-run-46036157557-complete/`. Final focused command
`PYTHONPATH=tests/Contract python3 -m unittest test_wave2_plan9_review
test_wave2_plan3_review -v` passed 18/18 with zero skips; `git diff --check`
passed. Scope fix allows the one dated handoff path as governance documentation;
product, contract and CI inventory remain pinned. Actual-base gate with matching
revision returned reviewed-breaking as recorded above. Preparing a single push;
new candidate CI and security delta remain pending.

## Run 46038766142: frontend failure and bounded diagnosis

Run https://github.com/leduc4894/crm-centric/actions/runs/46038766142 head
`d9d6ea78bcb3fbb56b89dab4c22ae4824cdd4382` progressed past compatibility;
web failed in frontend-test (`PLAN3_FRONTEND_STEP_FAILED`, correlation
`0b812370-9b17-4155-a5c6-1f3d4d43f1fd`). Safe output does not identify the test.
Targeted nine-file contact/form Vitest suite passed 153/153; do not infer the
GitHub failure was the earlier boolean issue. Added a bounded diagnostic that
projects only fixed source-reviewed test file names from failing Vitest lines,
never test titles or assertion payloads; its focused test failed first, then
`test_job_processes.py` passed 23/23. No product behavior was changed.

To retain exact candidate-bound governance, this diagnostic-only delta to
`scripts/ci/jobs.py` and its focused CI test is separately pinned by exact
SHA-256 in the candidate scope; other files still match the approved product
tree and existing old scope remains unchanged. Tampering with the delta fails a
new test; new/old scope tests passed 19/19. Real actual-base evaluation with
checkout-bound revision and the pinned diagnostic delta returned
`CONTRACT_BREAKING_REVIEWED`, correlation `14638245-4deb-4d61-b3c9-a92f43e9c7e1`;
both historical consumer checks and packaged contact checks passed, while both
SDK surfaces remain incompatible. Candidate-owned, not independent enforcement.
Run 46038766142 was still active when written; wait for completion before one
new push. Next hosted run should identify the failing file if it recurs, and
must be followed by focused reproduction and a grouped fix; do not claim green.

Security on run 46038766142: same 105 blocked Gitleaks findings and one blocked
Trivy finding as previous run. All 15 changed Gitleaks IDs matched previous
findings one-to-one by rule, file and value fingerprint in memory; Trivy IDs and
verdicts did not change. No raw value/fingerprint was saved or printed. No new
finding was detected in this exact comparison; security remains FAIL.

The failed `plan3-aggregate-reporting` can be reproduced without running the full
local suite: an archive without `.git` running only `test_wave2_plan9_review`
returned `setUpClass` error and zero tests because the candidate Git object was
unavailable. That aggregate deliberately tests a gitless checkout. Rather than
change coverage policy or hide a prerequisite, moved the eight Plan 9 scope
regressions into a hash-pinned helper invoked by the existing required Plan 3
scope test. In a real checkout the nested suite must run eight tests with no
failures/errors/skips; in a gitless copy the existing named Plan 3 prerequisite
skip remains unchanged. Targeted gitless test passed with that named skip;
real `test_wave2_plan3_review` passed 11/11 (including eight nested checks),
`test_job_processes.py` passed 23/23 and `test_summary.py` passed 12/12. The full
local suite was not run. Actual-base compatibility after these changes remains
in progress at this checkpoint; CI push waits for its result.

After integration, actual-base compatibility with a checkout-bound revision
file returned `CONTRACT_BREAKING_REVIEWED`, correlation
`0ccc42e9-f697-4cf8-bf3e-70dd63458af5`; historical and packaged consumers
passed and both SDK surfaces remain reported incompatible. This is still
candidate-owned and not independent enforcement. Ready to commit and push one
candidate with the bounded diagnostics and gitless-simulation fixture repair;
hosted browser, aggregate harness, full suite and security remain to verify.
