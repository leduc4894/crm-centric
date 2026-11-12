# Wave 0 Plan 4 — quyết định thu gọn CI MVP

## Objective and authority

Người dùng đồng ý giữ CI cần cho MVP và chuyển verifier độc lập cùng supply-chain hardening nâng cao sang trước production. Phiên này cập nhật tài liệu quyết định, không ủy quyền tự động ngoại lệ security, thay workflow, push/merge, repository settings hoặc deployment. AGENTS.md golden rules, P0 và RT-* không thay đổi.

## Completed

[Quyết định CI MVP](../engineering/09-WAVE-0-CI-MVP-SCOPE.md) nêu gates, điều kiện evidence, phân định hardening, mapping W0P4 và trạng thái OPEN. Đã thêm chỉ dẫn ưu tiên scope mới vào spec/plan Plan 4, acceptance matrix, CI docs, verification ledger và roadmap; evidence cũ giữ nguyên. Mức severity, ngoại lệ cụ thể và approval finding chưa được người dùng quyết định, không tự suy diễn đã phê duyệt.

## Workspace and verification

Branch `feat/wave-0-plan-4-ci`; resolve current HEAD via git. Các thay đổi phiên này gồm tài liệu nêu trên và handoff này; chưa sửa runtime/workflow/locks/contracts. Untracked `docs/handoff/2026-11-12-wave-0-plan-3-resumption-handoff.md` có sẵn từ trước được giữ nguyên. `git diff --check`: PASS; kiểm tra relative Markdown links của tám file thay đổi: PASS; không chạy runtime/CI/scanner vì documentation-only, không có remote action.

## Status and next steps

Wave 0, P0 và RT-* OPEN. Workflow hiện tại vẫn chặn trust/image/security; không coi việc đổi scope là pass hoặc xóa gate. Viết plan thực thi CI MVP, phân định rõ scanner blocking và quy trình review findings, sau đó triển khai và chạy full local/remote evidence khi được ủy quyền. Hardening vẫn OPEN trước production; verifier không tồn tại không cho phép policy transition bypass. Review governance trước bất kỳ thay đổi repository settings.

## MVP execution continuation

Người dùng yêu cầu viết plan ngắn và triển khai CI MVP. [Plan điều chỉnh](../superpowers/plans/2026-11-12-wave-0-ci-mvp-adjustment.md) phân tách image/trust hardening khỏi thực thi test MVP và giữ secret/dependency scan fail-closed, không tự phê duyệt findings. Tiếp tục thực thi local theo SDD tuần tự; remote operations/settings vẫn cần ủy quyền riêng. Wave 0/P0/RT-* OPEN. Untracked handoff Plan 3 có sẵn được giữ nguyên.

## Task 1 implementation — 2026-11-12

Objective: execute required MVP test jobs without deferred image scanners; preserve five jobs, immutable revision evidence, contract policy preflight and existing secret/dependency blocking. User authorized the local MVP adjustment plan; no finding exception, policy bypass or remote action is authorized.

Implemented explicit `--mode mvp` in job execution, safe completion and aggregate parsing. Default `hardening` behavior retains image verification. MVP fixture subprocesses receive neither image evidence hook (even when inherited); safe reports explicitly record `image_hardening.status=not-evaluated` and reject image clearance claims. Non-security MVP setup omits scanner provisioning. Testcontainers resource ownership and CLI run labels remain independent of image scanning. The MVP workflow no longer has an unconditional independent-trust exit; contract policy identity preflight is unchanged and policy transitions remain blocked without trusted approval. Security job code, scanner policies, finding thresholds and evidence requirements are unchanged for Task 2.

Changed files: `.github/workflows/wave0.yml`; `scripts/ci/{aggregate,completion,jobs,publication,summary}.py`; `scripts/ci/setup.sh`; `tests/CI/test_{aggregate,job_reports,summary,workflow}.py`; `tests/Integration/Fixtures/ContainerImages.cs`; this handoff. Public API/schema, lockfiles and application domain behavior unchanged. Branch `feat/wave-0-plan-4-ci`; implementation follows base `601dde8`; see commit history for Task 1 commit. Existing untracked Plan 3 handoff preserved.

Verification so far: RED confirmed missing MVP mode/parser/context, scanner setup still executing, and unconditional trust failure. GREEN focused regression coverage includes required test failure/skip/missing/wrong SHA, complete five-job reports without images, completion sanitation and unchanged security evidence gates. Local discovery PASS (20 tests); real PostgreSQL Testcontainers MigrationTests PASS (5 tests) under MVP execution context, no scanner hook, build included. Workflow contract lint, actionlint, shell syntax and `git diff --check` PASS. Initial CI discovery on system Python or incomplete PATH failed existing real policy semantic evaluator with `CONTRACT_TOOL_ERROR`; pinned Node/Python/toolchain rerun is required and its final outcome is recorded below. This was working-tree selective verification, not full exact-candidate MVP acceptance.

Remaining: Task 2 security scoped evidence/triage; Task 3 full exact candidate local five-job rehearsal and authorized remote evidence. Full backend/Compose/browser jobs, remote run, repository enforcement and P0 drills have not been completed by Task 1. Image clearance and independent trust hardening remain OPEN; Wave 0/P0/RT-* OPEN.

Additional Task 1 verification: real built API/container security fixture `ContainerRuntimeTests.Api_uses_only_application_secret_as_nonroot` PASS (1/1) under MVP context with image scanner hooks absent; actual container build/start, database migration, API readiness/nonroot/secret mounting and dependency outage assertions executed. Toolchain versions verified equal all six runner pins. This adds selective evidence, not a full backend/Compose job pass.

Final Task 1 CI regression: pinned Node/Python/toolchain `python -m unittest discover -s tests/CI` PASS, 120 tests, 0 failures/errors/skips, 305.037s. Includes real dual-policy semantics and unchanged-vs-changed preflight regressions. The earlier incomplete-PATH failure was resolved by using the verified toolchain, without changing policy code. Final syntax/lint/diff checks PASS. Task 1 implementation is complete; five-job exact-candidate/remote acceptance and Tasks 2–3 remain outstanding.

## Task 2 implementation — 2026-11-12

Objective: separate mandatory secret/dependency scans from deferred hardening, with run-bound safe findings and no finding approval. Local implementation only; user has not approved exceptions, new severity thresholds, remote actions or settings changes.

Implemented `security --mode mvp` with required Gitleaks/Trivy reports and exact committed tree/history/package coverage. Default hardening behavior is retained. Only `CI_TOOL_DEPENDENCY_COVERAGE` is deferred; unsupported ecosystem, unlocked project, scanner/feed/freshness and history errors remain blocking. Existing vulnerability thresholds and all-secret blocking are unchanged. License findings are retained under explicit OPEN coverage alongside SAST, images and tool inventory. MVP workflow no longer provisions deferred Semgrep/runtime or requires the unproduced full source-SBOM artifact. Safe completion reconstructs and revalidates the required/deferred evidence, rejecting forged clearance and stale identity.

Changed files: security workflow/setup, `scripts/ci/{scan,security,summary,publication}.py`, focused CI/real scanner tests, [security evidence and triage](../engineering/10-WAVE-0-MVP-SECURITY-TRIAGE.md), this handoff. Public contracts, baseline, dependency pins and application behavior unchanged. Branch `feat/wave-0-plan-4-ci`; Task 2 follows `a4359ca`. Existing unrelated untracked Plan 3 handoff preserved.

Verification: RED confirmed missing MVP combiner/evidence, deferred Semgrep prerequisite and missing workflow separation. Focused GREEN: scan policy 23 tests; security job 7 tests (including actual missing-scanner CLI); aggregate 8; workflow 9. Real pinned Gitleaks 8.30.1/Trivy 0.76.2/Semgrep 1.185.0 scanner suite PASS 9 tests, 0 skips, 19.308s, including introduced/deleted and merge-only secrets, missing history/blob, unsupported/unlocked dependency coverage, vulnerable dependency still blocked, and license-only evidence remaining OPEN. Workflow contract lint, actionlint, shell syntax and `git diff --check` PASS. Full pinned CI regression and exact committed-candidate scan results will be appended after completion.

No finding approved. SAST/license/image/tool hardening, independent verification, remote/enforcement and Wave 0/P0/RT-* remain OPEN. Next: record committed-candidate findings and blocking status, then Task 3 exact-candidate five-job rehearsal. No local selective fixture success closes acceptance.

Task 2 committed implementation: `4baea16` (`ci: report MVP security scans and deferred coverage`). Exact committed candidate scan at full SHA `4baea1646a424ab8be03bfecb5c25bf2d413de57`, base `601dde8847cad82d038ea837c01d3da0d7449450`, local run `local-mvp-task2` attempt 1: **FAIL**, exit 1, `CI_SCAN_INCOMPLETE`. Gitleaks BLOCKED, 64 unique secret digests over tree+history; Trivy PASS under unchanged vulnerability policy, 10 locks/6 nonblocking vulnerability records; required `CI_UNLOCKED_DEPENDENCIES` remains. Deferred tool inventory gap and license 271 review-required/508 allowlist-pass records remain OPEN with SAST/images. No classification as false positive or finding approval was made. Safe completion round trip PASS as failure-preservation (`CI_JOB_FAILED`). Durable safe classified digest evidence linked from security triage doc; raw scanner output was neither printed nor retained. Full regression result pending below. Evidence-only follow-up changes require fresh final-candidate scan in Task 3.

Final Task 2 regression: pinned `python -m unittest discover -s tests/CI` PASS, 125 tests, 0 failures/errors/skips, 296.148s, exit 0. The subsequently added actual missing-scanner MVP CLI test passed within the separate 7-test security job run. Final workflow/actionlint/shell/diff and durable evidence-shape/link checks PASS. Regression success proves implementation behavior; it does not clear the actual committed candidate's failed security scan. Task 3 full five-job/final-SHA/remote evidence remains outstanding; remote actions remain unauthorized. See history for evidence-only follow-up commit after `4baea16`; only unrelated untracked Plan 3 handoff remains outside Task 2 commits.


## Task 3 rehearsal continuation — 2026-11-12

Objective: full local five-job evidence under the authorized MVP scope, no
remote action or finding approval. Read this handoff and Task1/2 reports; verified
branch `feat/wave-0-plan-4-ci` at `5a17535b5da6802dde0548e7fe0aa0bef2a3ca2c`.
Base `601dde8847cad82d038ea837c01d3da0d7449450`; no merge SHA.

Completed original attempts: backend PASS96/96/436.611s; Compose
PASS14/14/1400.580s; contracts-sdk FAIL `CI_COMMAND_FAILED`, 87 passing cases
collected/1140.515s; browser FAIL same token, zero collected/30.681s after native
library correction; security FAIL `CI_SCAN_INCOMPLETE`/6.379s. Gitleaks64 digests
BLOCKED, Trivy10 locks/6 existing-policy nonblocking records, unlocked dependency
coverage still BLOCKED. No exception, severity threshold or approval changed.
Exact commands/tool versions and compact safe evidence are in
[ledger08](../engineering/08-WAVE-0-PLAN-4-VERIFICATION.md).

Safe diagnostic wrapper in ignored `.local/ci-task3/diagnose.py` captures raw
subprocess streams only in memory and publishes fixed classifiers/reason enums.
Chromium real launch PASS; full browser diagnostic then FAIL at generated-check
with `SDK_GENERATED_MISMATCH`. Exactly five ignored restore artifacts in
`sdks/dotnet/Crm.Client/obj` were moved intact to ignored
`.local/ci-task3/sdk-obj-backup`. No tracked code was changed. Full clean-state contracts rerun PASS87/87, 0 failures/skips, 2070.489s; generated
check, consumers, compatibility and all five Plan3 harnesses exit0. Exact-SHA
clean-export with browser PASS327.87s; negative cases PASS224.89s. Full clean-state
web rerun PASS58/58 (54 Vitest + 4 real Playwright), 0 failures/skips, 303.270s.
Original failed reports are preserved. Reports `contracts-sdk-diagnostic.json`
and `web-browser-diagnostic.json` stay in ignored `.local/ci-task3/`; compact
tracked evidence includes their identities/counts plus required security digest
findings, approval NONE/owner UNASSIGNED. No runtime fix was needed.
Initial full pinned CI discovery FAIL125/126,0skip,287.610s, only process
timeout cleanup test. Original exception UNKNOWN because safe reporter discarded
traceback; isolated process module PASS4/4/1.507s; full pre-fix diagnostic replay
PASS126/126,0failure/error/skip,268.910s. Controlled0.75s startup reproduces
FileNotFoundError in unchanged fixture: timeout0.5 can expire before child PID
publication. Added slow-start regression RED1 failure/5tests/3.038s, then test
fixture readiness synchronization GREEN5/5/3.867s. Production execute/timeout
and actual descendant cleanup assertions unchanged. This proves a fixture race,
not the exact original unknown exception. Final full post-fix regression PASS127/127,0failures/errors/skips,320.490s,
exit0. This result includes the new slow-start regression.
Actual-report completion preserves4testPASS+securityFAIL; local aggregate exits1
with CI_JOB_NON_SUCCESS. No forged all-green result.
Next: commit the verified test-fixture fix with this handoff, then finish the
separate documentation/evidence commit. No selective success replaces the preserved failures.

Current changed files: ledger08, scope09, compact safe rehearsal JSON,
`tests/CI/test_job_processes.py`, this handoff; ignored task3 report also updated. Unrelated untracked
`2026-11-12-wave-0-plan-3-resumption-handoff.md` preserved. No Task3 commit existed at the verification boundary; the test-fixture fix and
this handoff are committed together next, followed by documentation/evidence.
Static verification PASS: workflow contract, actionlint, setup shell syntax,
committed diff whitespace. Final full CI regression PASS127/127 as stated; full service reruns bind
5a17535, while the later test-fixture and evidence commits have no new complete
five-job rehearsal. No acceptance claim is transferred between these SHAs.
Remote CI NOT RUN, enforcement UNKNOWN; Wave0/P0/RT-* and deferred hardening OPEN.
Evidence-only documentation changes require fresh exact-final-SHA evidence
before any later acceptance claim.


Task 3 final boundary: readiness fix/handoff commit
`1ff6ca3145c4d1b23ec29805ad5e507870b38f36`; final full CI127/127 PASS320.490s
ran on that test content before commit. Full service/security evidence stays at
5a17535. Reviewed full MVP implementation/test diff and readiness correction.
Workflow contract/actionlint/setup shell/Python AST/Markdown links/evidence
identity-count-digest checks and `git diff --check` PASS. Remaining uncommitted
Task3 work at this boundary is documentation/evidence/plan status, committed
separately next; only unrelated untracked Plan3 handoff must remain afterward.
No public contract, dependency pin, migration or production runtime change.

Next steps after this local task: privately review64 secret digests with an
accountable reviewer; remediate unlocked dependency coverage; retain license,
SAST, image, tool inventory and verifier hardening OPEN; select a candidate and
run fresh full gates before separately authorized remote execution. Do not
transfer5a17535 rehearsal to later commits or interpret unit success as security
clearance. Remote CI NOT RUN, enforcement UNKNOWN, Wave0/P0/RT-* OPEN.


Final local state: Task3 documentation/evidence/plan status is committed in the
commit containing this paragraph (`docs: record MVP CI rehearsal and outstanding
decisions`, following1ff6ca3). No tracked Task3 changes remain. Only the original
untracked Plan3 resumption handoff is preserved outside these commits. All
started jobs and regression runs finished; no pending rerun is being handed off.
The next steps above concern unresolved security/acceptance work, not unfinished
Task3 execution. Full five-job evidence for this documentation commit NOT RUN.

## MVP finding identity and private triage continuation — 2026-11-12

Objective: privately review 64 blocked IDs and investigate unlocked coverage.
Authority: parent-directed local remediation on the current branch, no worktree,
subagents or remote action; no finding approvals or policy relaxation. Read
AGENTS, this handoff, security triage10, prior Task2/3 reports and verified
HEAD `71a0bab` on `feat/wave-0-plan-4-ci`. Preserved the unrelated untracked
Plan3 resumption handoff untouched.

Completed: privately replayed exact4baea16 tree/history. Found64 tree/0history;
60 JAR digest fields independently match retained local built JAR bytes and four
SDK provenance digest fields match exact Git OpenAPI blobs. Safe per-ID proposed
review observations are in `docs/engineering/evidence/2026-11-12-mvp-finding-review.json`.
All64 new stable IDs remain BLOCKED, approval NONE/owner UNASSIGNED. Original64
opaque IDs remain unmapped/unclassified because their discarded temporary path
was part of the hash. No retrospective positional mapping or approval claimed.

Fixed a confirmed ID defect with TDD: initial actual repeated Gitleaks scan RED
(1 test/1 failure,4.375s); run exact exported tree from cwd with relative `dir .`
instead of an absolute temporary pathname, retaining the complete record hash.
GREEN focused test1/1,5.854s. Full real scanner suite initially exposed only
order-sensitive tuple comparison (9tests/1failure,20.014s); compare multisets,
then final real scanner suite PASS9/9,0skips,21.469s. Unit path/rule/location
separation and scan policy suite PASS24/24,0skips,0.348s. No rule, scanner pin,
severity, baseline, threshold, public contract or runtime default changed.

Unlocked manifests identified precisely: Surface.csproj and CorvusProbe.csproj
listed in triage10. Private throwaway `dotnet restore --use-lock-file` and
`--locked-mode` for Surface PASS, empty package inventory; CorvusProbe restore
FAIL (lock output alone is not successful restore). No lock or project change
adopted. Review dependency errors before implementing correct transitive locks;
never exempt tools/fixtures or fabricate empty package coverage.

Changed tracked scope: scan.py, two scanner test files, triage10, safe review
JSON and this handoff. Reproduction algorithm and limitations are in triage10.
Initial full CI runner launch failed for missing libpython loader path before
running tests; retried with explicit pinned Python LD_LIBRARY_PATH. Full pinned
CI suite is still RUNNING at this paragraph; final result appended below.
No raw reports/matches/tokens/payloads were printed or committed. Private ignored
probes under `.local/mvp-triage` retain only authored safe metadata/locations.
No remote action. Wave0/P0/RT-*, acceptance and deferred hardening stay OPEN.

Additional coverage feasibility: normal CorvusProbe restore failed NU1008 due
inherited central package management conflicting with explicit versions in the
generated SDK project. A separate throwaway export restored both projects with
`dotnet restore --use-lock-file -p:ManagePackageVersionsCentrally=false`, followed
by `dotnet restore --locked-mode -p:ManagePackageVersionsCentrally=false`: both
PASS, Surface0 entries/CorvusProbe6 entries. This is an actual diagnostic, not
adoption or approval. A concrete follow-up can isolate these SDK/tool project
settings consistently with generator builds, add their authentic generated
locks, and verify both normal and locked build/consumer flows before committing.
No empty-lock shortcut for the transitive fixture and no coverage exemption.
Safe review JSON schema/count/unique-ID/blocked-approval invariants PASS.

Final implementation verification: pinned full CI regression PASS128/128,
0failures/0skips,298.908s,exit0, using
`LD_LIBRARY_PATH=.local/ci-python315/lib .local/ci-venv315/bin/python .local/ci-task3/unit.py`.
Python compilation, Markdown relative links and `git diff --check` PASS.
This verifies implementation behavior, not security clearance or full five-job
acceptance. Commit this scoped fix/evidence, then scan its exact SHA and record
the actual mandatory result separately. Full service/remote/P0 drills NOT RUN.
Rollback: revert this scoped identity/evidence change; no database migration.

Committed implementation/evidence SHA:
`d10c225d0fad214ddf54dbf4c46fe3b76a79dc6d`.
Actual exact-SHA command:
`GITHUB_RUN_ID=local-mvp-triage GITHUB_RUN_ATTEMPT=1 CI_SCANNER_ROOT=.local/task5 python3 -m scripts.ci.security --mode mvp --revision .local/mvp-triage/revision.json --output .local/mvp-triage/security.json`
FAIL exit1,8.642s,`CI_SCAN_INCOMPLETE`. Gitleaks64 blocked/2surfaces;
TrivyPASS10locks/6existing-policy nonblocking records. Required unlocked gap
remains. License271review-required/508pass; all deferred coverage OPEN.
All64 current IDs exactly equal the independently verified stable review IDs;
zero unmatched/unclassified current findings, zero approvals. Original64 remain
unmapped/unclassified. Safe completion round trip PASS as failure preservation,
`CI_JOB_FAILED`; this is not a passing security gate.

Final follow-up changed files: triage10, review JSON adds explicit committed-scan
binding, new safe security-d10c225 JSON and this handoff. Commit this evidence
separately; later documentation commit itself has no five-job/final-SHA scan.
No remote actions. No runtime/backend/Compose/browser reruns, remote E2E,
tenant/duplicate/outage/restore drills; no acceptance closure. Next: accountable
per-ID review and trusted decisions, project-isolation/lock remediation with
consumer tests, then fresh exact-final-SHA full gates. Existing Plan3 untracked
handoff remains untouched. No running verification process is handed off.
