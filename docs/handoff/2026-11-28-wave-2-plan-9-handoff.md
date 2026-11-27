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
