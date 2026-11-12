# Wave 0 Plan 4 — remediation discussion handoff

## Objective and status

User asked how to resolve the remaining Plan 4 blockers. This session reviewed the existing evidence and proposed a remediation order; it did not implement fixes or authorize remote writes. Wave 0, P0 and RT-* remain OPEN.

References: [Plan 4](../superpowers/plans/2026-11-11-wave-0-plan-4-ci-wave-exit.md), [design](../superpowers/specs/2026-11-11-wave-0-plan-4-ci-wave-exit-design.md), [verification ledger](../engineering/08-WAVE-0-PLAN-4-VERIFICATION.md), [final review handoff](2026-11-11-wave-0-plan-4-final-review-handoff.md).

## Findings and decisions

Priorities: safely triage real scanner findings and missing dependency coverage; diagnose and rerun failing backend/Compose jobs; implement and provision independent trust through a separately reviewed, separately authorized arrangement; run complete gates on one final candidate and obtain authorized remote PR/main evidence. Do not claim existing local results satisfy acceptance or relax policies to turn gates green. Governance verification is separate from technical Wave 0 exit; protection and merge queue remain UNKNOWN/OPEN.

## Workspace and verification

At session start branch `feat/wave-0-plan-4-ci` was clean. This handoff is the only intended change in this documentation-only session. No tests or scanners were run; `git status --short --branch` and the existing ledger/handoff were read. No remote write, push, merge, ruleset change or deployment was performed. Historical test and scan results are in the verification ledger; they were not rerun here.

## Next steps

Start local remediation with safe classification of scanner findings and coverage gaps, preserving sensitive data. Diagnose backend/Compose using normalized evidence and run complete final-SHA gates. Prepare a concrete independent verifier design and remote run/rollback proposal; request authorization only for remote operations or configuration when ready. Recheck actual branch/HEAD/status on resumption.

## Local scanner remediation continuation — 2026-11-12

Authorized scope: local investigation/remediation in the current checkout, one
coding agent, no worktree/subagents, push, remote writes/settings or publication.
Started on `feat/wave-0-plan-4-ci` at `9a0d749`; only this preserved untracked
handoff existed. Read this handoff, verification ledger and Task 5/6/7 handoffs.
Used systematic debugging and TDD instructions; no new implementation plan.

### Completed diagnosis

- Fresh pinned Gitleaks candidate-tree scan reproduced four `generic-api-key`
  findings, one in each SDK `provenance.json`. Privately compared each match to
  the `contractSha256` field and independently recomputed the SHA256 of its
  corresponding canonical OpenAPI file: all four agree. These findings are
  integrity metadata false positives; no matched values, snippets or raw reports
  were printed. No exception, scanner suppression, metadata rename or policy
  relaxation was applied. Findings remain blocking pending the existing
  independent finding-bound review process. This was a candidate-tree diagnosis,
  not a complete current trusted-range secret clearance.
- Private pinned Trivy dependency triage reproduced missing license metadata in
  239 package rows: pip 85, npm 3, NuGet 151. Additional identifiers include
  MPL-2.0 (12 rows) and BlueOak-1.0.0 (2 rows). Rows can repeat package identities
  across project locks; counts are not unique dependencies. No policy exemption
  or license clearance is inferred. Triage used the existing local database with
  `--skip-db-update`; it makes no freshness or current vulnerability-clearance
  claim. Raw output stayed in private-command temporary storage and was deleted.
- Exact missing project locks are
  `tools/contracts/surface/dotnet/Surface.csproj` (no package references) and
  `tests/Contract/fixtures/sdk/CorvusProbe.csproj` (SDK project reference).
  The adapter currently requires nonempty lock inventories; checking in an empty
  Surface lock would still fail and would not supply supported evidence.
- Attempted a real pinned SDK `dotnet restore .../CorvusProbe.csproj
  --use-lock-file`, privately capturing diagnostics: FAIL exit 1, NU1008.
  Root cause: the project inherits central package management while referenced
  generated SDK projects contain explicit PackageReference versions. A partial
  generated lock was removed; it was never accepted as valid evidence. Changing
  generated SDK projects/toolchain provenance requires the contract workflow;
  no speculative contract/generator edit was made in this scanner triage.
- Toolchain/dotnet-tool transitive dependency coverage remains unsupported and
  fail-closed. Adding manifest direct versions alone cannot establish that
  closure. No safe small implementation change was completed in this session.

### Actual verification and workspace

- `python3 -m unittest discover -s tests/CI -p test_scan.py`: PASS 21 tests,
  0 skips, 0.358s, system Python. Existing policy tests; no new RED/GREEN code.
- `python3 -m unittest discover -s tests/Security -v`: PASS 12 tests, 0 skips,
  23.347s with actual pinned scanners (private log under ignored owner-only
  `.local/remediation/`). Fixture success does not clear repository findings.
- Pinned SDK restore above: FAIL NU1008. No valid lock or runtime code change.
- Full CI, full backend/Compose jobs, final exact-SHA full gate set, remote jobs,
  trust provisioning and restore drills: NOT RUN here. Existing failures persist.
- Only intended tracked change is this handoff; local ignored triage scripts/logs
  contain no intentionally retained raw scanner reports. No API/schema, dependency
  version, security policy, exception, workflow or acceptance status changed.

### Next concrete steps

Implement reviewed support for complete zero-package lock evidence and validate
against real Trivy behavior; fix fixture lock restoration through the existing
contract/generator process, then add verified locks. Design complete independently
verified tool dependency closure. Review license metadata against authoritative
package contents and review the four hash-bound false positives via existing
trusted approval rules. Keep all blockers in force; rerun fresh exact-candidate
full gates after concrete fixes. Backend/Compose, image findings and independent
trust still need their own remediation; Wave 0/P0/RT-* remain OPEN.
