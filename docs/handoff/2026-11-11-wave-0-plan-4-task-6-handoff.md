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

## Review round 1 corrections (same authorized local checkout)
Parent requested feasible fixes for missing hosted provisioning, backend/direct
Docker/Testcontainers coverage and source SBOM, while retaining all real findings
and independent-trust blockers. Implemented:
- setup.sh calls pinned Trivy provisioning for hosted jobs; archive and binary
  SHA256, regular-file-only extraction, bounded HTTPS and exact version checked.
  Offline real reviewed release install PASS; initial real asset RED exposed
 176386208-byte binary > initial128MiB bound, corrected to reviewed256MiB bound.
  Fresh network install attempt failed CI_IMAGE_SCANNER_UNAVAILABLE; availability
  on hosted runner NOT PROVEN. No checksum/version fallback.
- Backend direct run uses CID before requested --rm cleanup, observes actual
  started container image ID and scans it; both PostgreSQL Testcontainers fixtures
  invoke evidence after start. Direct Compose replacement runs route through same
  helper. CI disables unobserved Ryuk sidecar; direct/TC containers receive owner
  labels so failure cleanup remains scoped. Job and summary now require backend
  evidence. A job-local OS flock serializes scans/cache access.
- Source SBOM inventories exact committed supported npm/NuGet/Python locks with
  ecosystem-aware purl/bom-ref and run/revision binding. Security publishes exact
  normalized artifact separately from summary download/aggregation. Unsupported
  tool/unlocked coverage remains incomplete and command nonzero.

Observed round1 checks:
- RED missing provision/source/backend interfaces; RED missing file-lock helper;
  RED direct Compose runtime role rejected; each corrected, final focused PASS18,
  0skips,0.163s on pinned Python3.15.2.
- Actual reviewed release installation command
  `python3 -m scripts.ci.provision_scanners --output .local/task6-provisioned --archive .local/task5/trivy_0.76.2_Linux-64bit.tar.gz`: PASS, archive/executable/version verified.
  Fresh bounded network command: FAIL closed; urllib diagnostic retry was stopped
  and replaced by bounded private curl so network cannot wait indefinitely.
- `dotnet build tests/Integration/Integration.csproj -c Release --no-restore`:
  PASS0warnings/0errors (7.46s checkpoint; final recheck recorded in report).
- Actual backend `Integration.MigrationTests.Up_creates_only_technical_schema_and_history`
  with image evidence enabled: expected FAIL1 with safe CI_IMAGE_EVIDENCE_INCOMPLETE;
  initial scanner result incomplete0 packages, immutable-ID retry generated151
  packages/152 SBOM components but remains incompleteUNKNOWN. No vulnerability
  suppression. Owner-label cleanup verified. This is a negative gate rehearsal,
  NOT a passing service test.
- `python3 -m unittest discover -s tests/Security -v`: PASS10,0skips,35.691s.
- Full pinned CI run: FAIL1error/93,385.794s: native-publication test fixture lacked
  newly required backend evidence. Corrected fixture; targeted job-report4 PASS,
  0skips,0.030s. Summary10 PASS0skips0.040s. The semantic policy test passed in
  that full run. Final all-suite rerun NOT RUN; parent directed commit for review
  without waiting on a long suite. No claim of full final suite green.
- Fresh four-image build/started-container/scan rehearsal from exact committed
  `f67d15d74320ef43e4d3c45c6b8049e0cd382fc2` Git archive (not preexisting tags),
  source-identity labels and newly provisioned Trivy binary: API BLOCKED106/107,
  migrator BLOCKED120/121, PostgreSQL INCOMPLETE151/152, Keycloak BLOCKED559/560
  (packages/SBOM components). Each container actually started `/bin/sh -c exit 0`
  with network disabled; this verifies image binding, NOT full service readiness.
  Task6r1 code changes were still uncommitted: NOT a final-SHA service rehearsal.
- Actual source SBOM for f67d15d: incomplete10locks/444components with
  CI_TOOL_DEPENDENCY_COVERAGE + CI_UNLOCKED_DEPENDENCIES. Findings not waived.

Changed round1: scripts/ci/{provision_scanners,image_identity,sbom,setup,jobs,
summary}; workflow; three CI test files; Integration ContainerImages,
PostgresFixture, ComposeFixture and LocalBootstrap fixtures; README/handoff.
No API/schema/database changes, remote changes, new exceptions or trust approval.
Independent trust remains unprovisioned by design: remote owner/independent
coordinator authority cannot be created by candidate-owned scripts. All earlier
security/acceptance blockers still apply where not explicitly corrected above.

Final round1 .NET rebuild PASS0warnings/0errors,7.41s; final source inventory unchanged incomplete10locks/444components. Final py_compile/workflow contract/actionlint/diff-check PASS.

Final pinned nonsemantic CI subset: PASS94,0errors/0failures/0skips. Used unittest
discovery and flattened all tests, excluding only
`test_real_plan3_semantics_under_both_immutable_policies` (already passed in the
93-case full run above). This is explicitly a subset, not a final full-suite pass.
Round1 commit subject: `fix(ci): provision image scanner and extend runtime evidence`.

## Review round 2: source artifact and direct Docker secret checks
Parent assigned Important leak fix at source SBOM allowed name/version fields and
empty direct-Docker sentinel guard. Read current handoff and verified starting
workspace clean at a5cf17b. Same local-only authorization, no subagents/remotes.

Implemented source prepublication boundary: remove stale output before revision/
input/scanner checks; inspect original supported lock bytes under neutral private
filenames (default Gitleaks skips lockfile paths), then inspect normalized output
before atomic publish. Known sentinels are checked before serialization as well.
Missing/mismatched scanner, findings and malformed output deny the artifact even
if coverage status was already incomplete. Provisioner/setup now install pinned
Gitleaks using archive+executable hashes and exact version; source workflow supplies
its root explicitly. No scanner failures or findings are waived.

Direct Docker freezes fixture secrets before launch and passes them to evidence
helper via stdin: secret bind/volume files, sensitive environment entries, and
connection passwords. Testcontainers retained explicit generated passwords.
New .NET tests distinguish mount and environment values and avoid echoing them.
No values added to argv/environment/artifact/logs by this plumbing.

Actual verification:
- Initial Python focused RED2: missing known-sentinel source argument/publication
  guard. .NET compile RED2: missing FixtureSecretValues interface,0warnings.
- First real Gitleaks name/version regression FAIL1 (ScanError not raised).
  Raw scanner probes showed neutral-name JSON detects synthetic token but
  package-lock.json is omitted. Name canonicalization also transforms its prefix.
  Scanning original lock bytes under private neutral filenames fixed the real
  regression; extra normalized-artifact scan remains mandatory.
- Additional observed RED: invalid revision left an old uploadable source.json.
  CLI now removes it before reading revision; regression passes.
- Pinned Python3.15.2 focused image/SBOM suite: PASS21,0skips,0.290s.
- Real Gitleaks source SBOM fixtures: PASS2,0skips,5.841s. Secret in package name
  or version leaves no artifact (including when preexisting output existed);
  clean inventory still publishes its explicit incomplete tool coverage.
- Full real security fixtures `python3 -m unittest discover -s tests/Security -v`:
  PASS12,0skips,27.390s (actual pinned source/dependency/secret/image scanners).
- `dotnet test tests/Integration/Integration.csproj -c Release --no-restore --filter FullyQualifiedName~Integration.ImageEvidenceSecretTests ...`:
  final PASS2,0skips,57ms; rebuild has no warnings. Earlier first-green rebuild
  had1 nullable warning, fixed before final rerun. Logs/TRX remain private locally.
- Pinned CI discovery excluding only the long immutable-policy semantic case:
  PASS97,0errors/0failures/0skips. Explicit subset, full final suite NOT RUN per
  parent direction not to block review commit on long suite.
- Real Gitleaks offline install from previously verified release archive:
  `python3 -m scripts.ci.provision_scanners --kind gitleaks --output .local/task6-provisioned --archive .local/task5/gitleaks_8.30.1_linux_x64.tar.gz` PASS.
  Fresh network/hosted provisioning NOT rerun; prior availability blocker remains.
- Workflow contract, actionlint, py_compile and diff-check: PASS.

Changed round2: scripts/ci/{sbom,provision_scanners,setup}; source workflow env;
ContainerImages fixture; focused CI tests; new ImageEvidenceSecretTests.cs and
real source SBOM tests; README and this handoff. No contract/schema/database or
remote changes, exceptions, new trust authority or vulnerability suppression.
Commit subject `fix(ci): validate artifact secrets before publication`.
All real image findings, tool coverage/unlocked gaps, independent trust and
full-service/remote acceptance remain OPEN/BLOCKED. Local scanner diagnostics and
synthetic private files never become uploaded artifacts.

Final round2 guarded source rehearsal on committed a5cf17b: actual pinned Gitleaks
checked original lock bytes and normalized output; safe artifact produced with
INCOMPLETE10locks/444components, preserving existing coverage gaps. This is local
precommit guard evidence, not final-SHA/remote acceptance.
