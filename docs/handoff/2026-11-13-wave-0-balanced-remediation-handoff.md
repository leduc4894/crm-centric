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

## Final fixed-candidate local rehearsal

Correction committed as `c5cfdd16732f7560ecdf7406e83b66270f514ba0` on
`feat/wave-0-plan-4-ci`. Exact local nonforced push event validated against
`c4a5ef40a34daded023d5e5121c0462e94cc1487` (actual local-main merge-base);
all six pinned runtimes reverified. No base substitution or remote operation.

Actual final job commands: `python -m scripts.ci.jobs <job> --mode mvp --revision
.local/balanced-c5cfdd1/revision.json --output .local/balanced-c5cfdd1/<job>.json`
for backend, compose, contracts-sdk and web-browser; security uses
`python -m scripts.ci.security` with the same flags. PASS: backend96/0/0 in418.225s,
Compose14/0/0 in1350.790s, contracts87/0/0 in2099.296s, browser58/0/0 in343.676s,
security1/0/0 in19.871s. Contracts completed actual generation, historical consumers,
true-base semantic gate, aggregate/environment fixtures, exact-candidate clean
export with browser, negative cases and cleanup. Security actually reviewed64
exact tree plus60 exact history metadata records; six nonblocking vulnerability
records remain visible. No broad finding exclusion or independent-verifier claim.

Actual aggregate command: `python -m scripts.ci.aggregate --mode mvp --identity
.local/balanced-c5cfdd1/identity.json --needs .local/balanced-c5cfdd1/needs.json
--reports .local/balanced-c5cfdd1/backend.json .local/balanced-c5cfdd1/compose.json
.local/balanced-c5cfdd1/contracts-sdk.json .local/balanced-c5cfdd1/web-browser.json
.local/balanced-c5cfdd1/security.json`: PASS0 `CI_COMPLETE`, success=true,
correlation `c35425c0-1d73-44c8-b966-ba994bdc2734`. Local needs reflect real local
process outcomes; they are not GitHub scheduler/enforcement proof.

Execution correction retained as evidence: initial contracts had86pass/1fail
because a private helper inherited PYTHONPATH into the intentionally incomplete
missing-module fixture. The missing-module test PASS1 in0.124s without PYTHONPATH;
no acceptance test change. Removed PYTHONPATH from all rerun job subprocesses;
full contracts then PASS87. Initial browser PASS58 in345.328s was also preserved
and rerun in the corrected environment, PASS58 above. Both initial reports remain
with `-initial-env.json` suffix; no first failure overwritten. The original8e67a18
Compose finished PASS14 in1666.713s; its paused continuation ended after completion.
Final isolated backend PASS96 confirms no readiness timeout change was necessary.
All started job processes have completed; no active continuation remains.

Post-fix focused MVP contract review suite PASS3 in0.091s. Full CI155 result above
was on8e67a18; it was not rerun in full after the small reason-token correction.
Full Contract87 and final five-job runs are on fixedc5cfdd1. Safe report identities,
digests, runtime pins and original-attempt outcomes are in
[final evidence](../engineering/evidence/2026-11-13-balanced-mvp-c5cfdd1.json).

Evidence/documentation commit changes HEAD after the rehearsal; these full jobs
bindc5cfdd1 and do not claim a fresh run for the later documentation commit.
Unrelated untracked `docs/handoff/2026-11-12-wave-0-plan-3-resumption-handoff.md`
remains untouched and outside commits. No public contract/schema or database
migration. Rollback correction via scoped revert; retain historical evidence.
Next: separately authorized remote CI/enforcement and acceptance drills. Wave0,
P0 and RT-* remain OPEN; remote DemoModule E2E, concurrent duplicate, tenant
isolation, outage/recovery and restore acceptance NOT RUN. Independent enforcement
and dual-policy evaluation remain deferred hardening, explicitly absent in MVP.

Additional standalone exactc5cfdd1 command `python scripts/contracts/compatibility.py
--base-ref c4a5ef40a34daded023d5e5121c0462e94cc1487 --mvp-reviewed-policy`
PASS0 `CONTRACT_COMPATIBLE`; full safe result embedded in final evidence,
including exact input hashes and explicit absence of independent/dual-policy
verification. Final documentation/evidence validation: identity/count/report-digest
invariants, changed Markdown links, Python AST and `git diff --check` PASS.
No full regression rerun for this final evidence-only commit.

## Final local review

Read-only whole-change review `d949ece..00e0ce4` found no Critical/Important findings within the approved MVP scope. Reviewer verified the five private report hashes against committed evidence, actual local-main merge-base, sixty exact historical IDs, and focused contract-review tests PASS3. Full local five-job/aggregate evidence remains bound to `c5cfdd1`; this paragraph and the previous evidence commit are documentation-only. No remote run, push, merge or ruleset change occurred. The unrelated untracked Plan3 handoff remains preserved. Remote CI and Wave 0 exit remain OPEN; independent enforcement and full image/license hardening are still pre-production work.
