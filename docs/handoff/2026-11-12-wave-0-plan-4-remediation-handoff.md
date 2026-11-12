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
