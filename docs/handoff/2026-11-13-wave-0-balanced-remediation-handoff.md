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
