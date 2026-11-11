# Wave 0 Plan 4 Task 5 handoff

## Objective and authority
Implement fail-closed source/dependency/secret scanner adapters under parent task
brief. Branch `feat/wave-0-plan-4-ci`; current shared checkout, one coding agent,
no worktree, no subagents, push, remote policy or repository permission changes.
Read AGENTS, Task4 handoff, Task5 brief and spec supply-chain section; verified
clean starting tree at `b8f65a9`. User authorizes local implementation/commits and
read-only official release/docs lookups. No new exception or trust approval.

References: [spec](../superpowers/specs/2026-11-11-wave-0-plan-4-ci-wave-exit-design.md),
[plan](../superpowers/plans/2026-11-11-wave-0-plan-4-ci-wave-exit.md),
[scanner details/limits](../../tools/ci/README.md).

## Implemented, acceptance still blocked
Private subprocess output lifecycle, opaque normalized summaries, checksum/version
preflight; exact immutable candidate archives; complete Git commit/object range,
including merge-parent diffs and added-then-deleted synthetic credentials; Semgrep
CE local C#/TS security rules with full source inventory and no ignore/comment
suppression; Trivy fresh upstream DB checks, complete application lock/package
inventory, npm development dependencies, Python filename staging, restrictive
license review; exact approval-bound exception policy with no active exceptions;
repository combiner propagates missing classes, identity mismatches and tool gaps.

Pins: Semgrep CE1.185.0 +67 hashed transitive Python wheels, Gitleaks8.30.1,
Trivy0.76.2. Official release asset checksum verification actually performed.
Semgrep requires a dedicated supported Python3.12 environment; existing hosted
Python3.15.2 does not support its wheel. Runtime/launcher attestation and automated
hosted provisioning are OPEN; the local launcher hash is measured, not an external
trust proof. Rules are deliberately limited syntactic intrafile security analysis;
no claim of broad rule-pack/cross-file coverage or provider secret verification.

Changed: scripts/ci/{run_safe,scan}.py, tests/CI/test_scan.py,
tests/Security/test_real_scanners.py, tools/ci/{toolchain,security-policy,
exceptions}.json, tools/ci/{semgrep.yml,gitleaks.toml,semgrep-requirements.lock},
tools/ci/README.md, this handoff. No public API/schema/database migration.
Rollback: revert Task5 commit. Commit subject:
`ci: add fail-closed source and dependency security gates`; exact SHA recorded in
ignored `.superpowers/sdd/2026-11-11-wave-0-plan-4-ci-wave-exit/task-5-report.md`.

## Verification actually observed
- Initial focused RED: missing scanner module/interface. Further observed RED:
  Trivy license result sections; replayed exception approval with edited expiry;
  absent immutable archive/staging/package-inventory/combiner implementations.
- Required Python3.15.2 wrapper suite: PASS17,0skips,0.285s. Earlier3.15 run
  failed child-runtime prerequisite because intentionally scrubbed LD_LIBRARY_PATH;
  synthetic subprocess fixture corrected to explicit system Python; rerun PASS.
- Real scanner suite: PASS5,0skips,13.484s before merge test. Stronger real merge
  fixture then reproduced a secret missed by default Git log (RED); fixed with
  `--full-history --diff-merges=separate`, single-case PASS1,2.355s. Final six-case
  result: PASS6,0skips,17.231s.
- Stronger real fixtures reproduced default Semgrep tests-directory omission and
  default Trivy npm dev-dependency omission (RED); staging/flag fixes PASS.
- Historical committed candidate `b8f65a9` repository rehearsal after those fixes:
  Semgrep PASS all1016 files,0 findings; Trivy BLOCKED on9 lock targets with
  exact package inventory plus separate `CI_TOOL_DEPENDENCY_COVERAGE`; Gitleaks
  BLOCKED4 findings. No suppressions or exceptions applied. This is historical
  scanner evidence, not a scan of the new Task5 commit.
- Real Trivy DB upstream UpdatedAt `2026-11-11T13:00:47.186797699Z`, retrieved
  at local run time; DownloadedAt was never used as freshness. Not a permanent
  freshness claim; every invocation rechecks current age.
- py_compile and git diff --check: PASS at recorded implementation checkpoint;
  final py_compile/git diff --check PASS. Full CI suite on earlier in-memory
  implementation completed74 cases in334.728s, FAIL1 (the pre-correction child-
  runtime fixture above); other73 passed, no skips. Corrected focused17 PASS.
  This does not constitute a final-SHA full-suite pass.

## Open work and concrete next steps
Current security workflow placeholder and CI_TRUST_NOT_PROVISIONED remain intact.
Tool manifests (.NET tools/external tool binaries, scanner runtime dependencies)
need complete transitive vulnerability/license coverage; current adapter marks
tool-manifest coverage as incomplete and cannot return repository pass with gaps.
Investigate four unresolved secret IDs privately; do not print matched data/raw
reports. Review dependency license findings without broad exemptions. Add source
SBOM and Task6 image/SBOM identity binding; provision dedicated pinned scanner
runtime and independent trusted policy/verifier. Complete hosted integration and
scan final commit, then run required workflow/remote evidence. Full Wave0/P0 and
remote DemoModule/duplicates/isolation/outage/recovery/restore acceptance remain OPEN.

Ignored local scanner installs/downloads and normalized rehearsals are under mode0700
`.local/task5`; raw diagnostics never uploaded. Scanner adapter-owned temp files
are deleted automatically. Full CI process result is in the safe
`.local/task5/ci-suite-result.json`; private raw log must not be published. Parent
must review final report and any still-running rehearsal before handing off.
