# Wave 2 Plan 1 — Contact CRUD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. One coding agent at a time, as required by AGENTS.md.

**Goal:** Cung cấp contact create/get/patch/soft-delete qua API tenant-scoped, với optimistic concurrency và audit/outbox atomic.

**Architecture:** Mở rộng API .NET và PostgreSQL hiện có, tái sử dụng xác thực OIDC, verified tenant transaction và Problem response. CRM lưu contact trong schema riêng; audit và outbox được append qua infrastructure trong cùng transaction. Truy vấn list/filter, tags, fields, timeline và UI thuộc plan sau.

**Tech Stack:** .NET 10, Npgsql/EF migrations, PostgreSQL và Testcontainers, xUnit, OpenAPI hiện có và generated TypeScript client.

**Spec:** [Wave 2 CRM Contacts](../specs/2026-11-27-wave-2-crm-contacts-design.md); [access/state](../../architecture/07-P0-ACCESS-AND-STATE.md); [data model](../../architecture/06-DATA-MODEL.md); [CRM OpenAPI](../../contracts/crm-api.openapi.yaml).

## Global Constraints

- Trước code phải xác định các điều kiện Wave 1 không thuộc security đã đạt trên candidate thích hợp; nếu chưa đủ evidence thì ghi blocker, không triển khai. Security FAIL/deferred không phải PASS; P0 vẫn OPEN.
- RED → GREEN → refactor; từng plan chỉ chạy test liên quan local. Full suite chỉ chạy ở Wave 2 exit trên GitHub CI, không chạy local. Không tự vô hiệu hóa CI bắt buộc.
- Integration tests dùng PostgreSQL thật qua `PostgresFixture`/Testcontainers và application role thực; không thay bằng in-memory.
- Tenant context từ membership đã xác thực, không từ body; issuer/sub không phải contact identity. Bảo vệ cả API, SQL, RLS và pool. Không log tokens, secrets hoặc PII.
- Public contract-first; breaking diff cần ADR, migration plan và consumer tests. Không tự cập nhật trusted baseline.
- Mọi mutation contact thành công commit contact, audit và outbox cùng transaction; không remote I/O. Mọi lỗi đóng có reason token và correlation ID.
- Migrations additive, least privilege, `FORCE ROW LEVEL SECURITY`; rollback chỉ khi an toàn, nếu có dữ liệu thì hướng dẫn backup/forward-fix.
- Không chạy integration kernel/worker, UI hoặc list/search ở plan này; không claim Wave 2 hay P0 exit.

## Review Focus

1. Body có `tenantId`, duplicate property hoặc unknown property: từ chối, không đổi tenant hoặc ghi dữ liệu (Task 3).
2. Hai PATCH/DELETE cùng ETag: đúng một mutation thắng; loser 412, không có audit/outbox thừa (Task 4).
3. Tenant A dùng ID của B, tenant thiếu hoặc membership bị thu hồi: không đọc/đổi dữ liệu B, không rò sự tồn tại (Task 3–4).
4. PATCH bỏ qua field khác với explicit null; invalid/multiple/weak If-Match không ghi dữ liệu (Task 4).
5. Audit/outbox insert hoặc commit thất bại: không trả thành công và không để contact/audit/outbox chỉ được ghi một phần (Task 2, 4).

## File map và interface

- `docs/contracts/crm-api.openapi.yaml`, `docs/contracts/` event schema/fixtures: contract contact và durable event; giữ operation IDs `createContact`, `getContact`, `updateContact`, `softDeleteContact`.
- `src/Platform.Infrastructure/Persistence/Migrations/`: migration CRM/outbox; `DatabaseRoleGrants.cs`: grants và validation cho schema mới.
- `src/Platform.Infrastructure/Crm/ContactStore.cs`: SQL tenant/version predicate; `src/Platform.Infrastructure/Crm/ContactMutationJournal.cs`: append audit/outbox dùng cùng `NpgsqlConnection` và `NpgsqlTransaction` của caller.
- `src/Platform.Api/Identity/TenantAuthorization.cs`: thêm `ContactsRead`, `ContactsWrite`; `src/Platform.Api/Crm/ContactEndpoints.cs`: HTTP validation/mapping; `src/Platform.Api/Program.cs`: route registration. Tái dùng `TenantEndpointTransaction` và `MembershipPreconditions` khi phù hợp; sửa error mapping nếu cần để lỗi CRM có token riêng, không làm suy yếu IAM.
- `tests/Integration/ContactSchemaTests.cs`, `ContactEndpointsTests.cs`, `ContactConcurrencyTests.cs`: schema/privilege và API transactional tests; dùng `Fixtures/PostgresFixture.cs`, `ApiFactory.cs`, `FakeOidcIssuer.cs` như các tests tenant hiện có. `tests/Contract/test_contact_crud.py`: contract và schema examples.
- `sdks/typescript/crm/` và các generated artifacts khác chỉ cập nhật bằng `scripts/contracts/generate.py` nếu contract thay đổi; không sửa tay.

## Task 0: Xác minh điều kiện vào wave

**Files:** Modify `docs/handoff/2026-11-27-wave-2-branch-handoff.md` (hoặc handoff đúng ngày khi thực hiện).

- [ ] Đối chiếu [Wave 1 exit](../../engineering/11-WAVE-1-EXIT-VERIFICATION.md), [handoff CI](../../handoff/2026-11-27-wave-1-pr3-ci-handoff.md) và candidate thực tế: liệt kê từng yêu cầu Wave 1 không thuộc security, SHA và evidence pass/fail/missing; không chuyển kết quả PR sang SHA khác mà thiếu căn cứ.
- [ ] Ghi kết luận và links evidence. Nếu bất kỳ điều kiện áp dụng nào thiếu hoặc fail, dừng trước Task 1 và báo blocker; không dùng việc viết plan làm bằng chứng cho phép thực thi. Security giữ FAIL/deferred theo quyết định hiện tại.

## Task 1: Contract CRUD và event intent

**Files:** Modify `docs/contracts/crm-api.openapi.yaml` chỉ khi cần; create event schema/fixtures trong `docs/contracts/` theo cấu trúc schema hiện có; create `tests/Contract/test_contact_crud.py`; generate SDK nếu contract đổi.

**Interfaces:** HTTP contract là bốn operation hiện có. `Contact`: id, displayName, email/phone nullable, version, createdAt, updatedAt; `CreateContact` yêu cầu displayName; `UpdateContact` ít nhất một property, omitted giữ nguyên, null chỉ xóa email/phone.

- [ ] Viết test contract kiểm tra create/get/patch/delete, `additionalProperties: false`, độ dài (displayName 1–200, email ≤320, phone ≤40), ETag strong và status 400/412/428. Test event schema từ chối thiếu event identity, tenant, contact, event type, thời gian và payload không hợp lệ; schema/fixture không chứa PII không cần thiết.
- [ ] Chạy `python3 -m unittest discover -s tests/Contract -p 'test_contact_crud.py' -v`; ghi RED thực tế trước thay đổi contract/schema.
- [ ] Review contract hiện có và thiết kế tối thiểu event intent versioned, có stable event ID, tenant ID, contact ID, loại và thời gian; `crm.contact.created.v1` đã được tài liệu gọi tên. Định nghĩa rõ event type còn lại, payload, version và consumer compatibility trong schema/fixtures trước code; không tuyên bố đã có consumer hoặc delivery. Nếu cần breaking API diff, dừng thực thi cho tới khi ADR, migration và consumer tests được phê duyệt.
- [ ] Nếu contract đổi, chạy `python3 scripts/contracts/generate.py` và `python3 scripts/contracts/check.py`; kiểm tra generated diff, không sửa bằng tay. Nếu không đổi, xác minh SDK hiện có đã có bốn operation và ghi rõ không regeneration.
- [ ] Chạy lại test trên: PASS. Commit contract/fixtures/tests/generated artifacts liên quan; không commit token hoặc dữ liệu thật.

## Task 2: PostgreSQL schema, quyền và journal atomic

**Files:** Create migration trong `src/Platform.Infrastructure/Persistence/Migrations/` theo convention repo, `src/Platform.Infrastructure/Crm/ContactMutationJournal.cs`, `tests/Integration/ContactSchemaTests.cs`; modify `DatabaseRoleGrants.cs` và snapshot nếu migration tooling yêu cầu.

**Interfaces:** `ContactMutationJournal.AppendAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid contactId, string issuer, string subject, string action, string correlationId, ...event intent..., CancellationToken ct)` không commit; caller sở hữu transaction. Tham số event dùng kiểu được định nghĩa cùng implementation theo schema Task 1.

- [ ] Viết schema tests: migration từ DB trước migration; composite `(tenant_id,id)`, contact version ban đầu 1, email nullable/không unique, soft-delete và indexes; RLS ENABLE/FORCE, thiếu context không thấy dữ liệu, context A không thấy B. App role không owner/BYPASSRLS, không CREATE schema hay quyền rộng lên audit; pool không giữ tenant cũ. Test rollback migration DB rỗng và fail-closed khi có dữ liệu, kèm forward-fix.
- [ ] Viết test journal: insert contact + audit + outbox trong một transaction rồi rollback để cả ba không tồn tại; lỗi append audit hoặc outbox không để contact hay entry còn lại tồn tại. Kiểm tra event ID stable sau commit và payload khớp schema Task 1.
- [ ] Chạy `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~ContactSchemaTests'`; xác nhận RED vì schema/journal chưa có.
- [ ] Tạo CRM table và outbox durable intent tối thiểu theo data model; append audit qua port/SQL có quyền giới hạn, không cấp quyền sửa/xóa audit. Xác minh `DatabaseRoleGrants.ValidateAsync` bao phủ schema CRM/outbox và mọi role có thể assume. Transaction là đơn vị sở hữu bởi caller, không bắt exception rồi commit.
- [ ] Chạy lại lệnh trên: PASS; ghi số test và migration/rollback thực tế. Commit schema, journal và tests.

## Task 3: Create/get và tenant permissions

**Files:** Create `src/Platform.Infrastructure/Crm/ContactStore.cs`, `src/Platform.Api/Crm/ContactEndpoints.cs`, `tests/Integration/ContactEndpointsTests.cs`; modify `TenantAuthorization.cs`, `TenantEndpointTransaction.cs` nếu cần, `Program.cs`.

**Interfaces:** `ContactEndpoints.MapContactEndpoints(IEndpointRouteBuilder endpoints)` đăng ký `POST /api/v1/contacts`, `GET /api/v1/contacts/{id}`; `ContactStore` nhận connection, transaction và tenant ID đã xác minh. Create append journal Task 2 trước commit; GET chỉ đọc record active.

- [ ] Viết tests: owner/admin/agent create/read; operator read nhưng write 403; role không có quyền deny. Anonymous 401, invalid tenant 400, inactive/revoked membership 403, A đọc contact B trả 404. Body tenantId/unknown/duplicate, email sai hoặc vượt giới hạn trả 400 Problem với reason/correlation; không có side effects. Hai contacts cùng email và email null được tạo. POST 201, GET 200, cả hai ETag `"v1"`; response khớp contract và không trả tenant/PII ngoài contract.
- [ ] Chạy `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~ContactEndpointsTests'`; xác nhận RED do route/quyền chưa có.
- [ ] Thêm `ContactsRead` cho owner/admin/agent/operator và `ContactsWrite` cho owner/admin/agent theo bảng quyền; các role khác deny. Dùng verified transaction và staff binding hiện có; không nhận tenant từ JSON. Validate request trước ghi, parameterized SQL, commit trước khi gửi success; lỗi store trả Problem 503 với reason token CRM và correlation, không tiết lộ exception.
- [ ] Chạy lại test Task 3 và Task 2: PASS; xác minh audit/outbox mỗi create đúng một record. Commit API/store/tests.

## Task 4: PATCH, DELETE và cạnh tranh

**Files:** Modify `ContactEndpoints.cs`, `ContactStore.cs`, `ContactEndpointsTests.cs`; create `tests/Integration/ContactConcurrencyTests.cs`.

**Interfaces:** `PATCH /api/v1/contacts/{id}` và `DELETE /api/v1/contacts/{id}` dùng strong `If-Match` từ contract; store mutation predicate `(tenant_id,id,version,deleted_at IS NULL)` và tăng version trong SQL. Phân biệt absent field và JSON null.

- [ ] Viết tests: omitted không đổi, explicit null xóa email/phone, null displayName/empty object/unknown hoặc duplicate field fail; missing If-Match 428 `PRECONDITION_REQUIRED`, stale 412 `VERSION_CONFLICT`, weak/wildcard/multiple/malformed 400 `PRECONDITION_INVALID`. PATCH thành công trả 200 và ETag version mới; DELETE trả 204, sau đó GET 404. Cross-tenant ID trả 404 kể cả version trùng, không lộ tồn tại.
- [ ] Viết tests đồng thời với cùng `"v1"`: hai PATCH, PATCH/DELETE, hai DELETE; mỗi cặp đúng một thành công và một 412, version/audit/outbox chỉ phản ánh một mutation. Inject lỗi journal trước commit: không success, contact và counts bất biến. Kiểm tra retry stale không tạo mutation giả.
- [ ] Chạy `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~ContactConcurrencyTests|FullyQualifiedName~ContactEndpointsTests'`; xác nhận RED.
- [ ] Implement PATCH/DELETE, reuse parser `MembershipPreconditions` nếu giữ nguyên semantics; resolve not-found/stale trong tenant transaction không leak cross-tenant, và đảm bảo race được serialize hoặc atomic predicate. Append journal và commit trước response. Dùng Problem reason/correlation và không ghi raw request vào log.
- [ ] Chạy lại lệnh trên và Task 2: PASS; kiểm tra persisted audit/outbox với SQL dùng migration role chỉ trong test. Commit implementation/tests.

## Task 5: Review và kết thúc Plan 1

**Files:** Update `docs/engineering/03-ACCEPTANCE-MATRIX.md`, tài liệu migration/verification phù hợp, `docs/handoff/YYYY-MM-DD-wave-2-handoff.md`.

- [ ] Chạy lại **chỉ** các test liên quan: `python3 -m unittest discover -s tests/Contract -p 'test_contact_crud.py' -v` và `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~ContactSchemaTests|FullyQualifiedName~ContactEndpointsTests|FullyQualifiedName~ContactConcurrencyTests'`. Nếu contract/generated SDK đổi, chạy thêm `python3 scripts/contracts/check.py` và consumer test cụ thể bị ảnh hưởng. Ghi command, SHA, số pass/fail/skip, RED/GREEN, môi trường và lỗi thực; không chạy full suite local.
- [ ] Review contract diff, migration upgrade/rollback, quyền application role, isolation, error/correlation và audit/outbox. Cập nhật matrix chỉ cho evidence đã đạt; US-03 vẫn OPEN cho list/filter/performance và Wave 2 exit. Ghi security FAIL/deferred, các việc Plan 2–5, branch/commit, thay đổi chưa commit và blocker vào handoff.
- [ ] Chạy `git diff --check`, commit tài liệu liên quan; báo kết quả chính xác. Full suite và benchmark vẫn thuộc Wave 2 exit, full suite chạy GitHub CI.
