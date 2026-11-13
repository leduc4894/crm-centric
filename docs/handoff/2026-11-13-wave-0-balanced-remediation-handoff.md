# Wave0 balanced remediation — 2026-11-13

Objective: implement user-approved exact sixty historical metadata reviews and
practical maintainer review of the two exact policy differences; user explicitly
authorized balanced remediation and plan adjustment. Read AGENTS, scope09, latest
rehearsal handoff/review and machine evidence; verified branch
`feat/wave-0-plan-4-ci` starting at `d949ece`, actual local-main merge base c4a5ef4,
tracked clean workspace, and preserved unrelated untracked Plan3 handoff.

Implementation: separate historical policy binds native IDs, original commit/blob,
full document digest, mapped reviewed tree ID/field/location/rule, multiplicity,
pinned scanner/config, maintainer/expiry. Actual Git bytes are verified in scan.
Other tree/history/new/deleted/merge records remain blocking. Sixty-four original
tree reviews remain unchanged. Exact two-file contract identity delta on c4a5ef4
is maintainer reviewed; semantic compatibility still executes. Reports disclose
candidate ownership and absent independent/dual-policy verification. No bootstrap
rewrite, base substitution, synthetic capability, remote or repository settings
change. See scope09 and amended MVP adjustment plan. No contract/schema changes
or DB migration; rollback by reverting these implementation commits.

Changed files: finding_adjudication.py, scan.py, new historical inventory,
contract_review.py, compatibility.py, focused history/contract tests, scope09,
plan and this handoff. Verification and actual candidate identities are updated
below after execution. Work is local; Wave0/P0/RT-* and production hardening OPEN.
Remote five-job CI and acceptance drills NOT RUN. No full local five-job claim.

Initial actual scan at7547e5a: BLOCKED64 reviewed/60 history, correctly fail-closed;
identified missing acquisition import in new history callback (kernel direct real
scanner tests had passed). Replaced callback with private-command Git blob bytes;
rerun required on corrected committed candidate, original result retained in/tmp.

Focused verification so far: historical inventory RED missing-file failure then
GREEN; real pinned history + mutations + merge-introduced/deleted-secret + bad
scanner tests PASS3 in35.079s, including full adapter PASS124 reviewed/0 blockers
at corrected d268ca7. Existing MVP policy tests PASS8 in38.418s; job tests PASS15
in4.016s. Real default preflight remains blocked; explicit MVP preflight PASS;
wrong base/extra diff/changed hash/expiry/clean-export propagation tests PASS3.
Shell syntax checks PASS. Complete CI/compatibility suites and actual semantic
compatibility are still RUNNING at this commit; no success inferred.

## Resumed verification and isolated CLI correction

Resumed on8e67a18, verified tracked clean start/current branch and preserved the
unrelated untracked Plan3 handoff. Reviewed7547e5a,d268ca7,8e67a18 against exact
review evidence. No remote operations or additional agents.

Fresh true-base compatibility command with `--mvp-reviewed-policy` PASS0
`CONTRACT_COMPATIBLE`, base c4a5ef4, correlation
`d13222d8-40cd-4f8f-9b64-a793f43e7c60`; historical SDK toolchain/consumer execution
and identical twelve contract input hashes. Report explicitly discloses absent
independent/dual-policy verification. Full `python -m unittest discover -s
tests/CI -q` PASS155 in721.358s. Real pinned security job PASS0 on8e67a18.
Private reports/event/version proof: `.local/balanced-8e67a18/`; all six runtime
pins verified, actual nonforced local event base is c4a5ef4.

Full standalone Contract suite RED:85pass/2fail in1623.915s. Both isolated-copy
CLI tests exposed optional CI module import masking required reason tokens.
Minimal correction: load maintainer review only for explicit MVP opt-in; reject
absent transition before importing independent transition capability. Existing
`test_missing_tool_fails_closed` and `test_candidate_policy_change_cannot_rebaseline`
then GREEN1/1 each in0.439s/0.559s. `git diff --check` PASS. No contract/schema or
semantic comparison changes; rollback by reverting correction commit.

Initial pinned backend attempt FAIL `CI_COMMAND_FAILED` after737.181s with zero
collected report entries. Separate native diagnostic reproduced75/76 pass, only
Keycloak management readiness fails. Private fixture capture reports no ERROR or
FATAL, server startup45.987s plus augmentation; its timestamp span exceeds the
100x500ms readiness poll while concurrent Docker suites run. This is evidence of
load-sensitive readiness, not proof of a product defect. Original failure retained;
no timeout/gate weakening. Isolated backend rerun remains required. Compose job
is still running at this correction commit; remaining jobs and fresh exact-candidate
validation after correction are pending, not passed. Full original Contract failure
and original backend attempt remain evidence. No Wave0/P0 acceptance claim.
