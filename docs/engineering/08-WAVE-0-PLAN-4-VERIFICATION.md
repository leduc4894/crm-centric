# Wave 0 Plan 4 verification — 2026-11-11

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
