# Wave 0 Plan 4 — CI và wave exit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Thực hiện tuần tự, một coding agent tại một thời điểm; reviewers chỉ đọc. Các checkbox biểu thị kết quả thực tế.

**Goal:** Chạy đầy đủ local/remote CI gates trên đúng revision và cung cấp evidence để đánh giá Wave 0 exit.

**Architecture:** GitHub Actions tách backend, Compose, contracts/SDK, browser và security jobs; aggregator đối chiếu platform status và summaries. Compatibility dùng event base đã xác minh; policy transition chỉ qua independent trusted verifier và cả hai semantic policies. Local pass không thay remote evidence hoặc branch-protection enforcement.

**Tech Stack:** GitHub Actions, Python 3.15.2, .NET 10, Node/npm pinned, PostgreSQL/Testcontainers, Docker Compose, Playwright, Plan 3 scripts và pinned stable security scanners.

**Spec:** `docs/superpowers/specs/2026-11-11-wave-0-plan-4-ci-wave-exit-design.md`.

## Global Constraints

- Không runtime business/auth/tenant implementation, production deployment, package/image publication hoặc thay đổi contracts để gate pass.
- PR dùng `pull_request`, không `pull_request_target`; no secrets/write privilege hoặc privileged execution của untrusted code/artifacts.
- Actions pin full commit SHA; tools/SDK/browser/image pin lock/checksum/digest. GitHub-hosted runner image version được ghi, không giả immutable.
- Không hardcode `40a7c91` cho PR/push, tự fetch/reset/rebaseline, hoặc lấy candidate-controlled waiver làm approval.
- Scanner errors/unknown coverage/expired database fail closed; no raw matched secrets, tokens, HTTP payloads, test/process output hoặc unsafe annotations trên logs/artifacts.
- Không skip required tests do thiếu prerequisites. Local pass không thay remote evidence. Wave 0 chỉ đóng khi W0P4-01→12 pass/review; P0/RT-* vẫn OPEN.
- User yêu cầu plan, chưa ủy quyền push/merge, thay rulesets/secrets hoặc deploy trong lượt này. Chuẩn bị thay đổi local để review trước remote action.

## Review Focus

1. PR merge commit chứa sai parents hoặc base đã advance phải fail (Task 1).
2. Attestation hợp lệ không biến semantic breaking change thành pass (Task 2).
3. Skipped/partial discovery và thiếu report không biến aggregator thành xanh (Task 3).
4. Unverified hoặc secret đã bị xóa trong cùng commit range vẫn bị phát hiện (Task 5).
5. Scanner/image digest khác image thực sự được test, hoặc raw output rò trước artifact scan, phải fail (Tasks 5–6).

## File map và contracts dùng chung

| Files | Trách nhiệm |
|---|---|
| `.github/workflows/wave0.yml`, `contracts.yml` | PR/main triggers, jobs và aggregate; xử lý overlap/required status rõ ràng |
| `scripts/ci/{revision,summary,aggregate,run_safe}.py` | resolve SHAs, result schema, fail-closed aggregation và safe process output |
| `scripts/ci/{scan,sbom,image_identity}.py` | pinned scanner invocation, freshness/coverage, image provenance |
| `scripts/ci/policy_transition.py` | attestation validation và orchestration hai evaluators; verifier trust boundary độc lập |
| `tools/ci/{toolchain,coverage,security-policy,exceptions}.json` | exact pins/checksums, reviewed minima/identities, policies; không secrets |
| `tests/CI/{test_revision,test_summary,test_aggregate,test_policy_transition,test_scan,test_workflow}.py` | regression/negative tests; external commands chỉ mock cho wrapper failure tests |
| `tests/CI/fixtures/` | synthetic positive scanner samples/JSON/TRX, no real credentials |
| `tests/Plan3/{ci-environment,aggregate-reporting}.sh` | bỏ exact total-count assumptions, giữ named missing-prerequisite checks |
| `docs/engineering/08-WAVE-0-PLAN-4-VERIFICATION.md` | W0P4 evidence, local/remote SHA, counts, run URLs và limitations |

Contract `CiSummary`: schemaVersion=1, runId, job, candidateSha, baseSha, status, passed/failed/skipped/total, durationSeconds, reasonToken/correlationId khi failure, tool/image identities. `summary.py` validate strict fields/SHA/counts và never print raw input. Mỗi job tạo summary riêng; aggregate kiểm tra cả platform `needs.<job>.result` lẫn summaries, không tin JSON tự khai success.

Commands local: `python3 -m unittest discover -s tests/CI -v`; `python3 scripts/ci/revision.py --event-file <path>`; `python3 scripts/ci/aggregate.py --needs-file <path> --summaries-dir <path>`; scanner wrappers theo `tools/ci/toolchain.json`. Test fixtures phải có temp dirs/project resources riêng; không đọc/in secret thật.

## Task 1: Revision provenance và event validation

**Files:** `scripts/ci/revision.py`, `tests/CI/test_revision.py`, synthetic Git/event fixtures.

**Interface:** `resolve_event(event: dict, checkout_sha: str, git: GitReader) -> RevisionContext` chứa immutable candidate/base/head/merge SHAs và reason token khi lỗi; `GitReader` đọc Git objects, không fetch/reset.

- [ ] Viết tests PR candidate: checked-out SHA = event merge SHA, hai parents lần lượt base/head, merge-base = event base. Mismatched parent, stale/replaced merge ref, advanced base, missing object phải fail `CI_CANDIDATE_UNAVAILABLE` hoặc `CI_BASE_UNAVAILABLE`.
- [ ] Viết push tests: `before` ancestor của `after`, checkout=after; zero/missing/non-ancestor và event `forced=true` đều fail. Manual/merge_group fixtures không tự dùng HEAD làm base; merge queue chỉ supported sau event-specific proof.
- [ ] Chạy `python3 -m unittest discover -s tests/CI -p test_revision.py -v`: RED do behavioral stub. Implement SHA validation, bounded Git queries và safe JSON output; GREEN. Commit `feat(ci): validate event revision provenance`.

## Task 2: Policy transition đã ủy quyền

**Files:** `scripts/ci/policy_transition.py`, `scripts/contracts/compatibility.py`, `tests/CI/test_policy_transition.py`, trust design documentation.

**Interface:** `verify_transition(attestation, trusted_key, revision, old_hashes, new_hashes) -> VerifiedTransition`; `evaluate_transition(transition, old_evaluator, new_evaluator) -> CompatibilityResult`. Verifier/key đến từ base-controlled hoặc independently pinned workflow, không candidate; nếu chưa có trusted capability, transition BLOCKED.

- [ ] Viết tests attestation hợp lệ bind old/new policy hashes, PR base/head/candidate tree, files, ADR/reviewer/expiry; sai signature/base/tree/hash, expiry và PR waiver file fail. Test first main push với before + reviewed merge method/tree, và rollback transition riêng.
- [ ] Viết tests old policy hoặc new policy phát hiện breaking/tool error đều fail dù chữ ký đúng; cả hai evaluators nhận cùng base/candidate contracts thật. Candidate scripts không chạy trong privileged verifier.
- [ ] Chạy focused tests RED; refactor policy-identity preflight tách semantic evaluation, chỉ exact verified transition vượt preflight; chạy cả old/new evaluators bằng immutable snapshots. Không thay event base hoặc contract contents.
- [ ] Focused tests GREEN; Plan 3 compatibility/consumer regressions pass. Ghi trust-anchor bootstrap, key ownership, independent workflow/policy provisioning và trạng thái OPEN nếu chưa có quyền. Commit `feat(ci): verify reviewed policy transitions`.

## Task 3: Test reports và fail-closed aggregator

**Files:** `scripts/ci/{summary,aggregate}.py`, `tools/ci/coverage.json`, `tests/CI/{test_summary,test_aggregate}.py`, hai Plan 3 harnesses.

**Interface:** `parse_report(kind: str, path: Path, expected: RunIdentity) -> CiSummary`; `aggregate(needs: Mapping[str,str], summaries: Sequence[CiSummary], expected: RunIdentity) -> AggregateResult`. Required inventory: backend, compose, contracts-sdk, web-browser, security.

- [ ] Viết tests thiếu/duplicate/malformed/wrong-run/wrong-SHA report, `needs` failure/skipped/cancelled, zero/partial discovery, unexplained skip và test `tests/Local` cố ý fail: aggregate nonzero, safe reason/correlation. Whole-workflow cancellation/missing aggregator phải báo non-success, không giả summary.
- [ ] Thiết lập reviewed minima/required test identities: historical starting reference contract87, Local20, non-Compose70, Compose14, Vitest54, browser4; xác minh actual discovery trên pinned runtimes. Thêm test mới vẫn pass; xóa/skip required identity fail. Candidate không tự hạ baseline.
- [ ] Chạy focused tests RED; implement strict parsers/aggregator. Cập nhật `ci-environment.sh` và `aggregate-reporting.sh` để kiểm tra named expected skips/minimum thay vì cố định 87/55/32; test thêm/xóa case, giữ simulated missing-prerequisite coverage.
- [ ] Chạy focused Python + hai harnesses GREEN. Workflow sau dùng `needs` inventory + `always()`; cancelled toàn workflow được ghi cancelled/missing. Commit `feat(ci): aggregate complete test evidence fail closed`.

## Task 4: Locked backend, contract và browser jobs

**Files:** `.github/workflows/wave0.yml`, `.github/workflows/contracts.yml`, `scripts/ci/` runners, `tests/CI/test_workflow.py`.

**Interfaces:** Jobs `backend`, `compose`, `contracts-sdk`, `web-browser`; `wave0-required` có `needs` tường minh và `always()`. Python contract run có structured counts; Plan 3 `--base-ref` nhận SHA từ Task 1. Không biến `not run` browser thành pass.

- [ ] Viết workflow contract tests cho action SHA pins/permissions, PR+main triggers, mandatory job inventory, timeout và generated reports; positive clean fixture/negative skipped job, wrong SHA và missing prerequisite.
- [ ] Chạy tests RED; implement provisioning pinned SDK/Node/npm/Python/Docker/browser và isolated jobs. Backend chạy non-Compose + `tests/Local`; Compose chạy Category=Compose; contracts chạy prepared >=87/zero unexpected skips, bốn SDK packages/consumers và năm `tests/Plan3` harnesses; browser chạy real API stack với `--with-browser`.
- [ ] Test old `contracts.yml` không tạo ambiguous required status; chọn sửa hoặc thay workflow, document migration của check names. Không đổi `.NET`/npm locks ngầm. Reports ghi counts/job SHA, setup/timeout errors safe.
- [ ] Chạy workflow lint + local job rehearsal thật trên exact candidate, focused tests GREEN. Kết quả remote chưa có; commit `ci: run backend contract and browser gates`.

## Task 5: Source, dependency và secret security gates

**Files:** `scripts/ci/{run_safe,scan}.py`, `tools/ci/{toolchain,security-policy,exceptions}.json`, `tests/CI/test_scan.py`, synthetic fixtures.

**Interface:** `run_scanner(scanner: ScannerConfig, input: ScanInput, run: RunIdentity) -> ScanSummary`; subprocess stdout/stderr private temp, output whitelist-only. Exceptions bind finding ID/owner/reason/expiry/trusted approval; candidate PR không tự approve.

- [ ] Viết positive fixtures cho C# SAST, TS SAST, dependency, license và secret scanner thật; secret unverified cũng fail. Scan candidate tree + complete trusted commit range, gồm secret synthetic được thêm rồi xóa. Verification adapter test local/canned nếu provider verification không hỗ trợ; không dùng real credential.
- [ ] Viết missing scanner/feed/network, unknown severity/license, stale DB (>24h hoặc không có upstream timestamp), expired/forged exception và incomplete ecosystem inventory tests: fail `CI_SCAN_INCOMPLETE` hoặc review-required, không fake timestamp.
- [ ] Viết sentinel-bearing scanner/test-process failures (multiline/encoded variants): logs, annotations, artifacts chỉ normalized safe fields; parser/sanitizer failure fail closed và không echo raw input. Không claim chống malicious candidate sửa chính workflow.
- [ ] Chạy focused tests RED; chọn stable supported scanners theo primary docs/availability, pin version/checksum và data-source/freshness. Implement source/dependency/license/secret coverage theo spec, no credentials trả phí.
- [ ] Chạy scanner thật trên mỗi positive class và negative/wrapper suite GREEN; ghi versions/coverage/freshness và unresolved findings, không suppress để xanh. Commit `ci: add fail-closed source and dependency security gates`.

## Task 6: Image scans, SBOM và provenance

**Files:** `scripts/ci/{image_identity,sbom,scan}.py`, `tests/CI/test_image_identity.py`, workflow security/Compose/browser integration.

**Interface:** `ImageIdentity(job: str, runId: str, imageId: str, platform: str, manifestDigest: str | None)`; mỗi job scan immutable image IDs thực sự chạy, aggregate match theo job/run/identity. Không nhầm local config ID với registry manifest digest.

- [ ] Viết tests missing/mismatched image identity, wrong job/run, scanner unavailable và synthetic vulnerable image/fixture: aggregate fail. Real scanner positive + SBOM nonempty cho API/migrator/PostgreSQL/Keycloak, tool DB freshness được kiểm chứng.
- [ ] Chạy RED; implement immutable image inventory trước test và scanner trên đúng images. Nếu chia OCI artifact cùng run, verify digest/sentinel trước upload/consume; không chạy artifact trong privileged context. Không publish registry.
- [ ] Kiểm chứng SBOM/normalized summaries không sentinel, no raw inspect/history. Focused tests + actual image scan GREEN; commit `ci: bind image security evidence to tested images`.

## Task 7: Aggregator, remote security và failure rehearsal

**Files:** workflow aggregate/security jobs, `tests/CI/test_workflow.py`, integration harnesses và evidence docs.

- [ ] Viết negative tests actual job inventory: failed/skipped/cancelled/missing jobs, wrong-run summaries, no tests và `tests/Local` failure đều non-success. Xác minh `always()` chỉ cho job-level completion; whole workflow cancelled/missing không được báo aggregate success.
- [ ] Viết tests fork/untrusted PR: read-only permissions, no repository secrets, no `pull_request_target`, không interpolate PR title/body vào shell, no cross-trust cache/privileged artifacts. Merge queue nếu enabled phải có `merge_group` candidate/base validation; unknown ghi OPEN.
- [ ] Chạy tests RED; implement `wave0-required`, all summaries/artifact allowlist và safe wrappers. Test action timeouts, setup failure, scanner failure, forced push, missing base và policy transition failure đúng reason.
- [ ] Chạy local rehearsal exact-SHA, real Docker/PostgreSQL/Playwright và scanners; no unexpected skips. Review whole branch, fix findings/test lại. Commit `ci: enforce aggregate status and isolated diagnostics`.

## Task 8: Remote evidence và Wave 0 review

**Files:** `docs/engineering/08-WAVE-0-PLAN-4-VERIFICATION.md`, roadmap/acceptance/CI docs, `docs/handoff/`, spec/plan status.

- [ ] Trước remote action, chuẩn bị reviewed diff, local rehearsal/negative results, required check names, scanner policies, expected SHAs và rollback. User request triển khai plan không tự cấp quyền sửa repository rules/secrets hoặc deploy; chỉ thực hiện remote actions đã được ủy quyền trong session.
- [ ] Khi authorized, chạy remote PR và main-push workflows, record URLs/events/base/head/candidate/merge method/final SHA, counts/durations/image IDs và status từng job. Chứng minh main commit chứa reviewed candidate; không ghép unrelated green runs. Test remote failed/skipped/cancelled job và whole-workflow cancellation trong isolated test context.
- [ ] Map W0P4-01→12: 01 Tasks1/4; 02 Task4; 03 Tasks3/4; 04 Task4; 05–06 Tasks1/2; 07 Task5/6; 08 Task5/6; 09 Task3/7; 10 Task7; 11 Task7/8; 12 Task8. Ghi exact local/remote SHA, counts, versions/digests, artifacts, DB freshness, quota và actionable blockers.
- [ ] Kiểm tra branch protection/merge queue read-only nếu quyền có; ghi VERIFIED hoặc UNKNOWN/OPEN. Không claim enforcement nếu chưa xác minh; thay đổi settings chỉ với ủy quyền riêng. Thiếu remote evidence/trusted policy verifier hoặc required gate fail → Wave 0 OPEN.
- [ ] Cập nhật matrix/docs/handoff, review evidence và rollback. Wave 0 có thể đóng với branch protection UNKNOWN nếu mọi W0P4 checks pass và trạng thái governance báo rõ; P0/RT-* luôn OPEN. Commit `docs: record Wave 0 CI verification and handoff`; không tự merge/push/deploy.

## Self-review và handoff

W0P4-01→12 được map trong Task 8. Review Focus có test ở Tasks1–6; current contract suite skipped55 không được xem là full pass. SDK/contract/runtime semantics và remote trust boundaries được giữ theo spec. Đây là plan documentation; chưa chạy Plan 4 tests hoặc remote workflow. Phương pháp thực thi và branch cụ thể theo yêu cầu người dùng khi bắt đầu implementation.
