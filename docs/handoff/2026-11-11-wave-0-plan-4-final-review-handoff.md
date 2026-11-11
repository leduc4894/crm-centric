# Wave 0 Plan 4 final review handoff

## Objective and authority

Address two Important final-review findings on the current branch, with tests
first: allowed license rows rejected during completion, and PyPI punctuation
folding corrupting npm/NuGet package identities. Parent authorized local changes
and a commit only; no worktree, subagents or remote writes. Read AGENTS.md, the
Task8 handoff, SDD progress and verification ledger; workspace was clean at
`2d2d3249d597e3c2e14c0a9f13a92e2c5931b1ab`, branch `feat/wave-0-plan-4-ci`.
Spec/plan links: [design](../superpowers/specs/2026-11-11-wave-0-plan-4-ci-wave-exit-design.md),
[plan](../superpowers/plans/2026-11-11-wave-0-plan-4-ci-wave-exit.md),
[verification](../engineering/08-WAVE-0-PLAN-4-VERIFICATION.md).

## Completed changes and decisions

Shared strict safe-pass finding validation between scan combination and successful
security completion. Only normalized opaque IDs, known non-secret finding kinds
and pass verdicts are accepted. No findings suppressed or approvals granted.
Producer parsing through scan combination, actual completion and reparsing is
covered, including unsafe row mutations. Inventory normalization is ecosystem
specific: npm/NuGet preserve punctuation and fold case; Python retains PyPI
punctuation folding. Lock-to-SBOM purls and mismatched scanner identities are tested.
No public API/schema/database migration. Roll back with a scoped commit revert.

Changed files: scripts/ci/{scan,summary}.py, tests/CI/{test_scan,test_completion}.py,
verification ledger and this handoff. Final commit: resolve the commit titled
`fix(ci): align scan evidence and ecosystem package identities` in branch history.

## Actual verification

- RED `python3 -m unittest discover -s tests/CI -p test_completion.py -q`:
  FAIL 1/8, allowed license producer rejected by completion.
- RED `python3 -m unittest discover -s tests/CI -p test_scan.py -q`:
  FAIL two ecosystem subcases among 21 tests, npm/NuGet punctuation corruption.
- GREEN same completion command: PASS 8, 0 skips, 0.559s.
- GREEN same scan command: PASS 21, 0 skips, 0.638s.
- `python3 -m unittest discover -s tests/CI -p test_security_job.py -q`:
  PASS 3, 0 skips, 0.164s.
- `python3 -m unittest discover -s tests/CI -p test_image_identity.py -q`:
  PASS 22, 0 skips, 0.307s.
- `python3 -m unittest discover -s tests/CI -p test_sbom.py -q`:
  NO TESTS RAN (0 discovered); relevant SBOM tests are in image identity suite.
- `python3 -m unittest discover -s tests/CI -p 'test_*.py' -q`:
  FAIL 1/115, 6.415s, `test_real_plan3_semantics_under_both_immutable_policies`
  returned CONTRACT_TOOL_ERROR in unprepared system environment (no dotnet PATH).
- `CI_SCANNER_ROOT="$PWD/.local/task5" python3 -m unittest discover -s tests/Security -p test_real_scanners.py -q`:
  PASS 8, 0 skips, 20.592s (real provisioned scanner fixtures).
- `PATH="$PWD/.local/ci-dotnet:/home/leduc/.local/share/fnm/node-versions/v24.22.2/installation/bin:$PATH" LD_LIBRARY_PATH="$PWD/.local/ci-python315/lib:$PWD/.local/plan3-runtime" .local/ci-venv315/bin/python -m unittest discover -s tests/CI -q`:
  PASS 115, 0 skips, 310.150s (prepared pinned runtime).
- `python3 -m scripts.ci.workflow`: PASS, workflow contract.
- `git diff --check`: PASS after code and documentation updates.

## Blockers and next steps

Wave0/P0 and all RT-* remain OPEN. Real repository secret/license/vulnerability
findings, dependency coverage gaps, independent trust provisioning and missing
exact final-SHA full service/remote acceptance evidence remain unchanged. No
backend/Compose/browser/remote runs or restore drills performed in this fix wave.
Both review fixes and local verification are complete. The commit includes the
tracked handoff; no additional implementation work remains uncommitted. Continue
the existing ledger's remediation and separately authorized
remote evidence work; do not waive findings or weaken trust to obtain green.
