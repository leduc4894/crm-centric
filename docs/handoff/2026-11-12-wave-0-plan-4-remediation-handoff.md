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


## Backend/Compose continuation — 2026-11-12

Scope remains local current checkout, one coding agent, no worktree/subagents or
remote writes. Started clean on `feat/wave-0-plan-4-ci` at
`7de4e88ef4224e7b003d3dd539c2ab1238df1672`; read this handoff, Task 4/7
handoffs and the verification ledger. Used systematic debugging, TDD and
verification-before-completion. Wave 0/P0/RT-* remain OPEN.

### Reproduction and root cause

- Initial actual runner preflight failed `CI_COMMAND_FAILED` in 0.004s before
  cases: ambient Node 26 could not load libatomic. Reused the already prepared
  pinned Node 24.22.2 and local libatomic library; no host or source fix implied.
- At exact clean starting HEAD, ran the actual backend and Compose runners
  sequentially, with pinned tools, real Docker/Testcontainers/Compose and the
  mandatory image gate. A private native-suite observer retained only fixed
  reason tokens/outcomes (no raw test messages or scanner output). Backend
  failed `CI_COMMAND_FAILED` in 168.697s: 47 failed native results, 46 explicitly
  image-gate rejections, one other. Compose failed the same token in 932.285s:
  13 failed native results, all explicit image-gate rejections. The strict TRX
  parser rejects `ResultSummary outcome=Failed`, so outer failure normalization
  records zero cases. This explains missing job counts, not historical causes.
- Existing vulnerability/Java DB timestamps were still fresh at 00:52:22 UTC.
  After the suite, regular pinned update commands both exited 0, but Java DB
  retained upstream `2026-11-11T01:07:27.52801824Z`; at 01:09:26 UTC the strict
  24-hour check correctly rejected it before package scanning. No timestamp,
  freshness limit, scanner requirement or finding was changed.
- A fresh task-owned cache retrieved Java DB normally: exit 0, 38.917s, finished
  01:10:51 UTC, actual upstream `2026-11-12T00:58:04.30813481Z`, fresh. Normal
  vulnerability DB retrieval yielded upstream `2026-11-11T19:04:54.958468957Z`.
  Independent actual PostgreSQL image scan then completed package/SBOM stages
  with 151 packages and remained INCOMPLETE. Fresh retrieval is not clearance.
- A second full backend run with this fresh cache failed in 147.944s. Native
  counters: 74 total/executed, 27 passed, 47 failed, zero skipped/notExecuted,
  error, timeout or aborted. 46 failures were image gate; the other included
  IOException/UnauthorizedAccessException and remains to be localized.
- Fresh-cache native helper still emitted zero packages although independent
  scan collected 151. A narrowly instrumented real migration probe located the
  difference: `python3 -m scripts.ci.image_identity` creates
  `__main__.ImageIdentity`, while `scan_image` imports the named-module class.
  Dataclass equality rejects those distinct Python classes despite equal fields,
  before DB update/package scanning. This proves the current helper defect; it
  does not retrospectively prove the original Task 4 pre-image-gate failure.

### Scoped correction and verification checkpoint

Changed `scripts/ci/scan.py` to compare the complete dataclass field mappings
between observed and requested image identity. Config ID, platform, job/run and
manifest fields remain checked; no identity/finding/freshness rule is waived.
Added regression in `tests/CI/test_image_identity.py` loading the actual helper
file as a distinct module, exercising real scan parsing with external scanner
I/O fixtures and rejecting a genuinely different config ID.

- RED pinned image suite: 23 tests, one expected failure (valid CLI identity
  incorrectly INCOMPLETE), 0 errors, 0 skips, 0.411s.
- GREEN pinned image suite: 23 PASS, 0 skips, 0.266s.
- Real selective migration probe after correction reaches DB updates, four fresh
  checks, vulnerability scan and SBOM generation. It still fails the mandatory
  gate; no backend/service success is claimed. This was working-tree diagnostic
  evidence, not exact committed-candidate acceptance.
- Full pinned CI discovery: 116 PASS, 0 skips, 333.680s, exit 0. Command:
  `PATH=<existing pinned Node24 bin>:$PATH LD_LIBRARY_PATH=<existing Python315
  lib>:<prepared libatomic> .local/ci-venv315/bin/python -m unittest discover
  -s tests/CI`. Raw runner output stayed in ignored owner-only local storage.
- Real scanner fixture suite with old cache: FAIL, 12 tests / 1 failure,
  24.725s, exit 1. Named failure:
  `test_real_images.RealImageTests.test_vulnerable_synthetic_image_blocks_with_nonempty_sbom`;
  expected blocked versus actual incomplete, consistent with the verified stale
  old Java metadata. Fresh task-owned cache rerun: 12 PASS, 0 skips, 25.160s, exit 0.
  Fixture success does not clear actual repository/image findings.
- `git diff --check`: PASS at this checkpoint.
- Initial backend/Compose owner cleanup verified zero labelled containers,
  networks and volumes; existing foreign Keycloak untouched. Further runs and
  cleanup/results will be appended before final handoff.

Ignored owner-only `.local/remediation/backend-compose/` contains diagnostic
scripts and normalized local records. Scanner raw reports remain ephemeral and
are deleted by `private_command`; no raw diagnostic is published. No public
contract/schema, database migration, dependency version or security policy
change. Rollback: revert the scoped scanner comparison/regression commit.

Next: finish full CI verification, review/commit correction, rerun exact-SHA
backend/probes and localize the additional permission failure without touching
foreign resources. Full final-candidate gates, security findings, independent
trust and remote acceptance remain blocked/not proven.


### Committed image correction and permission-fixture follow-up

Image identity correction commit: `6562e89e2ed5b979974432eb0a0ea56ab05f0b00`.
Exact committed-SHA full backend rerun, fresh task-owned scanner cache: FAIL
`CI_COMMAND_FAILED`, 213.523s. Native 74 executed = 27 passed / 47 failed /
0 skipped; all timeout/error/aborted/notExecuted counters zero. 46 native failures
were explicit image-gate rejection. Actual normalized image records now collect
inventories: API 106 packages / 5 blocking; migrator 120 / 5; Keycloak 559 / 6;
PostgreSQL (two tested config IDs) 151 / 135 blocking + 12 unknown severity.
The original class mismatch is corrected; findings correctly keep the gate closed.

The remaining failure is
`Integration.ContainerRuntimeTests.Unreadable_or_missing_secret_fails_closed`: its
intentional chmod-000 secret was read by the new fixture evidence adapter before
Docker launch, yielding UnauthorizedAccessException/IOException. Parent explicitly
requested a narrow TDD fix retaining actual unreadable-file behavior and no bypass.

Follow-up working-tree correction: the Docker test helper accepts an explicit
file-bound fixture snapshot captured before permission revocation. It always
retains the snapshot for secret guarding and adds current bytes when readable;
only an explicitly supplied nonempty snapshot handles an unreadable bound file.
Unknown read failures close with `CI_SECRET_EXTRACTION_INCOMPLETE` plus fresh
correlation ID, with no raw exception/inner payload. File modes remain unchanged,
and the normal image capture/scan path remains mandatory. The unreadable negative
fixture supplies its own generated sentinel; other fixtures keep default extraction.

Actual follow-up verification:
- Original committed backend run above is RED for the existing unreadable test.
  New focused regression first failed to compile (CS1501, missing snapshot
  overload); this is a compile RED, not a claim that assertions executed.
- `dotnet test tests/Integration/Integration.csproj -c Release --no-restore
  --filter FullyQualifiedName~Integration.ImageEvidenceSecretTests`: GREEN
  6 passed / 0 failed / 0 skipped, 44ms. Includes mode preservation and safe
  unknown-unreadable error/correlation checks.
- Actual four-state runtime configuration probe with mandatory image evidence:
  FAIL `CI_COMMAND_FAILED`, 15.859s; 4 executed / 4 failed / 0 skipped, all four
  now explicit image-gate rejections; no other or permission-extraction failures.
- Separate real API and migrator unreadable-file diagnostic: both applications
  returned nonzero with `LOCAL_CONFIGURATION_INVALID`, correct service and valid
  correlation, no generated sentinel in output, and source mode remained 000.
  Each actual started container was then scanned through the mandatory image
  evidence helper and each gate was rejected. These application negative checks
  do not constitute a passing service/security job. Task-owned tags/containers
  were cleaned; an initial diagnostic could not use already-disposed fixture
  images, so it was rerun with freshly built task-owned tags. No foreign tag or
  resource was removed.
- `git diff --check`: PASS. Full exact follow-up SHA backend/Compose diagnostics
  will follow after the scoped commit; full final gate set/acceptance remains
  NOT RUN/blocked. Full Python CI 116 and real scanner 12 results above predate
  this C# test-helper-only correction; Python implementation did not change.

Changed follow-up files: `tests/Integration/Fixtures/ContainerImages.cs`,
`tests/Integration/ContainerRuntimeTests.cs`,
`tests/Integration/ImageEvidenceSecretTests.cs`, this handoff. No API/schema,
production runtime, migration, dependency or security-policy change. Rollback is
revert of this fixture correction. Continue exact-SHA evidence and review actual
image findings/unknown severities without suppressions; remote actions remain
unauthorized and all Wave 0/P0/RT-* acceptance stays OPEN.


### Final local checkpoint

Fixture correction commit: `a458e205ce2e0221f57257b3edfd25efec6e5bd6`.
The following actual runs were made sequentially against that exact clean SHA,
with the existing pinned toolchain, normal real services, fresh task-owned
scanner cache and mandatory image enforcement. The ignored observer wraps only
native suite collection; the selective Compose run explicitly changes the test
filter and is never represented as a full Compose job.

- Full backend: FAIL `CI_COMMAND_FAILED`, **182.334s**. Native **76 executed,
  29 passed, 47 failed, 0 skipped**; all 47 failures are explicitly
  `CI_IMAGE_EVIDENCE_INCOMPLETE`. No other or permission-extraction failures.
  Error, timeout, aborted and notExecuted counters are zero. As before, strict
  outer failed-TRX normalization emits zero collected job cases; these native
  diagnostic counts are not fabricated successful job evidence.
- Selective Compose
  `FullyQualifiedName=Integration.Compose.LocalStackTests.Cold_up_needs_no_host_sdk`:
  FAIL `CI_COMMAND_FAILED`, **90.013s**, 1 executed / 1 failed / 0 skipped,
  explicit image-gate rejection. Actual first API image scan: 106 packages,
  `CI_IMAGE_FINDINGS`, 5 blocking, zero unknown; immutable local config ID
  `sha256:7936935238564f110a1624be04bf8d50563f87efd2bfe3a9d906c91f6a9a785c`,
  linux/amd64, manifestDigest null. The gate stops at this first rejected role;
  this does not prove complete four-role Compose evidence or job success.
- Final Docker ownership reinspection: 0 labelled containers, 0 networks,
  0 volumes. Pre-existing foreign Keycloak still running. Diagnostic-owned
  application tags removed. Fresh ignored scanner cache/tools retained for
  reproducibility; no foreign cleanup or global prune.
- Final full five-job gate set, full Compose on the final correction, remote
  workflows, independent trust provisioning and P0/restore drills: NOT RUN /
  still blocked. Earlier full Compose failure remains recorded, not replaced by
  the selective result. Historical Task 4 pre-image-gate cause remains unproven.

Only documentation is changed after the tested implementation SHA: this handoff,
the verification ledger and the CI README snapshot-boundary explanation. Exact
commands are the actual `scripts.ci.jobs` backend/compose entrypoints with the
recorded RunIdentity and native-suite observer in ignored local scripts; backend
uses its full filter, Compose uses the explicit single-test diagnostic filter
above. Pinned unit/CI/security commands and earlier RED/GREEN results are recorded
above. No gate suppression, exception, timestamp edit, policy relaxation, remote
action or acceptance closure occurred.

Next: review the two scoped corrections, triage actual image vulnerabilities and
PostgreSQL unknown severities through the existing review process, retain fresh
upstream metadata, remediate source/dependency coverage and trust blockers, then
run the complete exact-candidate gates. Only separately authorized remote work
can supply missing PR/main/governance/acceptance evidence.

## Image dependency remediation — 2026-11-12

Continued locally on `feat/wave-0-plan-4-ci` at `2ee7b486f74cec561f79d7fdd318d5b08c032909`.
Read this handoff, AGENTS.md, the verification ledger, current Dockerfiles and
image locks. Used systematic debugging, TDD and verification-before-completion.
No remote writes, services/deployments, exception, severity or freshness changes.

### Diagnosis and scoped fix

Private pinned Trivy scans of the existing four Task 7 images reproduced API and
migrator 5 HIGH each; PostgreSQL 131 HIGH / 4 CRITICAL / 12 UNKNOWN; Keycloak 6 HIGH.
These diagnostic scans explicitly used the existing cache with update skipped;
they classify findings and alone provide no fresh gate clearance. Raw process
reports were ephemeral and deleted by `private_command`; only allowlisted
package/version/CVE/severity/status data was retained in ignored owner-only
`.local/remediation/image-triage/`.

API/migrator findings originate in the shared base: apt and libapt-pkg6.0t64
2.8.3 (CVE-2026-109518, scanner fix 2.8.4ubuntu0.1), and ASP.NET runtime 10.0.14
(CVE-2026-114146, CVE-2026-114147, CVE-2026-114149; scanner fix 10.0.15).
[Microsoft advisory](https://github.com/dotnet/announcements/issues/504) confirms
runtime 10.0.15 servicing; [Ubuntu advisory](https://ubuntu.com/security/CVE-2026-109518)
confirms apt impact, but its extracted release table still says vulnerable,
so it is not represented as independently confirming the scanner's fixed version.
Official MCR resolved the multi-platform runtime 10.0.15-noble manifest to
`sha256:bfd912c83295ce66d4b706ebb8541b9f14259b0e76fbb88b2751f58ce2399d2e`.
A private candidate-base scan collected 99 packages with no HIGH/CRITICAL/UNKNOWN.
Updated only that runtime lock/default and existing published-image assertions
for both Microsoft.NETCore.App and Microsoft.AspNetCore.App 10.0.15. SDK and NuGet
locks remain unchanged; no API/schema or database migration changes.

PostgreSQL's Bookworm OS contributes 119 blocking/unknown rows without fixed
versions, including util-linux, krb5, sqlite, libxml2, zlib and OpenSSL. Another
28 rows come from Go dependencies (stdlib v1.24.6 and golang.org/x/sys v0.1.0).
The official Dockerfile installs gosu 1.19; locating the exact binary remains a
follow-up before proposing replacement. Unknown IDs are CVE-2026-107496,
CVE-2026-39824, CVE-2026-88822, CVE-2026-88823, CVE-2026-95691,
CVE-2026-95692 and CVE-2026-97054 (some repeat by package). Do not infer a waiver
from vendor affected/fix_deferred/will_not_fix states.
An official same-server-version alternative `postgres:18.6-trixie` resolved to
`sha256:e3d24f6906d0760bf4da0dae872415391ededefad23fd471c47dc53b0ddf3c42`;
private candidate scan: 149 packages, 147 HIGH / 2 CRITICAL / 17 UNKNOWN. It was
not adopted: it does not clear the gate and changes distro compatibility.
[Debian util-linux tracker](https://security-tracker.debian.org/tracker/CVE-2026-53613)
shows a Trixie fix for one family, while
[Go tracker](https://security-tracker.debian.org/tracker/CVE-2026-97054) still lists
Bookworm/Trixie Go versions vulnerable. These are partial source checks, not a
complete independent CVE assessment.

Keycloak: java-21-openjdk-headless 1:21.0.13.0.9-1.2.el9 / CVE-2026-91403
(will_not_fix); sqlite-libs 3.34.1-11.el9_8 / CVE-2026-119373, -119374, -119379,
-119586 (affected); Guava 33.6.0-jre / CVE-2026-102554 (scanner fix 33.7.2).
[Official downloads](https://www.keycloak.org/downloads) and
[release](https://github.com/keycloak/keycloak/releases/tag/26.8.2) still identify
26.8.2 as latest; 26.8.3 registry lookup failed (no proof of a supported image).
[Guava 33.7.2](https://github.com/google/guava/releases/tag/v33.7.2) confirms its
CVE fix, but no unsupported JAR replacement or custom vendor runtime was applied.
Keycloak/PostgreSQL gates remain blocked pending supported tested upstream fixes
or a separately designed fully supported base/build change.

### Actual RED/GREEN checkpoint

- First selected runner attempt: preflight FAIL CI_COMMAND_FAILED, 0.008s, no
  tests, due missing pinned Node/native-library path; corrected local invocation.
- RED: existing published-image test with updated runtime assertion and old pin,
  actual selected backend runner: 1 executed / 1 failed / 0 skipped, mandatory
  old-image gate rejection, 20.214s. Assertion was not reached; this proves the
  original security regression, not assertion-level RED.
- GREEN working-tree diagnostic: same selected test with new pin, 1 executed /
  1 passed / 0 failed / 0 skipped, plus 20 Local tests passed. Both actual built
  API/migrator scans PASS CI_IMAGE_SCAN_OK (106/120 packages, no blocked or unknown
  findings). Runner deliberately reports CI_DISCOVERY_INCOMPLETE at 27.702s because
  selective execution cannot meet the full backend discovery contract. This is
  not a passing backend job or exact committed-candidate acceptance evidence.
- Actual scans performed normal DB updates and freshness enforcement through
  the mandatory helper. Retained cache metadata: vulnerability DB UpdatedAt
  2026-11-11T19:04:54.958468957Z; Java DB 2026-11-12T00:58:04.30813481Z; no timestamp
  edits. Freshness is checked at each scan, not claimed indefinitely.
- `git diff --check`: PASS. Full backend/Compose and final five gates will be
  recorded separately if run. Security/source/trust/remote/P0 blockers persist.

Rollback: revert runtime pin/default and assertion changes; no data rollback.
Next: run exact committed-SHA full backend with mandatory image scans; preserve
PostgreSQL/Keycloak rejection, localize the Go binary, and evaluate supported
upstream release/base candidates against full compatibility and image evidence.


## Exact runtime commit and remaining image diagnosis — 2026-11-12

Resumed on `feat/wave-0-plan-4-ci` at
`bf2921f16a37617dda2a781652ab8866fca2a50f`; verified actual completed process
records rather than assuming its outcome. Preserved unrelated untracked
`docs/handoff/2026-11-12-wave-0-plan-3-resumption-handoff.md` unchanged. Scope:
local diagnostics/documentation only, no worktree/subagents or remote writes.
Systematic debugging and verification-before-completion instructions used.

### Exact committed backend outcome

The full actual backend runner started at that clean SHA completed with FAIL
`CI_COMMAND_FAILED`, **218.563s**, run 1211/attempt 1. Native TRX counters:
**76 executed = 36 passed + 40 failed, 0 skipped**; error, timeout, aborted and
notExecuted all zero. All 40 failures explicitly reject mandatory image evidence;
there are no other exception categories in normalized failure records. Strict
failed-summary parsing correctly retains zero outer job cases. This is a failed
backend job, not acceptance closure.

Actual enforced scans: API PASS `CI_IMAGE_SCAN_OK` / 106 packages; migrator PASS /
120; each zero blocking/unknown. PostgreSQL (both observed local config IDs)
INCOMPLETE / 151 packages / 135 blocking + 12 unknown. Keycloak BLOCKED / 559 /
6 blocking. These completed records are in ignored owner-only diagnostic storage
under `.local/remediation/image-triage/full/` (revision, normalized job, native
failure metadata and image counts); no raw payload/report was published. The
invocation was the existing pinned-environment `python .../full/run.py backend`
observer around the real `scripts.ci.jobs` backend entrypoint, with full discovery.

### PostgreSQL binary provenance and supported candidates

Pinned Trivy identifies the Go target as `usr/local/bin/gosu` (`gobinary`). An
actual network-disabled disposable container from the pinned Bookworm image
reports `1.19 (go1.24.6 on linux/amd64; gc)`. Its SHA256 is
`52c8749d0142edd234e9d6bd5237dff2d81e71f43537e2f4f66f75dd4b243dd0`, exactly
matching a separately downloaded upstream `1.19/gosu-amd64` release. `dpkg-query
-S /usr/local/bin/gosu` reports no owning package (expected lookup exit 1).
The [official Dockerfile](https://github.com/docker-library/postgres/blob/master/18/bookworm/Dockerfile)
installs gosu 1.19 from signed upstream release assets, rather than a Debian
package. [Upstream 1.19](https://github.com/tianon/gosu/releases/tag/1.19)
confirms Go 1.24.6 and x/sys 0.1.0. No inference that an apt Go upgrade changes
this statically bundled binary.

[Latest gosu 1.20](https://github.com/tianon/gosu/releases/tag/1.20) still lists
Go 1.24.6 and x/sys 0.1.0. It updates moby/sys/user, so version advancement alone
provides no evidence of fixing the scanner's Go findings. The
[upstream security guidance](https://github.com/tianon/gosu/blob/1.20/SECURITY.md)
requests reachable-function analysis with its govulncheck wrapper. That guidance
is not an accepted finding-bound independent review, suppression or waiver here;
no reachability clearance was performed. No replacement binary was installed.

Additionally resolved/pulled/scanned official same-server candidate
`postgres:18.6-alpine@sha256:9ba53c7d41ea093baf5c1ba635ba5434c4f979880527d49bb7d9237d3193085b`:
**57 packages, 43 HIGH / 1 CRITICAL / 3 UNKNOWN**. It retains Go 1.24.6/x/sys
0.1.0 findings plus krb5/libxslt findings. Not adopted: it fails policy and would
require musl/entrypoint/volume compatibility evidence. Previous Trixie candidate
also remains rejected. No supported tested image candidate clears PostgreSQL.

### Keycloak same-version rebuild check

[Official downloads](https://www.keycloak.org/downloads) still list 26.8.2.
Registry tag 26.8.2 now resolves to a different immutable manifest:
`sha256:723b1d6e254c0fc96d5cffec860c7e0744d6a3277822b865bb538cf891575e41`.
Pulled and privately scanned that exact candidate: **559 packages / 6 HIGH**,
with the same Java21/sqlite/Guava package versions and findings as the current
pin. Not adopted: the rebuild does not clear any of those findings. Official
[container guidance](https://www.keycloak.org/server/containers) describes
customized builds and RPM addition, but does not establish that arbitrarily
replacing its bundled Guava is supported. No JAR or vendor-runtime substitution.

### Verification limits, workspace and next steps

Candidate scans above used pinned Trivy 0.76.2 with
`--skip-db-update --skip-java-db-update --scanners vuln --list-all-pkgs --format json`
and the existing task-owned cache. They are package diagnosis, not fresh mandatory
gate clearance. Commands ran via `private_command` (exit 0 for candidate scans,
pulls and manifest resolution); raw reports were deleted on context exit. Normalized
allowlisted results remain ignored locally. Upstream release-asset downloads
completed; their bytes were hashed in memory and not installed. No code change,
new test, contract, migration, lock/default or policy change in this continuation.
Actual additional-candidate command: `python3 .local/remediation/image-triage/additional_candidates.py`
(exit 0); full backend observer results predated resumption and were inspected,
not rerun. Unit/integration/scanner fixture suites NOT RUN in this documentation-only
continuation. Full Compose/five-job final gates, remote evidence and restore drills NOT RUN.

Docker ownership reinspection: zero containers/networks/volumes with
`io.crm-centric.ci-owner` label; no foreign resources removed. `git diff --check`
PASS before this documentation update, and rerun before commit. Documentation
rollback is revert of this follow-up commit. Wave 0/P0/RT-* remain OPEN.

Next: independently assess the exact gosu finding set through the existing review
process or design a reproducible supported patched build with full dependency
closure and actual privilege-drop/compatibility tests; await/test supported
PostgreSQL OS and Keycloak dependency fixes. Retain all blocking/unknown findings
until valid evidence resolves them. After fixes run full exact-candidate gates;
remote acceptance still requires separate authorization.

## Dependency coverage diagnosis — 2026-11-12

Resumed current checkout on `feat/wave-0-plan-4-ci` at `eddaa35` with only the
unrelated untracked Plan 3 resumption handoff; preserved it unchanged. Read
AGENTS.md, this handoff, scanner adapter/tests, generator and verification ledger.
Local scope only; no worktree/subagents, remote writes, policy relaxation or
contract/generated artifact changes. Used systematic debugging and verification
before completion. No implementation fix is claimed.

Actual isolated probe command: `python3 -u .local/remediation/coverage_probe.py`
(exit 0 on completed rerun), using pinned `.local/ci-dotnet/dotnet` and pinned
Trivy 0.76.2 through private-command temporary output. It copies project inputs
and inherited build/central-management properties into a disposable directory;
no accepted source lock is created or updated. Results:

- Surface restore PASS: real generated lock has framework `net10.0`, **zero**
  dependency rows. Real Trivy fs scan with existing task-owned cache,
  `--skip-db-update --scanners vuln --list-all-pkgs --format json`, returns schema
  2 and **zero Results**. Existing `lock_packages` rejects this evidence closed.
  This proves empty lock support needs a distinct verified zero-package evidence
  path; requiring a nonexistent Trivy target cannot work. Merely omitting the
  target or relaxing nonempty inventory globally would hide missing dependencies.
- CorvusProbe normal inherited restore FAIL: actual captured diagnostic contains
  **NU1008**. Diagnostic restore using the single global property
  `-p:ManagePackageVersionsCentrally=false` PASS with **6** lock rows. This
  isolates inherited central management versus generated explicit versioned
  PackageReference as the cause. Normal failed restore also emits a partial
  six-row lock; existence/count alone is not valid restore evidence. All probe
  locks were temporary and deleted. The property was not added to production,
  workflows, project files or accepted restore commands.
- The verified Corvus CLI archive contains one `.deps.json`, **238 libraries**,
  including **34 type=package libraries**. These are diagnostic counts, not
  established complete scanner/license evidence. Direct tool version manifests
  do not establish this closure. Dotnet-ef remains a direct manifest pin, while
  native scanner binaries and pinned toolchain images need their own dependency
  evidence. Existing `CI_TOOL_DEPENDENCY_COVERAGE` remains enforced.

Initial probe exited 1 at Trivy because private-command's isolated HOME had no
cache and skip-update cannot retrieve one. Rerun supplied the existing explicit
cache and completed above. Skip-update evidence establishes parser behavior only;
no freshness, vulnerability/license clearance or mandatory security job success.
No raw report or diagnostic payload was published; private-command deletes it.

Verification: `python3 -m unittest discover -s tests/CI -p test_scan.py` PASS,
**21 tests / 0 skips**, 0.359s, exit 0. No code was changed, so no new RED/GREEN
cycle or full-suite run is claimed. Full CI, backend/Compose, scanner fixture
suite, exact final-candidate gates, remote evidence and restore drills NOT RUN
in this continuation. `git diff --check` PASS before commit.

Concrete blockers and next steps: design/test a zero-package attestation bound to
successful evaluated restore (including imported/conditional MSBuild inputs),
while preserving exact nonempty package comparison elsewhere; route central
management isolation through the generator and regenerate/verify all provenance
and consumers before accepting a CorvusProbe lock; inventory actual tool package
closures, bind them to verified artifacts and verify real scanner treatment,
including native embedded dependencies. No guessed licenses, fabricated empty
scan records, hand-written package closure or unsupported bypass was accepted.
Wave 0/P0/RT-* remain OPEN. Changed tracked files are this handoff and the
verification ledger only; rollback is revert of the documentation commit.

### Empty-lock implementation feasibility follow-up

Parent requested safe Surface empty-lock support with actual restore proof,
malformed/tampered rejection and no candidate self-attestation. Started at
`491e74c`; unrelated untracked Plan 3 handoff still unchanged. No implementation
or baseline relaxation was made because the current adapter lacks the required
trusted input boundary; the following actual counterexample confirms why.

Command `python3 -u .local/remediation/empty_import_probe.py` PASS exit 0:
using the pinned SDK and a disposable explicitly authored project importing
`dependencies.props`, initial empty-import restore exits 0 and produces a
zero-package lock. Changing only that imported props file to add a versioned
System.Text.Json PackageReference leaves **exactly the same lock bytes**, but
actual `dotnet restore --locked-mode` exits 1 with **NU1004**. Thus unchanged
empty lock bytes and no direct PackageReference in the csproj do not establish
zero dependencies. Temporary inputs/output were deleted; no project source or
lock changed. This is a diagnostic counterexample, not an implementation RED/GREEN.

Precise architectural blocker: `dependency_inputs` copies recognized lockfiles
into an isolated Trivy directory and derives `expected_packages` from those same
candidate bytes. It supplies no project/import graph, evaluated restore outcome,
trusted producer identity or authenticated binding to candidate SHA/toolchain/
configuration. `ScanInput`/`DependencyInput` cannot carry such authenticated
restore evidence. Trivy emits no section for the valid empty case, so its absence
cannot distinguish this stale/tampered case. Adding a JSON declaration signed or
hashed only by candidate code would not create the requested independent proof.
Executing arbitrary candidate MSBuild during scanning also introduces an execution
boundary; arbitrary targets can alter files/evidence. Static XML detection cannot
cover imports, conditional items, SDK inference and target execution.

Required next implementation prerequisite: define/provision the independent
restore-verification producer and isolation policy, binding exact project,
lock/input closure, pinned SDK and restore configurations to candidate identity;
require successful locked restore and independently validated empty package assets.
Then extend the adapter with a separately authenticated empty-evidence channel,
keep empty targets distinct from scanner-observed nonempty targets, and test
missing/duplicate/malformed/stale/changed-import/changed-SHA/configuration evidence.
Current repository explicitly has no independently provisioned verifier; this
continuation cannot invent one from candidate data. Until that prerequisite,
empty locks remain unsupported and fail closed. No test or full gate success is
claimed beyond the actual diagnostic above and previous 21 policy tests; no new
unit tests were added to mirror unchanged fail-closed behavior. `git diff --check`
PASS before commit. Changed files remain this handoff and the verification ledger;
Wave 0/P0/RT-* remain OPEN.

## Independent trust provisioning proposal — 2026-11-12

Objective: make the independent verifier/empty-restore boundary concrete without
provisioning remote authority. Read AGENTS.md, this handoff, verification ledger,
trust spec, policy-transition implementation and workflow; verified workspace at
`242a14c22ccc81359e3a1f354548cb813193f1d1` on `feat/wave-0-plan-4-ci`.
Preserved unrelated untracked Plan 3 resumption handoff. Scope remains local;
no worktree, subagent, remote write, settings change, service or key provisioning.

Completed documentation only:
`docs/ci/independent-verifier-provisioning.md` describes proposed independent
service/App check ownership, exact required deployment fields and unassigned
owner inputs, key purpose/custody/rotation/revocation, isolated workers and trusted
supervisor, authenticated transport, complete five-job evidence, Surface-specific
reviewed restore profile, new evidence contract, bootstrap/migration/rollback and
RED/GREEN implementation sequence. It preserves the unconditional
`CI_TRUST_NOT_PROVISIONED` failure. Existing local transition CLI remains closed.
The document is a proposal for review, not user approval of an architecture or
remote deployment. No production signer or independently controlled source exists
as a result of writing it.

Decision proposed: reconstruct only independently reviewed Surface restore inputs
in a trusted pinned worker; arbitrary candidate MSBuild-generated empty assets
are not authoritative. Exact profile/input closure and negative constraints reject
changed imports even when lock bytes remain unchanged. Both Debug/Release command
variants require a real SDK spike before adoption. Unsupported projects, tool
closure, CorvusProbe restore and scanner findings remain blocked.

Verification: `git diff --check` PASS. No code tests, real SDK restore, full CI,
remote shadow/enforcement tests or operational trust tests run in this documentation
session. Only the proposal and this handoff changed. The planned negative matrix
is explicitly not completed. No public API/schema, dependency, security policy,
workflow or acceptance change. Rollback: revert documentation commit.

Next: owner reviews architecture and supplies missing immutable release/profile
hashes, actual administrative roles/IDs and bootstrap ADR; implement/test locally
with synthetic authority first. Actual independent source/service/App/keys,
remote shadow runs and ruleset/enforcement require separate authorization. Never
remove the local trust failure before reviewed independent evidence and explicit
migration. Wave 0/P0/RT-* remain OPEN.


### Trust proposal review clarification

Read-only review identified ambiguous worker networking and supervisor placement.
Clarified the proposal: actual tests retain isolated job-local service networking;
external egress, metadata/controller/signing and cross-run access are denied by an
external boundary. Surface restore still has no network. Candidate rootful Docker
access makes the entire worker VM untrusted; the trusted supervisor and completion
credentials are outside that VM. External lifecycle status alone does not establish
that assertions ran, and in-VM status/JSON remains untrusted. Added planned tests
for permitted local networking, denied external access and hostile daemon access
attempting to forge supervisor completion. These tests were NOT RUN; no service or
authority was provisioned. Documentation-only `git diff --check`: PASS.

## PostgreSQL patched-image feasibility — 2026-11-12

Resumed current branch `feat/wave-0-plan-4-ci` at `94c6d68`; read AGENTS.md,
this handoff, verification ledger and existing normalized image probes. Preserved
unrelated untracked Plan 3 handoff. Local diagnosis only, no subagents/worktree,
remote writes or production image/default changes. Used systematic debugging,
verification-before-completion and Tavily source extraction. No suppression,
waiver, package removal, major-version change or privilege-drop bypass.

Authoritative upstream checks:
- [PostgreSQL version policy](https://www.postgresql.org/support/versioning/)
  lists 18.6 as current supported minor for 18, supported until 2030-11-14.
  Changing major version is not a security shortcut; it requires upgrade evidence.
- [Official Debian Dockerfile](https://github.com/docker-library/postgres/blob/master/18/bookworm/Dockerfile)
  installs gosu as a separately downloaded signature-verified binary. It is outside
  dpkg; apt upgrades cannot patch its Go stdlib or `golang.org/x/sys`.
- [Debian krb5 tracker](https://security-tracker.debian.org/tracker/CVE-2026-107364)
  explicitly lists bookworm 1.20.1-2+deb12u5 and trixie 1.21.3-5+deb13u1 as
  vulnerable, with unstable also unfixed. Thus a gosu-only rebuild plus normal apt
  upgrade does not establish a clean Debian candidate. This is one confirmed
  blocker, not an exhaustive adjudication of every distro finding.
- [Upstream gosu module](https://raw.githubusercontent.com/tianon/gosu/master/go.mod)
  still requires x/sys v0.1.0. Recompiling unchanged source with a newer Go compiler
  leaves that dependency finding unresolved. Previous release-binary comparison
  remains diagnostic, not accepted clearance.
- Official Alpine 3.23 package pages provide [krb5-libs 1.22.3-r0](https://pkgs.alpinelinux.org/package/v3.23/main/x86_64/krb5-libs)
  and [libxslt 1.1.43-r4](https://pkgs.alpinelinux.org/package/v3.23/main/x86_64/libxslt).
  [Official Alpine PostgreSQL Dockerfile](https://raw.githubusercontent.com/docker-library/postgres/master/18/alpine3.23/Dockerfile)
  retains PostgreSQL 18.6 and gosu1.19, but uses musl and postgres UID70 instead of
  Debian UID999. That difference needs explicit compatibility/volume testing.

Actual local diagnostic command:
`python3 -u .local/remediation/image-triage/alpine_patch_probe.py` PASS exit0.
It built an ephemeral derivative of the previously verified immutable
`postgres:18.6-alpine@sha256:9ba53c7d41ea093baf5c1ba635ba5434c4f979880527d49bb7d9237d3193085b`
using only `apk upgrade --no-cache krb5-libs libxslt` against the image's normal
signed repositories. This is an exploratory build, **not reproducible production
provenance**: package repository state was not snapshotted/pinned. Own temporary
Dockerfile was deleted; diagnostic tag removed. Actual output config ID:
`sha256:ca74c5a1c6938418c2a6872ecbd8352f2a904592a24b35e6dadd0fb937cb6f68`.
Pinned Trivy0.76.2 scan by config ID, explicit task cache, `--skip-db-update
--skip-java-db-update --scanners vuln --list-all-pkgs --format json`: **57 packages,
25 HIGH / 1 CRITICAL / 2 UNKNOWN**. All 28 remaining rows are embedded gosu Go
stdlib (27) and x/sys (1); previous 19 krb5/libxslt rows disappear. Actual build
and scan exited0; scanner exit0 is not policy success. Raw output remained in
private-command temporary storage and was deleted. Skip-update diagnosis makes
no DB freshness, mandatory image-gate or license-clearance claim.

Concrete options and decision:
1. Retain Debian pin pending vendor fixes: least compatibility change, still
   blocked by distro findings and gosu; wait for fixed signed packages and an
   upstream binary with patched compiler **and** x/sys, then rescan exact digest.
2. Debian custom rebuild: patched gosu alone is insufficient. Requires reviewed
   vendor patches/backports for unresolved linked libraries, reproducible signed
   package inputs/build closure and full compatibility tests. No verified patch
   set or supported clean artifact was established; do not cherry-pick packages
   from another Debian release or remove linked features to hide findings.
3. Alpine18.6 custom rebuild: actual package probe demonstrates a narrower path,
   but still blocked by gosu28. A candidate needs immutable compiler/base/source
   and full Go module/checksum closure, reviewed x/sys upgrade to at least the
   scanner fixed version0.44.0, patched supported Go toolchain selected from the
   complete current finding set, repeated identical-output builds, SBOM/license
   coverage and fresh mandatory scans. This would be a maintained downstream
   build, not an upstream-endorsed replacement. Do not install Alpine gosu1.17
   blindly; it is an older implementation and no clean scan was established.

Before any adoption, require actual root-to-postgres privilege drop, supplementary
GID/UID semantics, failed-drop rejection, signal/exit propagation, secret-file
permissions, first initialization, idempotent restart, existing-volume ownership,
locale/collation behavior, migration/concurrent tenant tests, outage/recovery,
backup/restore and remote DemoModule acceptance. Alpine collation/UID differences
require migration/rollback planning; a clean scan alone cannot establish these.
No compatibility suite was run because no policy-clean candidate was established.

Source discrepancy retained for independent review: the local Alpine scanner maps
CVE-2026-87636 to libxslt, while the [Debian tracker](https://security-tracker.debian.org/tracker/CVE-2026-87636)
associates that ID with Chromium. This does not remove or waive the finding; the
upgraded local candidate was actually scanned and still blocked. Full source-feed
adjudication was not completed.

Verification: build/scan probe above PASS as diagnosis; `git diff --check` PASS
before commit. Unit/integration/full CI, fresh mandatory scans, exact final-SHA
backend/Compose, remote gates and restore drills NOT RUN in this documentation
continuation. Changed tracked files: this handoff and verification ledger only.
Rollback: revert this documentation commit. Wave0/P0/RT-* remain OPEN.

### Actual downstream gosu/source and offline APK continuation

Parent requested an actual source build and subsequently directed retaining the
runtime default until UID/libc/collation and license compatibility are established.
The earlier feasibility-only statements above are superseded by these additional
actual diagnostics; no production image change or adoption was authorized here.
Reviewable inputs/trade-offs are now in
[the candidate artifact](../ci/candidates/postgres-alpine18/README.md) and its JSON.

Verified Go1.27.4 stable from official go.dev download JSON, linux-amd64 archive
SHA256 `7226736c2339719cda28fbf3a54d71aa63bcefe376e98cef00034268dab24840`.
Resolved upstream gosu1.20 tag through GitHub API to immutable commit
`29b939aa5bd4dc588af765b8248f6e39150d2565`; no source signature verification claim.
Actual module tooling upgraded x/sys0.1.0 to0.44.0 and minimum go1.20 to1.25.0;
`go mod verify` and static compile with that verified Go toolchain PASS. Exact
module/checksum contents are recorded. Final `-trimpath -buildvcs=true` binary
SHA256 `732a3a2fbd309eeeb685b3469f970f178a7da17ad92a834a5859d19146f85808`.
No upstream arbitrary helper/install script was executed. Local source code was
compiled as explicitly requested; all captured diagnostics remained private.

Initial environment override to private-command was rejected; corrected to an
explicit minimal owned environment with in-memory subprocess capture. Initial
`-buildvcs=false` binary scan had zero HIGH/CRITICAL/UNKNOWN but mandatory
`scan_image` correctly returned INCOMPLETE because main gosu module Version was
missing. Honest VCS metadata fixed inventory without inventing a version. Two
initial non-VCS compiles matched bytes; final VCS-aware full reproducibility has
not been established.

Actual online candidate with repository wrappers, config
`sha256:15e8a3077bdf3236c98d595cd3b992bcf00dcbf371cba6bd1260c0626545f9f7`:
mandatory `scan_image` **PASS CI_IMAGE_SCAN_OK**,57 packages, SBOM SHA256
`ce1b3c1ccfec98bffc65d319f739c870b4be499af61f5a840ac6b6990ea13bee`;
residual zlib CVE-2026-4738 MEDIUM, installed1.3.2-r1/fixed1.4-r0.
`gosu_candidate_probe.py` PASS: drop to UID70, reject invalid identity, preserve
exit17, actual local wrapper first initialization, PostgreSQL18.6 and restart.
`gosu_compat_probe.py` PASS: primary/supplementary GID70, explicit TERM handler
receives signal and returns42, missing secret closed, idp bootstrap creates
restricted keycloak role. Initial naive PID1 sleep signal harness hung because
PID1 ignores default signals; killed only owned container and corrected the test
to use an explicit handler. Initial harness FAIL is not a product failure.

Actual signed Alpine12-APK recursive closure fetch/checksum verification PASS,
manifest recorded in artifact. Offline `docker build --network=none` with only
those checksum-verified APKs PASS. No `--allow-untrusted` or signature bypass.
The offline closure also upgrades zlib1.4-r0. Offline image config is recorded in
artifact; actual repository `scan_image` **PASS CI_IMAGE_SCAN_OK**,57 packages,
**zero vulnerability findings**, SBOM SHA256
`61a40fc89115b1f972bb42193393bdbcd03c55c6efed3860a0922a2f14d1c5dd`.
Both mandatory scans downloaded DBs through normal commands and accepted actual
UpdatedAt timestamps: vulnerability2026-11-11T19:04:54.958468957Z,
Java2026-11-12T00:58:04.30813481Z (fresh at scan time). Offline image did not include
local wrappers and did not receive wrapper compatibility tests; no inference from
online config is accepted as exact-image test evidence.

Actual separate `--scanners license --license-full` scan:71 license rows,
15 identifiers outside existing allowlist. No license clearance: GPL/LGPL/MPL,
ICU, OLDAP, X-11, curl, Zlib and others require existing review rules. Current image
adapter scans vulnerabilities/SBOM and does not adjudicate license rows. Do not
call image-gate PASS full license clearance. Mandatory scripts and policy unchanged.

Commands (local ignored image-triage directory): `gosu_build_probe.py` PASS final;
`gosu_candidate_probe.py` PASS final; `candidate_gate.py` PASS executed against
both online/offline config IDs; `gosu_compat_probe.py` PASS final;
`apk_snapshot_probe.py` PASS; `apk_offline_probe.py` PASS. No raw reports published.
Artifacts retain hashes and authored normalized observations only. New runtime
implementation/tests NOT added because adoption remains blocked; no TDD GREEN
claim. Full backend/Compose/tenant/concurrency, existing-volume/collation migration,
logical restore/rollback and remote acceptance NOT RUN. Next steps and precise
migration risks are in candidate README. No contract/dependency/default/policy
change; Wave0/P0/RT-* remain OPEN. Documentation `git diff --check` PASS.
