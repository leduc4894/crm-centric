# Wave 0 actual remote base review

Objective: preserve the true PR event base while allowing the already approved
exact two-file MVP policy transition on actual remote main
`df993ae4ff44510a2ecfc861bb277a3f9418577f`. User authorized branch publication and
PR creation to main, without merge or settings changes. This scoped correction
performs no remote mutation. Relevant context:
[balanced remediation](2026-11-13-wave-0-balanced-remediation-handoff.md),
[CI plan](../superpowers/plans/2026-11-11-wave-0-plan-4-ci-wave-exit.md).

Completed: verified `git diff --name-only df993ae4ff44510a2ecfc861bb277a3f9418577f
c4a5ef40a34daded023d5e5121c0462e94cc1487` lists only the prior handoff, CI plan
and CI design documents. A Python inventory using the real gate policy selectors,
SHA256 of git-show contents and the surface-policy prefix proved all five baseline
policy files identical. Added only this explicit remote base to the reviewed-base
set. Exact old/new hashes, two-file delta, expiry, MVP opt-in and default refusal
remain enforced. No event rewrite, rebaseline, public contract or schema change;
independent verification and dual-policy execution remain explicitly absent.

Changed files: scripts/ci/contract_review.py, tests/CI/test_mvp_contract_review.py,
and this handoff. Branch `feat/wave-0-plan-4-ci`, starting HEAD `3ddb674`.
Unrelated untracked `2026-11-12-wave-0-plan-3-resumption-handoff.md` is preserved.
Rollback: scoped revert; no migration. No P0/Wave 0 acceptance claim.

Verification so far:
- Initial `python` commands: NOT RUN (shell has no python executable).
- `.venv/bin/python -m unittest discover -s tests/CI -p test_mvp_contract_review.py`:
  RED, four tests with one expected remote-base assertion failure; after fix PASS4
  (0.163s), including real default-refusal/MVP-acceptance for both bases.
- Baseline inventory equality assertion: PASS, five policy files.
- `.venv/bin/python -m unittest discover -s tests/CI`: FAIL156, 153 pass/3 fail,
  69.136s. Failures: history exact60 observed64; immutable-policy semantic test
  returned CONTRACT_TOOL_ERROR; missing-scanners required set lacked trivy.
- First standalone actual-base compatibility attempt with unconfigured runtimes:
  FAIL SDK_TOOLCHAIN_REVIEW_REQUIRED; Node lacked libatomic.so.1. Another attempt
  using ci-venv315 without LD_LIBRARY_PATH could not load libpython3.15.so.1.0.
  These environmental failures are retained, not reported as gate success.
- Prepared runtime reruns pending below; `git diff --check`: PASS.

Prepared runtime environment for subsequent commands:
`PATH="$PWD/.local/ci-venv315/bin:$PWD/.local/ci-tools:$PWD/.local/ci-dotnet:/home/leduc/.local/share/fnm/node-versions/v24.22.2/installation/bin:$PATH"`
and `LD_LIBRARY_PATH="$PWD/.local/ci-python315/lib:$PWD/.local/ci-browser-runtime/usr/lib/x86_64-linux-gnu:$PWD/.local/plan3-runtime"`.

Prepared environment actual command `.local/ci-venv315/bin/python
scripts/contracts/compatibility.py --base-ref
df993ae4ff44510a2ecfc861bb277a3f9418577f --mvp-reviewed-policy`: PASS0
`CONTRACT_COMPATIBLE`, correlation `2abbbeb0-86e0-4816-b018-2df1eb178bee`.
This ran real generation, historical consumers and pinned Docker surface builds;
reported baseSha is actual remote base, sdkToolchainChanged=false.

History failure diagnosis (no new approval): pinned Gitleaks scan of current HEAD
reports four additional generic-api-key findings beyond the approved sixty, all
introduced by documentation commit `00e0ce4ebea101a0a17c4755a21f5406168ada50` in
`docs/engineering/evidence/2026-11-13-balanced-mvp-c5cfdd1.json`, lines 259, 265,
273 and 279. They are the SHA256 metadata fields beneath
`standalone_semantic/inputHashes/{base,current}` for crm-api.openapi.yaml and
platform-operations.openapi.yaml. An in-memory equality assertion verified all
four against SHA256 of the exact recorded base/candidate git blobs: PASS. No
matched values were printed, no approval expanded, and the history gate remains
closed. Parent session must handle this separately before claiming CI green.

Prepared environment `.local/ci-venv315/bin/python -m unittest discover -s
tests/CI`: FAIL156, 155 pass/1 fail in418.796s. Only remaining failure is
`test_real_pinned_history_exact60_and_tampering_fail_closed` (60 !=64), diagnosed
above. Both the immutable-policy semantic test and missing-scanners test PASS
with prepared runtime. This is not a green suite. Final focused suite PASS4 in
0.224s; `git diff --check` PASS. All started verification processes completed.

Next steps: parent reviews four newly generated historical metadata findings
under existing approval rules; do not silently enlarge exceptions. Publish scoped
branch/PR only under existing user authorization and report actual remote checks.
Remote CI/enforcement and Wave0/P0 acceptance drills are NOT RUN in this work.
This scoped code/test/handoff correction is committed together; no unrelated
untracked file is staged. Parent will record resulting commit identity.
