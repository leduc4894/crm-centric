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
