# Wave 0 Plan 4 verification — 2026-11-11

> **Cập nhật phạm vi 2026-11-12:** [Quyết định CI MVP](09-WAVE-0-CI-MVP-SCOPE.md) xác định điều kiện exit hiện hành và các mục chuyển sang hardening. Bảng W0P4 bên dưới là evidence lịch sử theo scope cũ; Wave 0 vẫn OPEN và chưa có full gate mới pass.

**Wave 0 OPEN; P0 and every RT-* OPEN.** Local implementation is reviewable; acceptance has not passed. This ledger consolidates recorded Tasks 1–7, not a new execution of their suites. No remote workflow, push, merge, deployment, repository setting or secret change was authorized/performed for Task 8.

## Revision and review boundary

Branch `feat/wave-0-plan-4-ci`; Task 8 started clean at `413601ea17bc6d4e34dc2ecdd41ff8f8b85a64dc`. Plan 4 baseline `c4a5ef40a34daded023d5e5121c0462e94cc1487`; review with `git diff c4a5ef40a34daded023d5e5121c0462e94cc1487..413601ea17bc6d4e34dc2ecdd41ff8f8b85a64dc`. Latest runtime rehearsal is `7a6464b16a1034f4326b9b10da48438c393459f0`, **not** final acceptance SHA. Subsequent publication fixes `fadc2a96cbc78e86bb8db4718c07daee537c7fbf` and `413601ea17bc6d4e34dc2ecdd41ff8f8b85a64dc` have focused verification only. A final exact-SHA full-job run is missing.

[Spec](../superpowers/specs/2026-11-11-wave-0-plan-4-ci-wave-exit-design.md), [plan](../superpowers/plans/2026-11-11-wave-0-plan-4-ci-wave-exit.md), [CI policies and report boundary](../../tools/ci/README.md), and tracked [Task 7 handoff](../handoff/2026-11-11-wave-0-plan-4-task-7-handoff.md) supply detail. Tasks 1–7 handoffs in the same directory are the durable per-task evidence. Ignored `.superpowers/sdd/2026-11-11-wave-0-plan-4-ci-wave-exit/task-{1..7}-report.md` contain local detail and are not portable CI artifacts.

## Acceptance mapping

No ID below is a full acceptance PASS; local negative tests prove rejection behavior only.

| ID / tasks | Actual local evidence | Status and missing evidence |
|---|---|---|
| W0P4-01 / 1,4 | Locked runtime installs/build and clean Git archives; exact e186fb4b759e59c2f7e58e37a793ff580272ec3c contracts/browser rehearsal; exact 7a6464b archive npm ci 254 packages, 7s; build 3.205s | OPEN: final-SHA full job set and actual hosted runner identity/install unavailable |
| W0P4-02 / 4 | e186 backend FAIL CI_COMMAND_FAILED, 950.6699s, 0 collected; Compose FAIL same reason, 2494.0634s, 0 collected. Separate backend diagnostic 70/70 pass, 730.7874s | FAIL/OPEN: diagnostics do not replace failed jobs. 7a6464b selective Testcontainers probe FAIL 1/1, 0 skips, 18.241s at mandatory image gate; full backend/Compose rerun required |
| W0P4-03 / 3,4 | e186 contracts runner PASS 87, 0 failures/skips, 3175.5879s, SDK/consumer and five harness command path. Task3 aggregate-reporting 16/16; ci-environment 87 total/32 pass/55 expected named simulation skips | OPEN: historical prepared pass only; final-SHA full gate missing. Simulation skips never count as real full-suite pass |
| W0P4-04 / 4 | e186 browser gate PASS 58=54 Vitest+4 real browser, 0 skips, 1113.3042s. Exact 7a6464b Playwright PASS 4/4, 0 skips, 11.563s with prepared host libraries and owned API/PostgreSQL | OPEN: latest browser is diagnostic after image rejection, not successful job. Earlier b4d70f3 launch FAIL 4/4 missing libatk, then diagnostic PASS 4/4 |
| W0P4-05 / 1,2 | Revision tests 12 pass at b6b68785ce925e29b08d42f83bcfce4abdd620fd; immutable signed dual-policy real semantic tests at 6c9f8a2356040e74ef996b23ef2f87d351ebc335: CI23 pass/387.677s; compatibility39 pass/1024.499s; SDK surface6 pass/77.552s, all zero skips | BLOCKED: independent trusted verifier, keys, policy inventory and isolated remote runner not provisioned; real PR candidate provenance not executed remotely |
| W0P4-06 / 1,2 | Push before/after ancestry, forced/zero/missing/nonancestor rejection and signed first-push/rollback fixture coverage | OPEN: fixtures are local; no actual main-push run or reviewed merge/tree binding. merge_group unsupported |
| W0P4-07 / 5,6 | Real security fixture suite12 pass, 0 skips, 26.809s at Task7 checkpoint. b4d70f3 Semgrep1017/1017 pass; Trivy10 locks with271 license-review findings and coverage gaps; Gitleaks4 findings. 7a6464b security FAIL CI_SCAN_INCOMPLETE23.372s | BLOCKED: actual findings, tool/unlocked dependency gaps and image/unknown findings remain. Source inventory INCOMPLETE10 locks/444 components |
| W0P4-08 / 5,6 | Captured process/scanner sentinel fixtures; guarded source/image SBOM; direct argument override sentinel regression. Task6 .NET guard4 pass/60ms and real argument cases6 pass/5s; image guard22 pass/0.246s. Final publication SIGTERM regression7 pass/0.736s at413601e | OPEN: bounded synthetic coverage only, no final complete log/artifact/config/image audit or remote publication proof. Projection does not recognize arbitrary secrets disguised as valid identities; pre-rename/non-started completion cannot quarantine producer artifact |
| W0P4-09 / 3,7 | Missing/failed/skipped/cancelled/wrong-run/zero/partial/required-test skip rejection; deliberately failing Local fixture; actual failed-security aggregate returns CI_JOB_NON_SUCCESS and safe correlation. Final aggregate7 pass/0.447s | OPEN: actual remote scheduler/job/whole-workflow cancellation not run; unscheduled aggregator has no fabricated report |
| W0P4-10 / 7 | Workflow9 focused pass; pinned actions/read-only/no secrets/no caches/exact expression allowlist checked | OPEN: static contract checks only; fork/hosted execution and independent enforcement unproven |
| W0P4-11 / 7,8 | Exact7a6464b archive/readiness96.729s followed by CI_IMAGE_EVIDENCE_INCOMPLETE; owner-only cleanup verified0 containers/networks/volumes | FAIL/OPEN: local gates rejected. Remote PR/main runs, URLs, base/head/candidate/final/merge-method proof, runner identity and quota NOT RUN/unknown |
| W0P4-12 / 8 | This ledger, matrix/roadmap/status and handoff updated; governance read-only inspection below | OPEN: failing gates, independent trust and missing remote evidence prevent closure. Governance unknown alone would not prevent technical exit if all other evidence passed |

## Tool and scanner identity

Recorded local runtime: Python3.15.2, Node24.22.2, npm11.19.1, .NET SDK10.0.403, Docker29.9.3, Compose5.7.0; supported dedicated Python3.12 scanner runtime. Host native libraries were prepared locally, not a hosted runner result. Exact action SHAs are in `.github/workflows/wave0.yml`; scanner archive/executable hashes are in [toolchain lock](../../tools/ci/toolchain.json). Semgrep CE1.185.0 uses pinned C#/TS intrafile rules, including generated code; no broad taint/cross-file guarantee. Gitleaks8.30.1 scans candidate and complete trusted commit range (including introduced/deleted and merge fixtures), without provider verification. Trivy0.76.2 checks upstream DB UpdatedAt with maximum86400s; historical vulnerability DB `2026-11-11T13:00:47.186797699Z`, Java DB `2026-11-11T01:07:27.52801824Z` were fresh at those scans only. No current freshness claim.

[Security policy](../../tools/ci/security-policy.json): HIGH/CRITICAL block; unknown severity/database/tool/coverage failure closes; every active secret finding blocks including unverified; restricted/unknown licenses require review; finding-bound independent approvals required, no active exceptions. The local launcher hash does not establish independent authority. Hosted scanner retrieval/provisioning remains unproven.

Exact7a image retry: API BLOCKED106 packages/107 components/5 blocking; migrator BLOCKED120/121/5; PostgreSQL INCOMPLETE151/152/135 blocking+12 unknown; Keycloak BLOCKED559/560/6. All are linux/amd64 local config IDs, manifestDigest=null. Original zero-package startup/probe failures remain failures. Exact7a normalized local records `.local/task7/image-retry-final.json` bind run7/job web-browser to these actual tested config IDs (independent retry, not a passing job):

| Role | Local config ID |
|---|---|
| api | sha256:dff078afc5aa2ca965a2120072c511cfacc144d9c7d221881ff32733d74ac062 |
| migrator | sha256:5d718dfb1c4a39b4bd0400e218161f8a73380d3678aa75fc93c2ad980929f0a5 |
| postgres | sha256:a823df4bddf1ea8b49b72e32ce1e2d526d11bfad0ebee45dd9970e3d08d1b2a0 |
| keycloak | sha256:6a23731369ebf6af3ec164c563fe5f914c2f366618ef7d59fc715e94e2c22d0d |

No image config ID is a registry manifest digest. These are local records, not remote artifacts. Artifacts proposed for upload are normalized per-job/aggregate JSON and guarded source SBOM, seven-day retention; raw reports, browser media, inspect/history, credentials and payloads are excluded.

## Verification provenance and limits

Task7 full pinned CI109 PASS/0skips/388.397s began on b4d70f3e3b8254a8175dee2e679ef7674cf582bd before lint-only7a correction. Focused workflow9 covers that correction. It is not immutable final-SHA full-service evidence. Task7 final publication fixes independently passed completion7 and aggregate7; full CI/service suite was not rerun. Earlier system-Python Contract59 failed15+2errors/7skips due absent validator/unusable Node; later prepared targeted tests and e186 Contract87 pass are separate results, not rewritten history. Plan3 historical remote run42987032191 with55 skips is insufficient. Task8 documentation verification results are recorded in its handoff.

## Remote/governance state and proposed checks

Read-only `gh repo view --json nameWithOwner` identified `leduc4894/crm-centric`. On2026-11-11 `gh api repos/leduc4894/crm-centric/branches/main/protection` failed exit1; error body was not published. `.../rulesets` and `.../rules/branches/main` returned empty lists. Branch protection/required-check enforcement **UNKNOWN/OPEN, not VERIFIED**; empty visible rules do not prove complete governance absence. Merge queue UNKNOWN/OPEN; do not enable until merge_group provenance support and evidence exist.

Proposed workflow `Wave 0` jobs/check names: `backend`, `compose`, `contracts-sdk`, `web-browser`, `security`, `wave0-required`; observed hosted check contexts/app identity must be confirmed before any separately authorized rule change. The old `contracts.yml` is removed locally; migrate any obsolete required context explicitly to avoid pending checks. Aggregate needs exactly the five gates and only platform success plus valid same-run evidence passes. Timeouts backend/security30min, Compose/contracts60min, browser60min are bounded proposals, not measured hosted guarantees. No remote PR/main/fork/cancellation run URLs, hosted duration/minutes/quota or designated acceptance SHA exist for Plan4.

## Review and rollback package / next steps

Review the full baseline..413601e diff and Task8 documentation diff. No public contract/schema change, database migration, deployment or image/package publication. Revert scoped workflow/config/code commits for rollback; preserve required-check context compatibility and inspect governance before any authorized remote change. Do not waive findings, relax baselines or reset database volumes to obtain green. Policy reversal requires separately reviewed reverse transition; no operational transition route is enabled.

Remediate private secret/license/vulnerability findings and dependency coverage; provision independent verifier/key/inventory/isolation with separate authorization; prove hosted tool installs. Diagnose the original backend/Compose failures and rerun the complete exact final candidate gates with fresh DB evidence and secret-safe artifacts. Only after separate remote authorization run PR/main/fork/failure/cancellation scenarios, bind reviewed candidate/tree/merge method to final main SHA and review all W0P4 IDs. Remote DemoModule E2E, concurrent duplicate, tenant isolation, outage/recovery and restore drills remain P0/RT-* work; this ledger closes none of them.

## Final local review corrections

The final review adds a producer-to-completion regression for allowed Trivy license
rows. Successful source scans and completion now share strict validation of opaque
finding IDs, known non-secret kinds and `pass` verdicts; blocked, unknown and
malformed records remain rejected. npm/NuGet inventories retain meaningful dots
and underscores, so NuGet SBOM purls preserve package identity and punctuation-
distinct scanner inventory is rejected. PyPI punctuation folding remains scoped
to Python requirements. These are local correctness fixes, not new clearance of
repository findings, independent trust or any W0P4/P0 acceptance. Actual commands
and results are recorded in the [final-review handoff](../handoff/2026-11-11-wave-0-plan-4-final-review-handoff.md).


## Local backend/Compose remediation — 2026-11-12

No acceptance status changes. [Remediation handoff](../handoff/2026-11-12-wave-0-plan-4-remediation-handoff.md)
records the actual commands, safe diagnosis, RED/GREEN evidence and limits.
Commit `6562e89` corrects the CLI/imported dataclass identity mismatch that rejected
equal image fields before scanning; `a458e20` preserves explicit private fixture
snapshots when unreadable-secret tests revoke file access, retaining unchanged
file modes, safe unknown-extraction errors and mandatory scanning. No production
API/schema, dependency or security-policy change.

Pinned Python CI: 116 PASS/0 skips/333.680s after the Python correction. Real
scanner fixtures: old-cache FAIL 1/12 (stale Java metadata), then fresh task-owned
cache PASS 12/0 skips/25.160s. Normal fresh retrieval obtained upstream Java DB
`2026-11-12T00:58:04.30813481Z` and vulnerability DB
`2026-11-11T19:04:54.958468957Z`; no timestamps were edited or age limits waived.
Focused .NET fixture tests after the second correction: 6 PASS/0 skips/44ms.
These fixture successes do not clear actual findings.

Exact implementation `a458e205ce2e0221f57257b3edfd25efec6e5bd6` full backend
FAIL `CI_COMMAND_FAILED`/182.334s: native 76 executed = 29 passed + 47 failed,
0 skips; every failure explicitly rejects mandatory image evidence, with no
other or permission-extraction failure. Actual API/migrator/Keycloak inventories
106/120/559 packages remain BLOCKED by 5/5/6 findings; PostgreSQL 151 packages
remains INCOMPLETE with 135 blocking + 12 unknown. Strict failed-summary parsing
still emits zero job cases; native diagnostic counts are not passing evidence.

Same exact SHA selective real Compose cold-start probe FAIL 1/1/0 skips/90.013s
at first API scan (106 packages, 5 blocking). Full final-correction Compose and
complete final five-job gates were NOT RUN; earlier full Compose failure and
historical Task 4 unknown cause remain recorded. Final owner-labelled resources
0 containers/0 networks/0 volumes; foreign container preserved. Final subsequent
commit is documentation only. All original security/coverage/trust/remote and
Wave 0/P0/RT-* blockers remain; no suppression or remote action occurred.

## 2026-11-12 image remediation checkpoint

Local private pinned triage reproduced API/migrator 5 HIGH each, PostgreSQL
131 HIGH / 4 CRITICAL / 12 UNKNOWN, Keycloak 6 HIGH. Runtime findings originate
in apt/libapt and ASP.NET 10.0.14. Official MCR 10.0.15-noble manifest verified
and pinned in runtime Dockerfile/default; existing published-runtime test now
asserts both shared frameworks at 10.0.15. No SDK, NuGet, contract or policy change.

Actual selected published-image test: RED 1 failed/0 skipped on old mandatory
image gate (20.214s); GREEN 1 passed/0 skipped after patch, plus 20 Local passes.
Built API/migrator mandatory fresh scans each PASS CI_IMAGE_SCAN_OK,
106/120 packages, zero blocking/unknown. Overall selected runner remains
CI_DISCOVERY_INCOMPLETE (27.702s), correctly rejecting partial backend coverage.
This checkpoint is working-tree diagnostic evidence, not exact-SHA full gates.

Same-server PostgreSQL Trixie alternative was privately evaluated and not adopted:
149 packages, 147 HIGH / 2 CRITICAL / 17 UNKNOWN. Keycloak official latest remains
26.8.2; no verified supported replacement clears its OS/Guava findings. No vendor
JAR swapping, CVE suppression or unknown-severity downgrade. Detailed normalized
root causes, primary-source links, actual commands/results and next steps are in
[remediation handoff](../handoff/2026-11-12-wave-0-plan-4-remediation-handoff.md#image-dependency-remediation--2026-11-12).
Wave 0/P0/RT-* remain OPEN; source/security/trust/remote acceptance blockers persist.


## Exact bf2921f backend and upstream image follow-up — 2026-11-12

Verified completed full backend at `bf2921f16a37617dda2a781652ab8866fca2a50f`:
FAIL CI_COMMAND_FAILED / 218.563s, native 76 executed = 36 passed + 40 failed,
0 skipped. All 40 fail mandatory image gates; no other exception category.
API/migrator actual enforced scans PASS (106/120 packages, no blocking/unknown);
PostgreSQL INCOMPLETE (151, 135 blocking + 12 unknown); Keycloak BLOCKED (559, 6).
Strict failed-TRX parsing retains zero outer cases. No backend acceptance pass.

PostgreSQL Go target confirmed as `/usr/local/bin/gosu`, actual upstream 1.19
binary hash match, built Go1.24.6/x/sys0.1.0; not dpkg-managed. Latest official
gosu1.20 retains these compiler/dependency versions, so is not a demonstrated
fix. Official PostgreSQL18.6 Alpine immutable candidate scan: 57 packages,
43 HIGH/1 CRITICAL/3 UNKNOWN; rejected. Current Keycloak26.8.2 tag resolves to
new manifest `sha256:723b1d6e254c0fc96d5cffec860c7e0744d6a3277822b865bb538cf891575e41`;
exact private candidate scan retains 559 packages/6 HIGH with unchanged affected
versions; rejected. These skip-update candidate scans are diagnosis, not fresh
gate clearance. No dependency/policy/code change or vendor JAR replacement.

Exact commands, immutable candidate IDs, primary-source provenance, verification
limits, next steps and ownership cleanup are in the
[remediation handoff](../handoff/2026-11-12-wave-0-plan-4-remediation-handoff.md#exact-runtime-commit-and-remaining-image-diagnosis--2026-11-12).
Full Compose/final five gates/remote/restore NOT RUN. Wave0/P0/RT-* remain OPEN.

## Dependency coverage isolated probes — 2026-11-12

At `eddaa35`, disposable pinned SDK restore produces a valid Surface lock with
net10.0/zero packages; real pinned Trivy schema 2 emits zero Results. Existing
adapter correctly remains fail-closed, and no empty source lock was accepted.
CorvusProbe inherited restore reproduces NU1008; one diagnostic global property
`ManagePackageVersionsCentrally=false` makes restore pass with six lock rows.
Failed restore also writes six rows, so a lock's presence is not successful
restore evidence. All probe locks were deleted; generated provenance unchanged.
Corvus CLI archive embeds 238 deps.json libraries, including 34 package libraries;
these counts do not prove complete tool closure or license clearance.

Actual completed private probe exit 0; initial no-cache Trivy attempt failed,
then explicit existing-cache skip-update rerun established parser behavior only.
`python3 -m unittest discover -s tests/CI -p test_scan.py`: PASS 21, 0 skips,
0.359s. No implementation change/new RED-GREEN/full-suite/final gate/remote run.
Concrete zero-package attestation, generator/CPM and tool-closure blockers remain
in the [handoff](../handoff/2026-11-12-wave-0-plan-4-remediation-handoff.md#dependency-coverage-diagnosis--2026-11-12).
Wave 0/P0/RT-* remain OPEN; no policy or contract change.

Empty-lock follow-up at `491e74c`: actual pinned-SDK import-tamper probe exit 0.
Initial real restore succeeds with empty lock; adding PackageReference through
an imported props file leaves identical lock bytes while locked restore fails
NU1004. The lock-only adapter has no authenticated evaluated-restore input, so
Trivy's absent empty target cannot prove valid zero dependencies. Independent
restore producer/isolation/input binding is a concrete prerequisite; candidate
JSON/hash or static direct-reference checks cannot supply it. No implementation,
new tests or policy relaxation; existing empty locks remain fail-closed. Details
and required evidence boundary are in the remediation handoff's empty-lock
implementation feasibility follow-up. Full gates and remote/acceptance NOT RUN.

PostgreSQL feasibility follow-up (2026-11-12, starting94c6d68): official Debian
krb5 tracker confirms CVE-2026-107364 unfixed in bookworm/trixie; separate gosu
binary is outside dpkg and upstream module retains x/sys0.1.0. Actual local
Alpine18.6 immutable-base derivative with signed-repository krb5/libxslt upgrades
built and scanned successfully as a diagnostic: config
`sha256:ca74c5a1c6938418c2a6872ecbd8352f2a904592a24b35e6dadd0fb937cb6f68`,
57 packages,25 HIGH/1 CRITICAL/2 UNKNOWN; all28 remaining rows in gosu stdlib/x/sys.
Command `python3 -u .local/remediation/image-triage/alpine_patch_probe.py` exit0,
pinned Trivy0.76.2 skip-update cache; no freshness/gate clearance implied. No
candidate adopted; package inputs were not snapshotted and compatibility untested.
Detailed sources, three options, build/test prerequisites and source discrepancy
are in the same-day remediation handoff. No production/default/policy change.
Full CI/backend/Compose/remote/restore NOT RUN; Wave0/P0/RT-* remain OPEN.

Further actual PostgreSQL source-build spike supersedes feasibility-only status:
verified stable Go1.27.4 archive, upstream gosu1.20 commit29b939a, checksum-verified
x/sys0.44.0 closure, honest VCS metadata. Online wrapper config15e8a307 passed
actual `scan_image` (57 packages;1 MEDIUM zlib); first-init/restart, UID/GID/drop,
failed identity, exit/signal propagation, missing-secret and restricted idp-role
checks PASS. Exact12-APK checksum manifest and offline no-network build PASS;
offline config recorded in [review artifact](../ci/candidates/postgres-alpine18/README.md)
passed actual `scan_image`,57 packages,zero vulnerabilities, fresh DBs,SBOM
`61a40fc89115b1f972bb42193393bdbcd03c55c6efed3860a0922a2f14d1c5dd`.
Offline image lacks local wrapper and did not receive online-wrapper tests.
Separate full license scan71 rows/15 identifiers require review; no license or
full acceptance clearance. Parent directed no runtime adoption until compatibility
and license established. Source artifact is diagnostic only; reproducibility,
existing-volume/musl collation migration, full integration/restore/rollback and
remote tests remain NOT PROVEN. Same-day handoff records initial failed diagnostic
harnesses as well as final passes. Default and policy unchanged; Wave0/P0/RT-* OPEN.

### Keycloak remediation investigation (2026-11-12)

At01aef5c local baseline exact pinned26.8.2 private Trivy0.76.2 diagnostics:
559 packages/6 HIGH, full license52 rows/20 identifiers outside current allowlist.
Normal GPG-enabled UBI9.8 upgrade/install with same distribution built successfully;
actual immutable config scan722 packages/36 HIGH including original six. Broader
UBI package set is not a minimal-rootfs equivalence claim. Explicit skip-DB-update
diagnostics do not establish freshness or mandatory gate success. Official current
Keycloak download26.8.2; registry26.8.3 lookup failed. Official Guava33.7.2 fixes
embedded Guava CVE; base/JDK alone cannot clear it. No policy-clean supported
candidate or license clearance established; default remains blocked unchanged.
[Inputs, official sources, tradeoffs and next tests](../ci/candidates/keycloak26/README.md).
Probe own tag/Dockerfile removed, no foreign cleanup or remote operation.
Full CI/auth/DB/restore/acceptance NOT RUN; Wave0/P0/RT-* OPEN.

Keycloak follow-up: unchanged official minimal packaging JDK21 actual559/6 HIGH;
supported UBI JDK25 actual560/7 HIGH. Temurin25 Noble actual108/0 reported HIGH
but no JVM in scanner inventory despite actual Java25.0.5: runtime coverage gap,
not clearance. Actual official-source26.8.2 server rebuild with source Guava33.7.2
management patch PASS224.962s after normal signed build-only ICU prerequisite.
Source tar/patch/tool checksums and observed483 runtime JAR hashes recorded in
candidate artifact. Exact built UBI21 source runtime postgres/health augmentation
PASS; vulnerability scan559 packages/**5 HIGH**, Guava correctly33.7.2-jre with
finding removed. Remaining Java1/SQLite4 block. Tests explicitly skipped for
feasibility build; complete Maven lock/reproducibility, fresh mandatory image gate,
full license/auth/DB/restore/acceptance NOT RUN/unproven. No default/policy change.


## MVP exact-candidate rehearsal — 2026-11-12

Local candidate `5a17535b5da6802dde0548e7fe0aa0bef2a3ca2c`, base
`601dde8847cad82d038ea837c01d3da0d7449450`, no merge SHA. Run
`local-mvp-task3`, attempt 1, on branch `feat/wave-0-plan-4-ci`. These are
actual full-job attempts using explicit `--mode mvp`; failed or missing stages
are not converted to success by collected passing cases.

| Job | Actual original result | Counts and duration |
| --- | --- | --- |
| backend | PASS | 96/96, 0 failures/skips; 436.611s |
| compose | PASS | 14/14, 0 failures/skips; 1400.580s |
| contracts-sdk | FAIL `CI_COMMAND_FAILED` | 87 contract cases passed, 0 failures/skips; job incomplete; 1140.515s |
| web-browser | FAIL `CI_COMMAND_FAILED` | 0 collected; 30.681s after native-library preparation |
| security | FAIL `CI_SCAN_INCOMPLETE` | Required scanner blockers remain; 6.379s |

[Compact safe job evidence](evidence/2026-11-12-mvp-rehearsal-5a17535.json)
binds counts, reason/correlation, tool versions and run identity to that candidate.
Each test job command is `python -m scripts.ci.jobs JOB --mode mvp --revision
.local/ci-task3/revision.json --output .local/ci-task3/JOB.json`; security uses
`python -m scripts.ci.security --mode mvp --revision .local/ci-task3/revision.json
--output .local/ci-task3/security.json` with `CI_SCANNER_ROOT=.local/task5`.
Runtime pins: Python 3.15.2, Node 24.22.2, npm 11.19.1, .NET 10.0.403, Docker
29.9.3, Compose 5.7.0; scanners Gitleaks 8.30.1 and Trivy 0.76.2. Host PATH and
LD_LIBRARY_PATH use the locally prepared pinned tools and extracted browser
native libraries. This is local preparation, not proof of hosted provisioning.

Security: Gitleaks BLOCKED 64 unique secret digests across 2 surfaces; Trivy
PASS under unchanged policy, 10 locks and 6 nonblocking vulnerability records.
`CI_UNLOCKED_DEPENDENCIES` remains required/blocking; only
`CI_TOOL_DEPENDENCY_COVERAGE` is deferred. SAST, images, license and tool inventory
remain OPEN. No finding approval, severity change, suppression or false-positive
decision was made. See [triage](10-WAVE-0-MVP-SECURITY-TRIAGE.md).

Bounded subprocess diagnosis confirmed the real Chromium launch passes with
prepared host libraries. The browser job then fails at Plan3 generated-check
with `SDK_GENERATED_MISMATCH`. Exactly five ignored restore files under
`sdks/dotnet/Crm.Client/obj` were present after local build activity; they were
preserved outside the source tree in ignored local scratch. The exact generated
checker correctly rejects these extras. Full clean-state reruns using the same complete production job path both PASS:
contracts-sdk 87/87, 0 failures/skips, 2070.489s; web-browser 58/58
(54 Vitest + 4 real Playwright), 0 failures/skips, 303.270s. Contracts rerun
completed generated-check (`SDK_GENERATED_CURRENT`), ClientConsumers, compatibility
(`CONTRACT_COMPATIBLE`) and all five Plan3 harnesses, including exact-SHA
clean-export with browser. The browser rerun completed the real Plan3 stack
lifecycle and native-report parsing. Diagnostic execution wraps the existing
runner subprocess boundary, preserving command order, timeouts and outputs;
only fixed safe classifiers are printed. These are complete reruns, not selective
replacements. Original failures remain recorded. No tracked runtime fix was
needed; local SDK contamination was the cause. Rerun counts/identities and exact
required scanner finding digests are in the compact safe evidence above.

Static verification: `python3 -m scripts.ci.workflow`, actionlint on
`.github/workflows/wave0.yml`, `bash -n scripts/ci/setup.sh`, and
`git diff --check 601dde8..5a17535` PASS. Initial full pinned CI regression FAIL125/126, 0 skips, 287.610s; only
`test_job_processes.ProcessTests.test_timeout_terminates_child_that_ignores_term`
failed. The safe reporter discarded its traceback, so its exact original
exception is UNKNOWN. Isolated process module then PASS4/4/1.507s; full
pre-fix diagnostic replay PASS126/126, 0 failures/errors/skips, 268.910s.

A controlled 0.75s startup delay reproduces a separate concrete fixture race:
`execute(timeout=0.5)` correctly terminates the command before child PID
publication, then the original test raises `FileNotFoundError`. The test
assumes startup completes within its timeout instead of synchronizing readiness.
Added slow-start case RED1 failure/5 tests/3.038s; minimal fixture readiness
synchronization GREEN5/5/3.867s. It wraps real Popen until the real child has
installed SIGTERM-ignore and published its PID, with a bounded 10s fixture
readiness deadline and owned-group cleanup on setup failure. Production execute,
half-second timeout and descendant termination assertions are unchanged.
This test-only correction does not establish the unknown original exception.
Final full post-fix CI regression PASS127/127, 0 failures/errors/skips,
320.490s, exit0. Readiness test fix and handoff committed as
`1ff6ca3145c4d1b23ec29805ad5e507870b38f36`. The full regression ran on that test-file
content before commit; full service/security reports remain bound to 5a17535.
Tasks 1–2 results remain historical separate evidence.

Actual-report local completion roundtrip: backend/Compose/contracts/browser
`CI_JOB_COMPLETE`, security `CI_JOB_FAILED`. Local aggregation of those five
reports with corresponding local job outcomes exits1/`CI_JOB_NON_SUCCESS`,
`success=false`; this is failure-preservation evidence, not remote scheduler
proof.

No remote PR/main run, protection/settings change, push/merge or deployment was
performed. Remote CI NOT RUN; repository enforcement UNKNOWN. This evidence
commit changes HEAD and therefore is not itself covered by the candidate runs.
Wave 0/P0/RT-* remain OPEN; no wave-exit acceptance is claimed.


Task 3 final review: implementation/workflow/fixture/test diff from601dde8 through
5a17535 and the readiness correction reviewed; no additional runtime change.
No public contract/schema, lock version or database migration change. Test-fixture
rollback is a scoped revert of its commit; evidence should remain historical.
Final workflow/actionlint/shell/Python AST/Markdown-link/JSON identity-count-digest
checks and `git diff --check` PASS. Final evidence-only commit changes HEAD again;
its full five-job suite is NOT RUN, and no acceptance is transferred. Concrete
next work: accountable private finding review, unlocked dependency remediation,
fresh agreed-candidate full gates and separately authorized remote CI.

## 2026-11-13 exact activation-candidate full local rehearsal

Candidate9706334d7778ebcc4f5704d5bcbfbc8ec68ef969, actual local-main merge-base c4a5ef40a34daded023d5e5121c0462e94cc1487; validated simulated local non-forced push, run local-mvp-9706334/attempt1. Earlier601dde8 is not substituted. Six pinned runtimes verified equal runner requirements. Full jobs ran sequentially: backend PASS96/0/0,449.680s; Compose PASS14/0/0,1401.193s; contracts FAIL87/0/0 collected,1184.342s; browser FAIL0collected,96.541s; security FAIL0/1/0,16.550s. Generic test-job failures are CI_COMMAND_FAILED. Real aggregate exit1/success=false/CI_DISCOVERY_INCOMPLETE. Original safe reports preserved privately, no native payload published.

Direct compatibility CLI fails CONTRACT_POLICY_REVIEW_REQUIRED. Actual Plan3 diagnostic entrypoint exit1/85.742s reports generated-current/package-boundary success then policy blocker before browser tests. Exact two policy changes: sdk_surface.py and new Surface lock. Immutable c4a5ef4 semantic module lacks extracted evaluation API; existing dual runner needs separately reviewed legacy bootstrap. Scope defers independent hardening but grants no automatic transition bypass.

Security required tree64 reviewed, history60BLOCKED/CI_SCAN_FINDINGS under real full feature range; repeated native IDs stable. All60 at commit5b19690a08f9c0bcf7c3edbf3bae1f15817c312e/diagnostic-runtime-jars.json match exact approved document bytes/location/rule and unique tree IDs, without extending approval to history. Trivy PASS11locks/6nonblocking findings, one actual empty-restore receipt. [Exact safe mappings/job evidence](evidence/2026-11-13-mvp-rehearsal-9706334.json), [pending review/options](evidence/2026-11-13-mvp-rehearsal-review.md), [commands/runtime/handoff](../handoff/2026-11-13-wave-0-mvp-rehearsal-handoff.md).

No source, policy, scanner, threshold, settings, approval or remote changes. Documentation commit itself has no fresh full gate run; no success transfers from9706334. Remote CI/enforcement and P0/RT-* acceptance drills NOT RUN; Wave0 OPEN. Documentation validation: safe evidence identity/count/digest invariants, relative links and git diff --check PASS (actual checks below in session). No full CI regression run for documentation-only additions.

## 2026-11-13 authorized balanced MVP resolution

User authorized exact60 historical metadata review and exact two-file policy
transition with real true-base semantics, without independent-verifier requirement
for MVP. Implemented7547e5a/d268ca7/8e67a18; isolated CLI reason-token defect found
by existing RED tests and corrected inc5cfdd1. No acceptance weakening.

Exact fixed candidate `c5cfdd16732f7560ecdf7406e83b66270f514ba0`, true local-main
base `c4a5ef40a34daded023d5e5121c0462e94cc1487`, six pins verified. Actual local
five-job PASS: backend96, Compose14, contracts87, browser58, security1; zero
failed/skipped. Contracts includes real true-base compatibility and clean exported
candidate browser execution. Aggregate CLI PASS0 `CI_COMPLETE`, success=true.
Security reviews64 exact tree/60 exact history, six nonblocking vulnerability
records remain. [Safe exact evidence](evidence/2026-11-13-balanced-mvp-c5cfdd1.json)
and [commands, original failures, correction and environment](../handoff/2026-11-13-wave-0-balanced-remediation-handoff.md).

Initial load-sensitive Keycloak failure and inherited-PYTHONPATH test failure were
retained; isolated/correct-environment reruns passed without timeout or acceptance
changes. Full CI155 PASS on8e67a18; post-fix focused policy suite3 PASS and full
Contract87 PASS on c5cfdd1. This local rehearsal is not remote scheduler proof or
P0/Wave0 acceptance. Later documentation commit is not the tested candidate.
Remote enforcement and acceptance drills remain NOT RUN; independent hardening OPEN.
