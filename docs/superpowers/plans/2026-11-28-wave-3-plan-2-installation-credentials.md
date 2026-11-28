# Wave 3 Plan 2 — Installation and Credentials Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement task-by-task. Steps use checkbox (`- [ ]`) syntax. One coding agent at a time under AGENTS.md.

**Goal:** Cài và quản lý installation theo tenant, gắn approval với snapshot đã xác minh, và chỉ kích hoạt credential sau challenge/health thành công.

**Architecture:** API dùng tenant context và membership đã xác thực; POST ghi installation cùng work item trước 202, worker dùng pipeline candidate của Plan 1 ngoài DB transaction. Approval và lifecycle dùng version/ETag, immutable snapshot, current publisher trust và audit; secret được giữ trong secret provider qua reference, credential binding có phạm vi tenant/installation/audience/scope. Contract cho provisioning, challenge và một số trạng thái chưa đủ đặc tả: Task 0–1 phải giải quyết và review trước khi code phụ thuộc, không tự chế giao thức.

**Tech Stack:** .NET 10, PostgreSQL/Npgsql/EF migrations, xUnit/Testcontainers, OpenAPI 3.1, SDK TypeScript/.NET hiện có; external IdP/secret provider hoặc test double độc lập theo contract được duyệt.

**Spec:** [Wave 3 design](../specs/2026-11-28-wave-3-integration-kernel-design.md); [Plan 1 handoff](../../handoff/2026-11-28-wave-3-plan-1-handoff.md); [OpenAPI](../../contracts/crm-api.openapi.yaml); [module callbacks](../../contracts/MODULE-CALLBACKS.md); [webhook protocol](../../contracts/WEBHOOK-PROTOCOL.md); [data model](../../architecture/06-DATA-MODEL.md); [ADR-022](../../architecture/12-ADR-022-MODULE-EVENT-NAMESPACE.md).

## Global Constraints

- Người dùng đã cho tiếp tục Wave 3 khi security deferred theo handoff Plan 1; xác minh lại candidate và chỉ thị trước code. Security và aggregate vẫn FAIL/deferred, Wave 2/P0 OPEN; không claim acceptance. Plan 1 candidate `b80df59` có focused evidence, không suy diễn full CI.
- Test liên quan local RED → GREEN → refactor, PostgreSQL thật/Testcontainers cho RLS, transaction và race; full suite thuộc Plan 9 trên GitHub CI. Không tắt mandatory CI gate. Ghi lệnh, SHA, pass/fail/skip và hạn chế.
- Dùng public contract trước; breaking diff phải có ADR, migration plan và independent old/new consumer tests. Contract mới cần review và SDK generation; approval plan không tự duyệt thay đổi contract hoặc giao thức bảo mật.
- Tenant từ verified membership hoặc authenticated credential binding; không tin tenant trong payload/header, module identity, source hoặc secret reference. Mọi tenant-bound row/query/mutation dùng tenant context, RLS/FORCE, tenant-aware FK và app role least privilege. Contact khác identity user.
- Install/approval phải revalidate current publisher key/status/origins/event namespace ownership theo ADR-022; trust cũ trong catalog chỉ là historical evidence. Snapshot manifest/schema, grants và approved origins immutable theo version; cannot reuse approval cho hash, scopes hoặc origins khác.
- Không log/commit credentials, bearer tokens, raw challenge hay secret refs nhạy cảm; audit chỉ metadata cần thiết. Không network trong DB transaction; mọi failure có reason token và correlation ID. Không giả lập acceptance khi secret store hoặc challenge unavailable.
- Webhook rotation dùng key mới, overlap mặc định 24h; compromise revoke tức thời. Disable/uninstall ngăn ingress và claim/send mới, revoke binding trước khi trả success, giữ history; in-flight có thể hoàn tất. Re-enable cần fresh credential/challenge và current approval, replay skipped phải explicit.
- External identity, webhook ingestion, outbox dispatch, action execution và UI thuộc plan sau; cung cấp enforcement boundary/test seam cho chúng, không tuyên bố chúng hoạt động.

## Review Focus

1. Hai approval cùng ETag hoặc approval của hash cũ sau candidate upgrade: chỉ state hợp lệ được commit; không activate sai snapshot (Tasks 3–4).
2. Revoke publisher/namespace, membership hoặc installation trong lúc fetch/challenge: kiểm tra lại trước commit/use; không tự dựa vào trust revision đã chụp (Tasks 2–5).
3. POST bị crash sau commit và trước response, hoặc provider timeout sau tạo secret: work bền vững, không lặp credential và không active khi kết quả chưa rõ (Tasks 2, 4).
4. Credentials nhầm tenant, installation, audience, scope, key đang overlap hoặc đã revoke: fail closed; không lộ sự tồn tại trước auth (Tasks 3–5).
5. Cross-origin và DNS rebinding, secret-provider outage hoặc DB commit failure: không bypass approval/egress, không có active orphan credential hoặc audit giả (Tasks 1, 4–5).

## Current baseline và ranh giới contract

- `ManifestCandidateRegistration.RegisterAsync(Uri, FetchPolicy, PublisherTrustSnapshot, Guid, CancellationToken)` là pipeline nội bộ hiện tại; registry lưu verified candidate, không chứng minh tenant approval hay trust hiện hành. Không sửa/đọc trực tiếp bảng registry để bỏ qua verification; bổ sung read port hẹp nếu cần.
- OpenAPI hiện có `listInstallations`, `installModule` (202), `getInstallation` (ETag), `approveInstallation`, `changeInstallationState`, `uninstallModule`; mutable operations dùng If-Match. `InstallModule` chỉ có `manifestUrl`; `ApproveInstallation` chỉ có `manifestHash` và `grantedScopes`.
- Gaps phải review trước implement: (a) 202 trả schema `Installation` bắt buộc moduleId/version/hash dù chưa fetch; (b) approval hiện không có approved origins; (c) signed challenge, bootstrap/provisioning, secret provider, outbound OAuth audience/grants và rotation/revoke control chưa có wire contract; (d) durable async failure/retry và idempotency của POST chưa định nghĩa đủ. Không dùng URL đưa secret hoặc end-user token làm shortcut.

## File map và interfaces

- Contract: `docs/contracts/crm-api.openapi.yaml`, `docs/contracts/MODULE-CALLBACKS.md`, thêm schema/fixtures challenge và ADR tại `docs/architecture/` khi cần; generated `sdks/` chỉ qua generator. `tests/Contract/test_wave3_installation.py` và independent consumer fixtures kiểm tra old/new contract.
- Runtime dự kiến: `src/Platform.Api/Integration/InstallationEndpoints.cs`, `src/Platform.Api/Identity/TenantAuthorization.cs`, `src/Platform.Api/Program.cs`; `src/Platform.Infrastructure/Integration/InstallationStore.cs`, `InstallationWorker.cs`, `InstallationChallengeClient.cs`, `CredentialBindings.cs`, `InstallationTrustValidator.cs`, `IIntegrationSecretStore.cs`; migration trong `src/Platform.Infrastructure/Persistence/Migrations/`, grants/readiness cập nhật theo convention.
- Test dự kiến: `tests/Integration/InstallationSchemaTests.cs`, `InstallationEndpointsTests.cs`, `InstallationWorkerTests.cs`, `InstallationCredentialsTests.cs`, `InstallationConcurrencyTests.cs`, cùng isolated HTTPS/IdP/secret-store fixture. Tên và signatures nội bộ dưới đây là boundary hướng dẫn, không thay thế contract đã duyệt.
- `InstallationStore.CreatePendingAsync(VerifiedTenant tenant, Uri manifestUrl, Guid correlationId, CancellationToken ct)` commit installation + durable work; `InstallationWorker.ProcessAsync(Guid workId, CancellationToken ct)` claim lease, fetch/verify ngoài transaction và guarded commit; `InstallationTrustValidator.ValidateCurrentAsync(candidate, ct)` kiểm key, origin và namespace ownership. Kiểu `VerifiedTenant`, candidate và result phải khớp code repo thực; nếu khác, dùng adapter typed trong cùng task, không truyền tenant chưa xác minh.
- `IIntegrationSecretStore` cấp/đọc/revoke bằng opaque reference, hỗ trợ idempotency và reconciliation theo contract đã duyệt; `InstallationChallengeClient` chỉ trả bằng chứng challenge đã validate ràng buộc installation/snapshot/source/nonce/expiry. Không định nghĩa chữ ký hay field wire trước Task 1.

## Task 0: Preflight và quyết định contract

**Files:** handoff đúng ngày; đọc Plan 1 code/evidence, OpenAPI, callbacks và security/acceptance docs.

- [ ] Ghi branch/SHA, dirty state, bằng chứng Plan 1 và authorization tiếp tục; security FAIL/deferred và các acceptance còn OPEN. Xác định actual trust provider, candidate read path, egress policy và tenant authorization đang có; không coi internal registry là API public hay trust hiện hành.
- [ ] Lập bảng từng gap (a)–(d) ở trên: contract hiện tại, lựa chọn khả thi, diff dự kiến, consumer bị ảnh hưởng, migration/rollback và test cần có. Phân biệt HMAC webhook inbound, OAuth client-credentials outbound, challenge credential và publisher Ed25519; không dùng cùng secret cho các mục đích khi chưa được thiết kế.
- [ ] Đề xuất cụ thể phương án async 202, signed challenge chống replay (nonce, expiry, binding và key provisioning), secret-provider availability/idempotency/recovery, origin approval, rotation và trust update race. Xin review/approval contract phù hợp trước Task 1 nếu cần thay đổi public/bảo mật; khi chưa có quyết định, dừng phần phụ thuộc và ghi blocker, không phát minh wire protocol. Không coi plan này là ADR hoặc approval.

## Task 1: Contract và consumer tests

**Files:** `tests/Contract/test_wave3_installation.py`; sửa OpenAPI/callback/schema/fixtures, SDK, ADR/migration docs nếu và chỉ nếu Task 0 quyết định yêu cầu.

**Consumes:** quyết định đã duyệt Task 0. **Produces:** canonical request/response, problem tokens, challenge/credential protocol, approval origins, async states và compatibility evidence cho tasks sau.

- [ ] Viết tests contract cho POST durable 202 và schema thực sự represent được trạng thái chưa fetch; list/get, ETag, approve hash/scope/origin, state patch/delete, 400/401/403/404/409/412/428/503; schema strict reject duplicate/unknown properties. Kiểm tra challenge sai nonce/source/installation/hash, replay/expiry và consumer không nhận secret không cần thiết.
- [ ] Chạy `python3 -m unittest discover -s tests/Contract -p 'test_wave3_installation.py' -v`; ghi RED thực tế trước thay đổi. Cập nhật contract theo quyết định Task 0, kèm ADR và migration/old/new consumer tests nếu breaking; không xóa response fields hoặc giả lập giá trị rỗng để làm vừa schema.
- [ ] Chạy generator/check và consumer tests bị ảnh hưởng, compatibility gate với actual base theo policy repo; ghi reviewed-breaking khác compatible và từng kết quả. Test contract PASS, commit contract/fixture/generated files được review.

## Task 2: Tenant installation store và durable async processing

**Files:** migration mới theo timestamp trống, `InstallationStore.cs`, `InstallationWorker.cs`, `InstallationSchemaTests.cs`, `InstallationWorkerTests.cs`; cập nhật `DatabaseRoleGrants.cs`, migration inventory và API composition khi cần.

**Consumes:** Task 1 contract và Plan 1 candidate pipeline. **Produces:** tenant-scoped installation, immutable snapshot reference, version và durable work với stable ID/lease; chưa cấp active credential.

- [ ] Viết test PostgreSQL: tenant A/B isolation, RLS/FORCE và app role; 202 chỉ sau installation + work commit, DB fail/rollback không partial; idempotency semantics theo Task 1 và timeout/unknown commit; 2 workers/lease expiry/crash recovery không tạo candidate sai. Revoke membership/trust trong khi fetch ngăn guarded commit. Snapshot/version không ghi đè và worker error có reason/correlation an toàn.
- [ ] `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~InstallationSchemaTests|FullyQualifiedName~InstallationWorkerTests'` RED; implement migration, tenant-aware FKs và worker, fetch ngoài transaction, claim với bounded lease/fencing và retry theo contract đã duyệt. Lưu failure trạng thái có thể quan sát, không ACK khi commit không chắc.
- [ ] Chạy lại focused tests PASS; kiểm upgrade, guarded rollback có dữ liệu, grants/readiness và audit transaction. Commit migration/store/worker/tests.

## Task 3: Approval, current trust và lifecycle API

**Files:** `InstallationEndpoints.cs`, `InstallationTrustValidator.cs`, `InstallationEndpointsTests.cs`, `InstallationConcurrencyTests.cs`; modify auth/Program/store theo convention hiện có.

**Consumes:** Task 1 contract, Task 2 store và Plan 1 operator trust snapshot. **Produces:** list/get/create/approve/state/delete tenant-scoped, versioned và audit; active chỉ sau Task 4.

- [ ] Viết tests: membership active và quyền `integrations.install`, anonymous/role denied, tenant A đọc/mutate ID B không lộ tồn tại; cursor tenant-bound; missing/stale/malformed If-Match trả 428/412/400. Hai approvals cạnh tranh, stale hash, unrequested scope, origin chưa duyệt hoặc operator denied đều fail; snapshot change/new scope-origin cần approval mới, downgrade fail.
- [ ] Kiểm thử current key revoked, namespace reassigned, origins thay đổi, approval/membership revoke và challenge chậm: no active/grant từ trust stale. Nếu trust nằm ngoài PostgreSQL, định nghĩa serialization/fencing hoặc fail-closed revalidation với race test theo quyết định Task 0; không hứa tính atomic chỉ từ đọc revision hai lần.
- [ ] Chạy `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~InstallationEndpointsTests|FullyQualifiedName~InstallationConcurrencyTests'` RED; implement auth/endpoint, typed Problem reason/correlation, ETag và audit cùng transaction, không tiết lộ secrets. Giữ state không active cho đến khi Task 4 chứng minh challenge.
- [ ] Chạy lại focused tests PASS, kể cả race và audit rollback; commit API/tests. Không claim full install hoạt động trước Task 4.

## Task 4: Secret provider, challenge và activation

**Files:** `IIntegrationSecretStore.cs`, `CredentialBindings.cs`, `InstallationChallengeClient.cs`, `InstallationCredentialsTests.cs`, isolated provider/module test fixtures và configuration theo contract Task 1.

**Consumes:** approved snapshot/version Task 3 và protocol được duyệt Task 1. **Produces:** credential binding tenant/installation/key/audience/scopes/source cùng secret reference; active chỉ sau verified challenge/health và commit guarded.

- [ ] Viết tests signed challenge valid và sai signature/nonce/source/install/hash/expiry, replay, wrong audience/scope/tenant, inactive key, health failure, SSRF/DNS rebinding và provider unavailable; không active hoặc ghi secret vào response/log/audit. Test credential lookup pre-auth tối thiểu không cấp global tenant data và trả lỗi đồng nhất cho unknown/revoked.
- [ ] Test crash/timeout ở trước và sau provider provisioning, duplicate worker/challenge, DB failure sau tạo secret, revoke giữa challenge và commit, và retry: không tạo credential usable mồ côi; provider reconciliation/compensation theo protocol đã duyệt, không giữ network trong DB transaction. Fresh credential/challenge bắt buộc khi re-enable.
- [ ] `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~InstallationCredentialsTests'` RED; implement narrow secret provider, challenge/health qua SafeFetchClient/egress guard, binding và guarded activation. Nếu secure provider hoặc provisioning contract chưa có, fail closed; không thay bằng hardcoded credential hay secret plaintext.
- [ ] Chạy focused tests PASS với independent fixture và PostgreSQL thật; ghi limitations, secret lifecycle, audit và safe error mapping. Commit code/tests/config documentation.

## Task 5: Rotation, disable, uninstall và recovery

**Files:** credential/lifecycle store và worker từ Tasks 2–4, `InstallationCredentialsTests.cs`, `InstallationConcurrencyTests.cs`, operator guide.

- [ ] Viết tests rotation giữ old/new key tối đa 24h, hết overlap/revoke từ chối old, compromise revoke ngay; race send/disable, cancel/revoke và re-enable với fresh challenge, uninstall tombstone/ID không tái dùng và history còn nguyên. Đảm bảo outbound/inbound authorization checks dùng binding mới; không coi test seam là webhook hay dispatcher đã triển khai.
- [ ] Test revoke store/provider unavailable, crash giữa revoke và DB state: fail closed trước 200/204, durable remediation không tái activate; in-flight request có outcome được ghi khi future dispatcher tồn tại, không tuyên bố hủy remote effect. Không replay skipped tự động.
- [ ] Chạy `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~InstallationCredentialsTests|FullyQualifiedName~InstallationConcurrencyTests'` RED; implement version predicate, restricted lookup, audit và bounded recovery theo Task 1. Run focused GREEN; commit code/tests.

## Task 6: Focused review, docs và handoff

**Files:** `docs/engineering/03-ACCEPTANCE-MATRIX.md`, operator runbook tại `docs/operations/`, handoff đúng ngày.

- [ ] Chạy focused contract suite Task 1 và `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~InstallationSchemaTests|FullyQualifiedName~InstallationWorkerTests|FullyQualifiedName~InstallationEndpointsTests|FullyQualifiedName~InstallationCredentialsTests|FullyQualifiedName~InstallationConcurrencyTests'`; chạy liên quan Plan 1 regression nếu sửa pipeline/trust/egress. SDK/consumer/actual-base gate khi contract đổi. Ghi candidate SHA, kết quả pass/fail/skip, thời gian và dependencies; không chạy full suite local.
- [ ] Review tenant/cross-tenant, trust use-time, egress, secret lifecycle, race/crash, migration/rollback và audit/log redaction; ghi provider provisioning, revoke/rotation, recovery và reason tokens/correlation. Cập nhật matrix chỉ với evidence đạt; US-06 và Wave 3 exit còn OPEN khi các phần khác chưa chứng minh.
- [ ] Ghi handoff bao gồm decisions, approvals, tests, blocker, commit và việc còn lại. Security FAIL/deferred; Wave 2/P0 OPEN, full CI thuộc Plan 9. Commit docs theo yêu cầu thực thi sau này.
