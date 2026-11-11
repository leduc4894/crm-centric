# Wave 0 Plan 4 Task 6 handoff

## Objective, authority and starting state
Implement image identity, private scans/SBOMs, Compose/browser evidence binding and
aggregate rejection for missing/mismatched evidence. Read AGENTS, Task6 brief,
Task5 handoff and image-provenance specification section. Starting branch
`feat/wave-0-plan-4-ci`, clean at `a3d1853f22f552519a4ef9ba77caaae38a6bcce9`.
Parent explicitly authorized current checkout, local commit and report; no new
worktree, subagents, remote changes, pushes or trust exceptions. Existing Task5
findings, tool coverage and independent trust remain blocking.

References: [spec](../superpowers/specs/2026-11-11-wave-0-plan-4-ci-wave-exit-design.md),
[plan](../superpowers/plans/2026-11-11-wave-0-plan-4-ci-wave-exit.md),
[implementation limits](../../tools/ci/README.md).

## Implemented; task/security acceptance remains blocked
ImageIdentity distinguishes local config IDs from OCI manifest digests; all local
manifestDigest values remain null without a verified descriptor chain. Started
container observation checks actual container Image and immutable image inspect.
Private pinned Trivy scans use Docker-only immutable IDs, nonempty OS/language
package inventories, real successful main and Java DB updates and upstream
UpdatedAt freshness checks (24h). Normalized CycloneDX inventory strips raw
metadata and includes tool/image properties; job evidence hashes are recomputed.
Missing/mismatched job/run/image/roles, empty/tampered SBOM, stale DB, unavailable
scanner, blocked findings and unknown severities fail closed with safe reason and
correlation IDs. Runtime sentinels are checked privately before artifact writes.

Compose/browser runners use fresh per-job scratch evidence. Local-stack readiness
captures the four actual runtime roles before returning to service tests; exact
identities are checked by job runner and normalized-summary parser used by the
aggregator. Explicit workflow scanner path is not yet provisioned, so hosted jobs
continue to fail closed. Security and independent trust placeholders remain.
No image archives or registry publication, public API/schema/database migration,
new exceptions or security baseline suppression. Rollback by reverting this commit.

Changed: .github/workflows/wave0.yml; scripts/ci/{image_identity,sbom,scan,jobs,
summary}.py; scripts/local/compose.py; tests/CI/test_image_identity.py;
tests/Security/test_real_images.py; tools/ci/README.md; this handoff.
Commit subject `ci: bind image security evidence to tested images`; exact SHA is
recorded by parent/report after commit. Ignored detailed report lives at
`.superpowers/sdd/2026-11-11-wave-0-plan-4-ci-wave-exit/task-6-report.md`.

## Verification actually observed
- Initial `python -m unittest ...`: NOT RUN, shell had no python command (127).
- `python3 -m unittest discover -s tests/CI -p test_image_identity.py -v`:
  initial RED missing module; later RED for missing job validator, empty/tampered
  SBOM accepted, malformed nested array raising IndexError, sentinel checker absent.
  Latest focused result PASS13, 0skips, 0.075s (pinned Python3.15.2).
- Real image fixture first RED: Trivy image mode does not inventory a copied npm
  lock as installed packages. Corrected fixture to installed lodash4.17.19
  package.json in scratch image. Real scanner detected blocking vulnerability,
  nonempty SBOM and aggregate evidence rejection. No fake scanner or exception.
- `python3 -m unittest discover -s tests/Security -v`: final PASS10, 0skips,
  24.628s, including all prior scanner fixtures plus vulnerable image and actual
  started-container config-ID observation. Tool checksum/version verified.
- `PYTHONPATH=. python3 .local/task5/image-rehearse.py`: actual local image results
  on existing `crm-ci-browser-e186-*` images: API BLOCKED106 packages/107 SBOM
  components/5 blocking findings; migrator BLOCKED120/121/5; PostgreSQL
  INCOMPLETE151/152/135 blocking plus unknown severity; Keycloak BLOCKED559/560/6.
  Main DB UpdatedAt2026-11-11T13:00:47.186797699Z; Java DB
  UpdatedAt2026-11-11T01:07:27.52801824Z. Both <24h when observed.
  These are historical available local images, NOT a current-SHA stack rehearsal.
- First full CI run with system Python3.12: FAIL1 of86 in3.968s,
  `test_real_plan3_semantics_under_both_immutable_policies` returned
  CONTRACT_TOOL_ERROR; runtime environment lacked working Node native library.
- Python3.15 run with required shared libs but wrong Node26: FAIL same1 of87,
  6.483s. No passing full-suite claim for either run.
- Final corrected pinned full CI run and final image recheck results recorded below.
- `python3 -m py_compile` changed Python modules, `git diff --check`,
  `python3 -m scripts.ci.workflow`, `.local/ci-tools/actionlint .github/workflows/wave0.yml`:
  PASS at verification checkpoint; final commands repeated before commit.

## Blockers and concrete next steps
Image findings require remediation/review; UNKNOWN severity is not waived. Backend
ContainerImages/direct Docker/Testcontainers image coverage is not wired into the
local-stack hook. Source dependency SBOM, complete tool dependency coverage,
scanner runtime attestation/hosted provisioning and independent trusted policy
remain open. Full current-SHA Compose/browser/backend service rehearsals and
remote CI NOT RUN. Remote DemoModule, concurrent duplicates, tenant isolation,
outage/recovery and restore acceptance NOT DONE. No Wave0/P0 completion claim.

Next: review this implementation; extend image observation to all direct/container
fixtures; provision pinned scanner runtime at declared workflow path; investigate
normalized findings privately; complete source SBOM/independent trust; rebuild and
scan current immutable images and run exact-SHA local and authorized remote jobs.
Private ignored `.local/task5/task6-*` logs and normalized rehearsals are retained
locally; never publish raw logs, inspect or scanner diagnostics.

## Final verification / implementation checkpoint
Correct full pinned CI command (PATH includes extracted dotnet and Node24.22.2;
LD_LIBRARY_PATH includes Python3.15 libraries and local native runtime) completed
PASS88,0skips,346.064s. This run began before the final lifecycle-hook/sentinel
fixtures were added; the latest focused13 and Local20 checks below cover the
subsequent changes. Do not label this a final-commit SHA full-service run.

Latest `python -m unittest discover -s tests/CI -p test_image_identity.py -v`
under pinned3.15 runtime: PASS13,0skips,0.075s. Latest pinned
`python -m unittest discover -s tests/Local`: PASS20,0skips,5.244s.
Final four-image scan rerun unchanged: BLOCKED/BLOCKED/INCOMPLETE/BLOCKED,
all four SBOMs nonempty. Full real security suite PASS10,0skips,24.628s.
Final py_compile, diff-check, workflow contract and actionlint all PASS.

Readiness image capture now runs after health readiness and outside the startup
retry/deadline loop, so a scanner failure propagates CI_IMAGE_EVIDENCE_INCOMPLETE
immediately. Helper handles TERM/INT and unwinds private scanner groups; the
lifecycle wrapper terminates the helper on its bounded timeout before force kill.
Full service/browser/remote acceptance remains NOT RUN and blocked as above.
