# Wave 0 remote PR revision proof correction

Objective: diagnose and correct the immutable revision step in actual [PR1
run43448370765](https://github.com/leduc4894/crm-centric/actions/runs/43448370765).
User authorized branch publication and PR creation, without merge or repository
settings changes. Parent coordinates publication; this session does not push.
Relevant plan: [CI wave exit](../superpowers/plans/2026-11-11-wave-0-plan-4-ci-wave-exit.md).
Prior context: [actual remote base](2026-11-13-wave-0-remote-base-handoff.md).

Completed investigation: read-only `gh run view` confirms all five jobs fail in
Verify immutable event revisions, before provisioning or gate execution. Step
stdout is redirected to revision.json, so logs expose only exit1; no claim that
logs contained a specific revision reason. Read-only `gh api
repos/leduc4894/crm-centric/events` projected only PR action and revision metadata:
the opened event at15:01:21Z has merge_commit_sha null, base df993ae and head
a465e43. Checkout job log identifies synthetic merge0940aa2; current PR API later
reports that merge. Resolver called `_sha(None)` and failed closed with
CI_CANDIDATE_UNAVAILABLE; synthetic regression reproduces it. No raw payload,
credentials or tokens are recorded.

Implemented: when immutable PR payload has null/missing merge_commit_sha, use
Actions GITHUB_SHA as the candidate, require it equals actual checkout and prove
local commit objects plus exact ordered parents [event base,event head] and
base ancestry. Missing/malformed/mismatched Actions SHA still fails closed.
Non-null payload SHA remains authoritative. No API call, fetch, live-ref lookup,
event rewrite or base substitution is introduced. Reason tokens/correlation IDs
and public contracts remain unchanged. Rollback is a scoped revert; no migration.

Files: scripts/ci/revision.py, tests/CI/test_revision.py, this handoff. Branch
feat/wave-0-plan-4-ci, starting HEAD a465e43. Unrelated untracked
2026-11-12-wave-0-plan-3-resumption-handoff.md is preserved. Scoped implementation, regression tests and this handoff are committed together;
parent records the resulting commit identity.

Verification:
- `.venv/bin/python -m unittest discover -s tests/CI -p test_revision.py`:
  RED initially unsupported event_sha API, then reproduced two null/missing
  merge failures CI_CANDIDATE_UNAVAILABLE; GREEN15 tests in1.016s after fix; final focused run PASS16 in1.072s.
  Tests cover Actions SHA binding, absent/malformed/mismatched SHA, exact parents,
  CLI environment wiring, non-null merge SHA authority despite valid Actions SHA,
  and existing stale/wrong checkout/base rejection.
- `git diff --check`: PASS.
- Prepared-runtime `.local/ci-venv315/bin/python -m unittest discover -s tests/CI`:
  FAIL159,158 pass/1 fail in343.514s. The sole failure is
  test_mvp_history_review.HistoryReviewTests.test_real_pinned_history_exact60_and_tampering_fail_closed:
  expected60 records, observed64. This exact pre-existing history fixture failure
  is documented in the prior remote-base handoff. No scanner suppression or
  fixture change is included. The full run loaded15 revision tests; final focused
  run separately verifies all16 after adding the review-requested negative case.
  Runtime environment matches prepared PATH/LD_LIBRARY_PATH in prior handoff.

Next: parent review/publish under existing
user authorization, then inspect new actual remote CI. Original run remains
failed; no remote gate PASS, Wave0/P0 completion or acceptance drill claim.

## Second remote run: bounded diagnostics, no proof relaxation

Run [43453090132](https://github.com/leduc4894/crm-centric/actions/runs/43453090132)
also fails all five revision steps. Its run API head is153df32, base df993ae;
checkout log identifies merge1d67951, matching the current PR API merge. Existing
logs contain only step exit1, not the redirected revision.json reason or actual
Actions GITHUB_SHA. Actual synchronize event fields are not available from these
logs. This session does not claim a stale/non-null/null event merge diagnosis.

Correction to the first investigation's evidence characterization: repository
Events API nested PR data is NOT an immutable event snapshot. The same opened
15:01:21 event now reports head153df32 instead of the previously observed a465e43.
Its earlier null merge projection supported a hypothesis, not proof of the run's
exact event-file contents. The original synthetic null regression and guarded
fallback remain valid, but do not establish the exact remote failure cause.

User authorized iterative PR publication and CI through parent, without merge or
settings changes. Parent approved a diagnostic iteration on this evidence gap.
Starting HEAD153df32; no push by this coding session.

Implemented diagnostic iteration:
- Revision CLI opt-in --failure-diagnostics emits reason/correlation and seven
  bounded proof facts to stderr only on failure: valid/invalid/absent states for
  event merge, event base/head and Actions SHA; equality booleans for event merge
  versus checkout, Actions SHA versus checkout, and exact ordered event parents
  versus checkout. Unavailable facts are null. No raw event, commit, SHA, private
  field or Git diagnostic is printed. Existing failure stdout stays safe JSON.
- All six workflow revision invocations (five jobs plus aggregate) enable that
  diagnostic flag.
- Completion independently reads CI_STEPS before revision identity. Previously a
  missing identity raised KeyError and reset steps to {}, hiding revision failure
  as CI_STEP_NON_SUCCESS. It now preserves CI_REVISION_FAILED without inventing
  an identity or converting failure to success.
- Candidate acceptance and all existing provenance checks are unchanged.

Additional files changed: .github/workflows/wave0.yml, scripts/ci/completion.py,
tests/CI/test_completion.py; revision.py, test_revision.py and this handoff also
updated. Rollback: scoped revert; no schema/public contract/migration change.

Verification:
- RED: diagnostic test could not parse absent stderr; completion CLI test
  expected CI_REVISION_FAILED but observed CI_STEP_NON_SUCCESS.
- `.venv/bin/python -m unittest discover -s tests/CI -p test_revision.py`:
  PASS17 in1.469s, including synthetic private-sentinel exclusion and no SHA
  disclosure from the diagnostic facts.
- Corresponding test_completion.py: PASS9 in0.983s.
- Corresponding test_workflow.py: PASS9 in0.315s.
- Prepared runtime `.local/ci-venv315/bin/python -m unittest discover -s tests/CI`:
  PASS164 in367.905s. Runtime PATH/LD_LIBRARY_PATH matches prior prepared
  environment above. Earlier history fixture failure is absent on starting
  HEAD153df32; that fixture remediation belongs to preceding parent commits.
- `git diff --check`: PASS.

Next: parent reviews and publishes diagnostic commit, inspects actual failed proof
facts in the new run, then reproduces the identified cause before any further
candidate resolution change. No actual remote root cause is asserted beyond the
observed revision step failure; both remote runs remain failed.

## Third remote run: confirmed stale webhook merge metadata

Parent supplied and this session independently verified the bounded backend
log diagnostic from [run43454678387](https://github.com/leduc4894/crm-centric/actions/runs/43454678387):
CI_CANDIDATE_UNAVAILABLE; Actions SHA valid and equal to checkout; event base/head
valid; exact ordered checkout parents equal event base/head; event merge SHA valid
but unequal to checkout. This establishes the old resolver's failure mechanism:
non-null webhook merge metadata selected a different candidate despite the
run-specific Actions candidate having the correct event parent proof. No raw
payload or SHA was needed to diagnose this cause.

User authorized iterative PR fixes/CI; parent explicitly requested the correction.
Implemented: PR candidate always comes from validated Actions GITHUB_SHA; missing,
invalid, unavailable or mismatched Actions SHA fails closed even if webhook merge
metadata matches checkout. Local commit checks, exact ordered event base/head
parents, checkout equality and event base ancestry remain mandatory. Webhook
merge_commit_sha is diagnostic metadata only, and cannot select the candidate.
Push event proof is unchanged. Bounded failure diagnostics remain enabled.

Changed revision.py and real-Git regression tests; updated CI plan and design to
clarify the prior “event candidate SHA” requirement as Actions run GITHUB_SHA and
record actual stale webhook behavior. No public contract/schema changes, remote
settings, migration, rebaseline, relaxed parent proof or live API dependency.
Rollback: scoped revert. Starting HEADd684309; branch feat/wave-0-plan-4-ci.

Verification:
- RED: actual-shape synthetic stale webhook failed with
  CI_CANDIDATE_UNAVAILABLE; matching webhook merge incorrectly accepted six
  missing/invalid/mismatched Actions SHA inputs. Tests use real temporary Git
  graphs, not a mocked graph reader.
- `.venv/bin/python -m unittest discover -s tests/CI -p test_revision.py`:
  PASS18 in1.361s, including prior wrong-parent/nonmerge/stale checkout/missing
  object/base negatives and CLI Actions binding.
- Corresponding test_completion.py: PASS9 in0.613s.
- Corresponding test_workflow.py: PASS9 in0.247s.
- Prepared runtime `.local/ci-venv315/bin/python -m unittest discover -s tests/CI`:
  PASS165 in385.957s, using the same prepared PATH/LD_LIBRARY_PATH.
- `git diff --check`: PASS.

Scoped fix, tests, spec/plan and handoff committed together; parent records the
resulting identity. Next: parent reviews/pushes and inspects new
remote CI. This coding session performs no push. Prior remote runs remain failed;
no remote PASS or Wave0/P0 acceptance claim.
