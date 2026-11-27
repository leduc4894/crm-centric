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

## Frontend query test root cause

Run 46043088359's bounded diagnostic identified
`src/contacts/contactQueries.test.tsx` as the frontend failure (correlation
`b818ea17-03ea-4a7d-b061-80a1bd318b45`). Focused Vitest reproduced 1/5 failed
with a missing RouterProvider and a React Router null `isServer` error. The test
fixture now renders ContactList inside a real TanStack RouterProvider and stubs
jsdom scrolling; no application code changed. Focused rerun passed 5/5 with no
skips or scroll warning; web typecheck and targeted ESLint passed. The exact
changed test fixture is separately hash-pinned in the candidate scope, alongside
the already pinned diagnostic files; scope tests reject tampering in all three.
Existing required Plan 3 review test (with eight nested Plan 9 scope checks)
passed 11/11 with zero skips. Actual-base comparison after the test-only delta
and the current GitHub jobs are still running at this checkpoint; don't start a
second CI until the current run completes.

Security on run 46043088359: 105 blocked Gitleaks and one blocked Trivy,
unchanged counts. Compared with run 46038766142, all 14 changed Gitleaks opaque
IDs matched one-to-one on rule, file and in-memory-only value fingerprint;
Trivy IDs/verdicts unchanged, zero new unmatched findings. This comparison is
not a waiver or security clearance.

Run 46043088359 completed: backend and Compose passed; contracts-sdk
passed compatibility and aggregate-reporting, then failed `plan3-clean-export`
(correlation `bc2148aa-db8c-4059-aec7-ffc9e3c5ab49`); nested reason was not
projected in that run. Clean export runs the same frontend aggregate, so the
RouterProvider fixture fix may resolve it, but that is not yet proven. Added a
bounded nested diagnostic for clean-export without emitting payloads; the new
case failed before implementation and `test_job_processes.py` passed 24/24
with zero skips after. The scoped test/diagnostic files are pinned by exact hash;
Plan 3 review tests (including eight nested Plan 9 regressions) passed 11/11.
The router/query/scope Vitest subset passed 15/15; typecheck and focused ESLint
passed. A first aggregate gate run returned reviewed-breaking while diagnostics
were subsequently refined, so a fresh actual-base run is in progress for the
final exact hashes. No new CI has started yet.

Final grouped actual-base comparison, after the clean-export diagnostic hash was
updated, returned `CONTRACT_BREAKING_REVIEWED`, correlation
`b2cdbd98-ebbd-4367-a104-523978e1caa9`; both historical consumer checks and
packaged contact checks passed while the two SDK surface breaks remain explicit.
Candidate-owned, not independent enforcement. Ready for one hosted rerun.

## Leak scan diagnosis

Run 46047196348 web advanced past frontend tests and failed
`frontend-leak-scan` (correlation `0a984e96-4a64-4ba8-ae99-d470b975ebdf`).
Focused leak scan reproduced one `browser:document: absolute-checkout-path`
finding. Safe DOM inspection found it only in Vite's `STYLE` element
`data-vite-dev-id` for the app's `shell.css`. Moved that existing stylesheet
from the App JS import into a link in `index.html`, preserving the callback
prelude and same-origin CSS. The leak scan then passed over seven artifact files,
browser and process; after a production build it passed again. Browser smoke
confirmed one loaded stylesheet with 48 rules and the app shell present; the
production build succeeded, and focused query/list/router Vitest passed 16/16.
The exact two frontend files and the updated scope helper are hash-pinned in the
candidate-owned review scope; scope tests passed 11/11 including the eight nested
cases. This changes frontend asset loading, so it is a new candidate requiring
hosted verification and review; not a security waiver or independent review.
Actual-base comparison and the current GitHub run were still running at this
checkpoint. Security comparison for this run: both had 105 blocked Gitleaks,
one blocked Trivy; all 15 changed secret IDs matched prior findings one-to-one
by rule/file/value fingerprint in memory, Trivy unchanged, no new unmatched
finding. No values or fingerprints were saved or printed.

Actual-base gate after stylesheet changes returned `CONTRACT_BREAKING_REVIEWED`,
correlation `025645d3-36e3-47e0-a7b6-11336f78cf03`; both historical consumers
and contact consumer checks passed, both SDK surface breaks remain reported.
Run 46047196348 completed: backend and Compose PASS; web failed leak scan;
contracts-sdk progressed through compatibility and its clean export failed at
that same `frontend-leak-scan` (correlation
`b1804856-43df-4e7a-902d-3d8a1175602b`); security and aggregate FAIL. Focused
scan, build, browser stylesheet smoke and related tests passed as recorded above.
The frontend CSS loading change and exact pinned scope update are ready to push
as a single candidate. Hosted results and any new security delta remain pending.

## Run 46049997742 and browser follow-up diagnosis

Run https://github.com/leduc4894/crm-centric/actions/runs/46049997742 completed
on head `beab93443f46a53aac5fe4af041c0df69059411a`, merge
`d2bec0915032921412babd5df66c23466d22c294`: backend, Compose and contracts-sdk
PASS; contracts summary 137/137, zero skips, CI_JOB_COMPLETE. Web passed Plan 3
including frontend test and leak scan, then failed an unclassified follow-up
command (`CI_COMMAND_FAILED`, stage `other`, correlation
`62feb518-e42b-40c3-89f1-c121e77126d8`). Security and aggregate FAIL.
Real provider Playwright and contacts Playwright each passed 1/1 locally with
isolated real provider and also with the exact JSON reporter, zero skips/flaky;
callback cleanup passed 2/2, with the known non-fatal health proxy refusal warning.
Focused JSON Vitest reporter passed 5/5. No assertion that the CI failure was
transient or that the local passes explain it.

Prepared fixed safe stage diagnostics distinguishing provider, contacts and
Vitest, and safe allowlisted failed-file extraction from bounded job-owned Vitest
JSON reports; no raw assertion/title/payload is emitted, invalid paths and
symlinks are rejected. New tests failed before implementation; focused CI process
tests passed 26/26. Exact diagnostic/test hashes are pinned by the reviewed
candidate scope; 11 required review tests (including eight nested scope cases)
passed. An intermediate actual-base gate returned review-required due to stale
pin constants; pins were corrected and a final actual-base gate is running.
Next: wait for its result, then push one candidate and monitor without overlapping
CI. If web fails the new diagnostics should identify its follow-up stage/file.

Security: 105 blocked Gitleaks and one blocked Trivy again. Between exact merges
of 46047196348 and 46049997742 all 15 changed Gitleaks IDs matched previous
findings by rule, file and in-memory-only value fingerprint, with zero ambiguous
or unmatched; Trivy IDs/verdicts unchanged. No new security finding detected in
that exact comparison; no security clearance or waiver.

The final actual-base gate with the corrected diagnostic pins returned
`CONTRACT_BREAKING_REVIEWED`, correlation `dad5f770-ec19-42e8-be37-90ff1377c775`,
both historical and packaged contact consumer checks passed; both SDK surface
breaks remain reported. Focused Plan 3 scope tests passed 11/11 after the final
pins. Preparing one hosted rerun; the web follow-up stage remains undiagnosed on
GitHub until that run produces the bounded diagnostic. Candidate-owned review,
not independent enforcement.

## Duplicate Vitest identities

Run 46053384297 web advanced past its commands but failed
`CI_DISCOVERY_INCOMPLETE`, correlation `00970e98-83e9-43a9-879f-ee212b436126`.
A focused JSON reporter run across 12 Wave 2 contact/settings files reproduced
168 passing tests with two duplicate identities: the three parameter cases of
`crmClient.test.ts` shared one title. Named those cases by error type, preserving
all three assertions. Focused rerun: 168/168 passing, zero duplicate identities;
`crmClient.test.ts` alone passed 24/24, zero duplicate identities. The exact
changed test file is pinned in the candidate-owned scope, and the scope helper
checks tampering of every pinned delta; Plan 3 scope tests passed 11/11. No
coverage threshold was loosened. Actual-base compatibility and the current
hosted run remain in progress before the next grouped push.

Security on run 46053384297 versus 46049997742: 105 blocked Gitleaks and one
blocked Trivy in each; seven changed Gitleaks IDs all matched one-to-one on
rule/file/in-memory value fingerprint, zero ambiguous or unmatched; Trivy IDs
and verdicts unchanged. No secret values or fingerprints were saved or printed.
No new finding detected in that comparison; security remains FAIL.

Actual-base gate after the unique test-title delta returned
`CONTRACT_BREAKING_REVIEWED`, correlation `eb1eb8e1-32eb-49fd-a2f5-ff924b4abeee`;
both historical and packaged contact consumers passed, both SDK surface breaks
remain reported. Candidate-owned review, not independent enforcement. Await the
current hosted run's Compose/contracts results before the single grouped push.

Run 46053384297 contracts-sdk also failed clean-export at `frontend-test` in
`src/contacts/ContactTimeline.test.tsx` (correlation
`854fb5aa-fe95-4ad8-86da-9472bede4712`); this is separate from web's duplicate
Vitest IDs. Five isolated runs of that file passed; the CI test case and root
cause remain unknown, not labelled flaky. Added bounded diagnostics that may
emit only one of the fixed, reviewed timeline test titles if the failure recurs;
raw assertions and arbitrary titles are never emitted. New diagnostic test failed
before implementation; focused CI process suite passed 27/27 and Plan 3 scope
suite passed 11/11 including eight nested checks after exact diagnostic hashes
were updated. The new unique names in `crmClient.test.ts` are also exact-hash
pinned; 168 focused Vitest results have zero duplicate IDs and all passed.
Actual-base gate on these final pins is in progress; wait before pushing one
candidate. Earlier real provider and contacts tests passed with JSON reporter;
security remains FAIL, no waiver.

Final actual-base gate after the unique-title fix and bounded timeline diagnostic
returned `CONTRACT_BREAKING_REVIEWED`, correlation
`25cccc85-8b38-4940-9155-308e4c209841`; both historical consumers and packaged
contact checks passed, SDK surface breaks remain explicit. Focused web typecheck
and ESLint passed. Scope tests passed 11/11 (eight nested Plan 9 cases) and safe
CI process tests passed 27/27. Five isolated timeline runs passed; do not call
the earlier hosted failure resolved without further evidence. Ready for one
hosted candidate; security remains FAIL and user requested no overlapping runs.
