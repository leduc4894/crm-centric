# Wave 0 Plan 3 — Frontend và contract toolchain Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Thực hiện từng task tuần tự, một coding agent; reviewers chỉ đọc. Checkboxes là kết quả thực tế.

**Goal:** Frontend React chạy được với operational API, bốn generated SDK compile/đóng gói độc lập, và local compatibility gate fail khi contract hoặc SDK phá vỡ consumer.

**Architecture:** Hai OpenAPI canonical tạo bốn clients TS/C#; frontend chỉ dùng operations client qua adapter an toàn. Gate so sánh contract và SDK surface với Git base đáng tin cậy, tách wire/security/schema review. Business API vẫn là fixtures.

**Tech Stack:** React, TypeScript strict, Vite, npm, Vitest, Playwright, .NET 10, generator và diff tool stable/pinned, existing Python contract tooling.

**Spec:** `docs/superpowers/specs/2026-11-10-wave-0-plan-3-frontend-contract-toolchain-design.md`.

## Global Constraints

- Canonical inputs: `docs/contracts/crm-api.openapi.yaml`, `docs/contracts/platform-operations.openapi.yaml`; không sửa contract để generator pass.
- Không login/tenant/business runtime, frontend production hosting, package publication, remote CI hoặc thêm DB migration.
- SDK không reference CRM server projects/database/domain; không token storage hoặc implicit bearer/cookie/tenant trên operations client.
- Pin supported stable Node/npm, packages, generator/diff artifacts và browser; commit locks. Không Temporal/experimental templates hoặc `any` cho toàn bộ DTOs.
- Developer stack Plan 2 giữ ownership/secrets/issuer và SDK-free backend; frontend dev riêng trên `localhost:5173`, strict port, chỉ proxy `/health/live` và `/health/ready` tới loopback API.
- Không credentials/tokens/PII trong bundle, logs, fixtures hoặc output; synthetic fixtures không thay runtime acceptance.
- TDD RED→GREEN; không tính lỗi thiếu prerequisites/compile là RED. Tests database thật dùng Testcontainers; browser smoke dùng API thật.
- Git base thiếu/invalid, tool unsupported hoặc input bị xóa không được pass. Không tự fetch/reset/rebaseline, ignore-all hoặc upload contract.
- Wave 0/P0 và Plan 4 CI vẫn OPEN. Cập nhật handoff; không merge/push/deploy.

## Review Focus

1. Generator round-trip valid conditional AutomationRule nhưng không claim enforce server validation (Tasks 1–2).
2. Auth scopes, webhook signature header và tenant context không rơi khỏi security comparison (Task 3).
3. Generator-only change không được so sánh cả hai phía bằng config mới (Task 4).
4. Browser deadline bao gồm body read và request cũ không overwrite request mới (Task 6).
5. Foreign listener, stale baseline/config và secrets trong diagnostics không tạo false pass (Tasks 3–4, 6–7).

## File map và commands

| File / nhóm | Trách nhiệm |
|---|---|
| `package.json`, `package-lock.json`, `.npmrc`, `.node-version` | npm workspaces, exact pins, locked install |
| `tools/contracts/` | input discovery, pinned generator/diff metadata, configs và compatibility policy |
| `scripts/contracts/{generate,check,compatibility}.py` | generate, read-only deterministic check, Git-base comparison |
| `sdks/typescript/{crm,operations}/`, `sdks/dotnet/{Crm.Client,Platform.Operations.Client}/` | generated code/locks và package metadata |
| `tests/ClientConsumers/{typescript,dotnet}/` | packaged independent consumers và captured HTTP |
| `tests/Contract/fixtures/compatibility/`, `tests/Contract/test_compatibility.py` | valid base và mutated fixtures; actual gate |
| `apps/crm-web/` | React/Vite app, health adapter, Vitest/Playwright tests |
| `scripts/check-plan3.sh` | aggregate checks không mutate source hoặc fetch Git |
| `docs/engineering/07-WAVE-0-PLAN-3-VERIFICATION.md` | W0P3-01→12 evidence |

Public entrypoints: `python3 scripts/contracts/generate.py`, `python3 scripts/contracts/check.py`, `python3 scripts/contracts/compatibility.py --base-ref <ref>`, `bash scripts/check-plan3.sh --base-ref <ref>`. Check commands fail closed, trả safe JSON reason/correlation, không in raw HTTP payload hoặc secrets. Exact tool versions/digests được chọn sau official metadata + compatibility proof, ghi vào evidence.

## Task 1: Generator compatibility và bốn SDK outputs

**Files:** workspace pins/locks, `tools/contracts/`, `scripts/contracts/{generate,check}.py`, bốn `sdks/`, focused generator tests, verification document.

**Interfaces:** `generate.py --output-root <dir>` tạo cả bốn outputs từ canonical inputs; default output là declared SDK roots. `check.py` generate trong temp, so full tree với committed outputs và provenance; không mutate workspace. Input hashes, tool digest và config hash được lưu trong output metadata.

- [ ] Kiểm tra Node/npm/.NET/Docker và official stable generator candidates. Bootstrap test harness/locks tối thiểu; ghi prerequisites riêng, không coi thiếu package là RED.
- [ ] Viết `test_generated_check_rejects_missing_edited_extra_and_nonlocal_refs`: bắt đầu bằng generated fixture hợp lệ, sửa/xóa/thêm file hoặc remote `$ref`; assert nonzero safe reason, workspace hashes không đổi. Chạy test: RED do gate stub thiếu behavior.
- [ ] Generate toàn bộ business + operations sang TS/C# với candidate stable templates; build bốn outputs. Chứng minh AutomationRule valid built-in/remote variants giữ wire fields/const/nullable; invalid combinations do canonical schema validator reject. Nếu template mất valid wire representation, trả `SDK_GENERATION_UNSUPPORTED`; không downgrade contract hoặc dùng `any` toàn cục.
- [ ] Implement generator/config/provenance và read-only check; chạy hai lần tại thư mục khác, so normalized output và input/tool/config hashes; focused tests GREEN. Existing contract suite pass. Commit `build(sdk): generate deterministic clients from canonical contracts`.

## Task 2: Package consumers và wire semantics

**Files:** `tests/ClientConsumers/typescript/`, `tests/ClientConsumers/dotnet/`, SDK package metadata, thin transport facades nếu cần, `tests/Contract/test_sdk_consumers.py`.

**Interfaces:** local `npm pack` tạo `.tgz`; `dotnet pack` tạo `.nupkg`. Consumer temporary projects chỉ cài artifact từ local path/feed; HTTP stub/captured fetch và `HttpMessageHandler` mô phỏng public wire, không reference server projects. Facades chỉ bổ sung status/header/cancellation; không viết lại DTOs.

- [ ] Viết consumer assertions cho cả hai languages: health 200/500/503, Problem/reason/correlation; ETag response và `If-Match` mutation, 412/428 không retry; required/nullable/additionalProperties, cursor encoding, UUID/date/numeric và AutomationRule valid variants.
- [ ] Thêm negative assertions: business token/tenant không xuất hiện trên operations request; `tenant_id` không tự đưa vào body; cancellation được truyền; captured outputs không in synthetic sentinel token. Schema validator riêng reject invalid DSL; không claim SDK runtime validation đầy đủ.
- [ ] Chạy packaged consumer suite: RED khi thiếu package/header/error behavior. Implement package metadata và adapter tối thiểu; không sửa generated files thủ công.
- [ ] Pack/install/build từ directory sạch, inspect dependencies/assemblies không có Platform.Api/Infrastructure hoặc web imports; focused suite GREEN. Commit `test(sdk): verify independent packages and wire semantics`.

## Task 3: OpenAPI, security và JSON Schema compatibility gate

**Files:** `scripts/contracts/compatibility.py`, `tools/contracts/` policy/lock, `tests/Contract/test_compatibility.py`, `tests/Contract/fixtures/compatibility/`.

**Interfaces:** `compatibility.py --base-ref <ref>` resolve merge-base của ref với HEAD; default `origin/main`. Đọc base contracts từ Git object, compare union base/current paths; no fetch/reset. Output classification + base SHA/input hashes + reason/correlation; non-compatible/error exit nonzero.

- [ ] Viết valid-base mutations: unchanged/description-only và additive-safe pass; removed operation/response/property, required request field, narrowed enum/type/nullable và ETag/If-Match changes fail đúng classification.
- [ ] Viết security mutations: effective root/operation security, `x-required-scopes`, bearer scheme và `webhookSignature` header/location; xóa/đổi phải fail `security-review-required`, kể cả wire tool coi compatible. Reordered scope/object keys không false-positive.
- [ ] Viết schema/input negatives: changed existing validation keyword → `SCHEMA_COMPATIBILITY_REVIEW_REQUIRED`; title/description/comment-only pass; removed/renamed schema, malformed/unsupported spec và missing base fail. Candidate config/waiver không thể ẩn input baseline.
- [ ] Chạy actual pinned diff tool trên fixtures: RED với wrapper stub; implement discovery, local ref validation, severity mapping và security/schema checks. Không tải remote `$ref` hoặc báo compatible khi tool lỗi.
- [ ] Focused suite GREEN; existing contract/HMAC tests pass. Ghi rõ JSON Schema gate là conservative review detector, không semantic proof. Commit `feat(contracts): add local compatibility and security gates`.

## Task 4: Baseline SDK surface và toolchain changes

**Files:** `scripts/contracts/compatibility.py`, `tools/contracts/` policy, TS/.NET public API snapshot helpers, consumer fixtures và `test_compatibility.py`.

**Interfaces:** Khi base đã có generator/config/lock, generate base bằng **base toolchain** và candidate bằng current toolchain; compare exported declarations/.NET public API + compile baseline consumer. Bootstrap đầu khi base chưa có SDK dùng cùng selected pinned config cho cả hai và ghi rõ chưa có released consumer.

- [ ] Viết `test_additive_exports_pass_but_existing_signature_break_fails`: additive operation/unreferenced model compatible pass; rename/remove operationId/type/member hoặc incompatible signature/nullability fail `SDK-surface-incompatible`.
- [ ] Viết `test_generator_only_break_is_not_hidden`, `test_missing_base_toolchain_fails_closed`, `test_policy_or_waiver_edit_cannot_rebaseline`: unchanged HTTP contract nhưng generator/config tạo incompatible API phải fail; missing toolchain/policy chưa chứng minh được báo `SDK_TOOLCHAIN_REVIEW_REQUIRED`/`CONTRACT_POLICY_REVIEW_REQUIRED`.
- [ ] Chạy focused tests RED; implement pinned public-surface comparison và baseline consumer compilation. Không auto-rebaseline hoặc nhận waiver trong current worktree như approval.
- [ ] Focused tests GREEN, rerun Task 3 fixtures và Task 2 consumers; commit `feat(contracts): verify baseline SDK surface and toolchain drift`.

## Task 5: Frontend workspace và same-origin proxy

**Files:** `apps/crm-web/` package/config/source/tests, root workspace lock, Vite proxy tests và README development instructions.

**Interfaces:** `npm run dev -w apps/crm-web` bind localhost:5173 strict; Vite proxy chỉ exact health paths tới `127.0.0.1:${LOCAL_API_PORT:-5080}`. `HealthClient` dùng generated operations package, không import CRM server code. Static build không claim production proxy.

- [ ] Viết tests cho valid/default port, malformed/non-loopback target, foreign listener on 5173 và path ngoài allowlist; assert fail an toàn, không auto-select port/CORS wildcard.
- [ ] Chạy tests RED; implement strict Node/npm workspace, React/TS strict, minimal navigation/heading và Vite allowlist. Không load toàn environment vào browser bundle.
- [ ] `npm ci`, frontend typecheck/lint/build và focused proxy tests GREEN; validate root Compose/Keycloak config không đổi. Commit `feat(web): add pinned frontend and restricted local proxy`.

## Task 6: Health UI, cancellation và browser behavior

**Files:** `apps/crm-web/src/health/`, component tests, `apps/crm-web/tests/e2e/health.spec.ts`, Playwright config/lock.

**Interfaces:** `checkHealth(signal: AbortSignal): Promise<HealthResult>` dùng operations client; discriminated result ready/unavailable/timeout/network/invalid. Tổng deadline 5s gồm body; server correlation chỉ khi hợp lệ; local codes `WEB_REQUEST_TIMEOUT`, `WEB_NETWORK_ERROR`, `WEB_CONTRACT_INVALID`.

- [ ] Viết Vitest cases: loading→ready, 503 Problem, network/invalid body, hung body timeout, refresh/unmount cancellation và stale response race. Assert `aria-live`, keyboard retry, không render raw HTML/stack/token/query. Test controlled server reason/correlation và forged invalid correlation.
- [ ] Chạy tests RED; implement adapter + UI tối thiểu, React escaping, no persisted health/auth data, no retry/poll vô hạn. Không bịa server correlation khi network lỗi.
- [ ] Viết Playwright tests intercepted 503/timeout/XSS và ít nhất một smoke đi qua dev proxy tới API/PostgreSQL thật của stack test-owned. Assert actual request/response, không screenshot-only; foreign service/port không được masquerade thành stack.
- [ ] Vitest, Playwright và build GREEN; scan bundle/sourcemaps/browser/process logs bằng synthetic sentinels, không dump payload khi fail. Commit `feat(web): display safe operational readiness`.

## Task 7: Aggregate checks, clean export và evidence

**Files:** `scripts/check-plan3.sh`, `docs/engineering/07-WAVE-0-PLAN-3-VERIFICATION.md`, README, `docs/contracts/README.md`, roadmap/acceptance matrix, spec status, plan checkboxes, `docs/handoff/`.

- [ ] Aggregate locked install → generated-check → SDK build/pack/consumers → compatibility gate → frontend typecheck/lint/test/build/browser; nhận explicit base ref, không fetch/reset/mutate source. Run negative tampered-generated/base-missing/policy-change cases và assert fail đúng lý do.
- [ ] Chạy existing contract + Local Python suites và relevant backend/real browser stack tests. Ghi exact commands/counts; Plan 2 Compose suite chỉ reuse historical exact-SHA evidence nếu source/config/lifecycle không thay đổi; nếu thay phải chạy affected/broader gates. Không claim reused suite đã rerun.
- [ ] Commit candidate source, `git archive` exact SHA vào thư mục sạch, fresh locked install/cache được ghi rõ; chạy aggregate, local packaged consumers và browser smoke với test-owned stack. `--base-ref` phải trỏ tới trusted Git object có sẵn: cung cấp read-only Git object context hoặc controlled baseline repo cho export, không làm missing history thành empty baseline. Ghi base/tested SHA và hashes.
- [ ] Map W0P3-01→12: 01 Task5/7; 02–03 Task1; 04–05 Task2; 06–07 Task6; 08 Task5/6; 09 Task3/4; 10 Task1/3/4; 11 Task2/5/6; 12 Task7. Ghi tool/package/browser versions, digests, exit/counts, local-only limitations và safe artifacts.
- [ ] Cập nhật docs/handoff: operations routes implemented, business spec pre-runtime; Wave0/P0, auth/tenant/remote DemoModule/DR và remote CI OPEN. Read-only whole-branch review; sửa findings với regression tests và refresh exact-SHA evidence nếu source đổi. Commit `docs: record Plan 3 verification and handoff`; không merge/push/deploy.

## Self-review và handoff

- Toàn bộ W0P3-01→12 map tới task/test ở trên; security extensions, conditional schema fidelity và generator-only surface drift có gate riêng.
- Không implement Plan 3 trong lượt viết plan. Exact versions và stable generator được builder chọn sau compatibility proof; nếu không có giải pháp đúng contract, báo blocker thay vì downgrade.
- Khuyến nghị SDD tuần tự theo tiền lệ, một implementer tại một thời điểm. Branch/worktree và execution chỉ thực hiện khi người dùng yêu cầu triển khai; không hỏi lại giữa các task sau khi được ủy quyền.
