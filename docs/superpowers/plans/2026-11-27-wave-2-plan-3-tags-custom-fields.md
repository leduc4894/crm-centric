# Wave 2 Plan 3 — Tags and Custom Fields Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans`. One coding agent at a time per AGENTS.md. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Cho phép tenant tạo/xem tags và custom field definitions, gắn/gỡ tag và đọc/ghi giá trị field cho contact với kiểm tra quyền, version và audit.

**Architecture:** Mở rộng ASP.NET API và PostgreSQL hiện tại, tái sử dụng bảng tags của Plan 2 và verified tenant transactions. Mutations thuộc contact khóa và kiểm tra version của contact, cập nhật quan hệ/giá trị cùng audit và durable `crm.contact.updated.v1` trong một transaction. Các definition tenant-level được audit riêng, không giả tạo event gắn với một contact.

**Tech Stack:** .NET 10, Npgsql/EF migrations, PostgreSQL/Testcontainers, xUnit, OpenAPI, generated TypeScript/.NET SDK hiện có.

**Spec:** [Wave 2 design](../specs/2026-11-27-wave-2-crm-contacts-design.md); [access/state](../../architecture/07-P0-ACCESS-AND-STATE.md); [data model](../../architecture/06-DATA-MODEL.md); [CRM OpenAPI](../../contracts/crm-api.openapi.yaml); [Plan 2 handoff](../../handoff/2026-11-27-wave-2-branch-handoff.md).

## Global Constraints

- Plan 2 hoàn tất theo handoff: tags foundation read-only đã có; list contact và `tagId` filter đang dùng các bảng đó. Không phá ADR-020/021 hoặc query semantics Plan 1–2.
- TDD RED → GREEN → refactor; chỉ chạy test liên quan local. Full suite chỉ tại Wave 2 exit trên GitHub CI, không chạy full suite local hoặc tắt gate bắt buộc.
- Integration tests dùng PostgreSQL thật qua Testcontainers, application role, verified staff/membership và RLS. Mọi query/mutation tenant-bound có tenant predicate và composite FK; không tin tenant trong body.
- Owner/admin/agent có `crm.contacts.read/write`; operator chỉ read. Chỉ owner/admin có `crm.fields.manage`. Permission phải kiểm tra lại trong transaction sau khi đọc request body; không giữ tenant/membership write lock trong khi chờ body.
- Contact tag/field mutations cần strong `If-Match`: thiếu 428 `PRECONDITION_REQUIRED`, invalid/weak/wildcard/multiple 400 `PRECONDITION_INVALID`, stale 412 `VERSION_CONFLICT`; cross-tenant/missing/deleted trả 404 không lộ dữ liệu. Atomic contact version + audit + outbox; no-op sau kiểm tra version không tăng version hoặc tạo journal giả.
- Contract-first; breaking change cần ADR, migration plan và consumer tests. Scoped `reviewed-breaking` Plan 1 gắn exact evidence, không tự bao phủ Plan 3; không tự rebaseline hoặc sửa gate để pass. Error paths có reason token/correlation ID, không log giá trị field, tag, token hoặc secrets.
- Security vẫn FAIL/deferred; full P0, external event delivery và Wave 2 acceptance/benchmark chưa đóng. UI, timeline, List/Segment, field-definition edit/delete và automation thuộc plan/wave sau.

## Review Focus

1. Cùng tag/field UUID ở tenant khác: không đọc, link hoặc cập nhật chéo; DB FK và RLS phải độc lập bảo vệ (Tasks 1–4).
2. Hai mutation cùng ETag, kể cả tag so với field hoặc PATCH/DELETE contact: chỉ một mutation thật thành công; stale không tạo audit/outbox (Tasks 3–4).
3. Field definition và value sai type, `null` với required, object/array, date bất hợp lệ, duplicate JSON property: 400, dữ liệu/journal/version không đổi (Tasks 2, 4).
4. Retry PUT tag đã gắn hoặc clear optional value vốn không có: kiểm tra ETag trước, trả kết quả no-op không tạo side effect; stale vẫn 412 (Tasks 3–4).
5. Audit/outbox failure hoặc revoked membership trong request: rollback mọi phần và trả lỗi đóng; không để audit/relations thiếu nhất quán (Tasks 2–4).

## Contract và semantics để triển khai

- Dùng operations `listTags`, `createTag`, `listContactTags`, `addContactTag`, `removeContactTag`, `listCustomFields`, `createCustomField`, `getContactField`, `setContactField` trong OpenAPI hiện có. List trả page `items`/`nextCursor`; limit default 20, 1–100, cursor ≤2048. Cursor được xác thực, ràng buộc tenant, loại danh sách và contact ID khi có; sắp `(id ASC)` theo UUID PostgreSQL; `limit+1`, không hứa snapshot.
- `CreateTag.name` theo contract 1–64 ký tự; normalized key do PostgreSQL `lower(btrim(name))`, collation DB và btrim ordinary spaces. Unique `(tenant_id,normalized_name)` của Plan 2 là source of truth; concurrent duplicate trả 409. Chuẩn hóa để so sánh không thay đổi name hiển thị. Nếu quy tắc từ chối tên chỉ có spaces hoặc bất kỳ giới hạn mới nào thu hẹp request hiện tại, phải review contract và được phê duyệt trước khi áp dụng.
- `CreateField`: key regex `^[a-z][a-z0-9_]{0,63}$`, displayName 1–200, dataType `string|number|boolean|date`, required default false khi bỏ qua; version 1. Key unique theo tenant, definition immutable sau creation. `date` là chuỗi calendar date ISO `YYYY-MM-DD` hợp lệ, không timezone; number là JSON number hữu hạn và bảo toàn giá trị, không ép string/boolean. `SetFieldValue` yêu cầu property `value`, null xóa value optional; null của required trả 400. Object/array bị từ chối. Khi value chưa có, GET trả 404; không suy ra required fields phải có ngay tại create contact vì contract chưa yêu cầu điều đó.
- `PUT` tag đã tồn tại là no-op sau xác minh version; `DELETE` tag chưa gắn chọn no-op cùng cách để retry nhất quán, nhưng contact/tag phải tồn tại cùng tenant. `PUT` cùng giá trị field theo JSON typed equality hoặc clear optional field chưa có cũng no-op sau version check. Mutation thật trả contact mới và strong ETag theo OpenAPI.
- Field values và tag attachments là contact mutation, emit event `crm.contact.updated.v1` hiện có chỉ gồm version, không có PII. Tạo tenant tag/definition ghi audit append-only với actor/correlation và target đúng loại, không gắn contact giả vào `outbox.events` vốn có FK contact. Nếu cần public event cho definition/tag phải định nghĩa contract và migration trước, không phát minh event ngầm.
- Những semantics chưa được mô tả trong contract (date, no-op, vắng value, tag normalization, paging) cần được ghi rõ và test tại Task 1. Nếu phát hiện restriction làm breaking diff, dừng phần phụ thuộc cho tới ADR/migration/consumer review; không tự xem plan là phê duyệt breaking change.

## File map và interfaces

- `docs/contracts/crm-api.openapi.yaml`, `tests/Contract/test_tags_fields.py`: contract cases, fixtures và consumer checks; regenerate `sdks/` bằng script khi cần.
- `src/Platform.Infrastructure/Persistence/Migrations/20261127030000_CustomFields.cs`: `crm.custom_field_defs`/`crm.contact_field_values`, constraints/RLS, audit changes; `DatabaseRoleGrants.cs`: grants và role validation được cập nhật có kiểm soát, không cho direct audit rewrite.
- `src/Platform.Infrastructure/Crm/TagStore.cs`, `CustomFieldStore.cs`: tenant-scoped parameterized SQL, nhận `NpgsqlConnection`, `NpgsqlTransaction`, `Guid tenantId`, cancellation token; không tự commit. `ContactStore` cung cấp conditional version increment cho mutation; `ContactMutationJournal.AppendAsync` dùng event `contact.updated` hiện có.
- `src/Platform.Api/Crm/TagEndpoints.cs`, `CustomFieldEndpoints.cs`: mapping/validation, verified transaction, shared cursor; đăng ký trong `Program.cs`. `TenantAuthorization.cs` bổ sung `FieldsManage`. `TenantEndpointTransaction` tái dùng flow hiện có, giữ revalidation sau body.
- `src/Platform.Api/Crm/CrmCollectionCursor.cs`: HMAC-domain-separated cursor dùng khóa đã cấu hình `ContactCursor:SigningKey`; encode/decode ràng buộc tenant, collection kind và optional contact ID, version và anchor UUID; không dùng lẫn cursor contact list. Key missing/invalid fail closed theo cơ chế Plan 2.
- Tests PostgreSQL thật: `tests/Integration/TagEndpointsTests.cs`, `CustomFieldSchemaTests.cs`, `CustomFieldEndpointsTests.cs`, `TagFieldConcurrencyTests.cs`. Fixtures dùng `PostgresFixture`, `ApiFactory`, `FakeOidcIssuer` hiện có.

## Task 1: Contract và cursor cho collections

**Consumes:** OpenAPI/SDK hiện tại, cấu hình cursor Plan 2. **Produces:** contract/fixtures rõ semantics phía trên; `CrmCollectionCursor.Encode(Guid tenant, string kind, Guid? contactId, Guid anchor)` và `TryDecode(string token, Guid tenant, string kind, Guid? contactId, out Guid anchor)` với kind cố định `tags`, `contact-tags`, `field-definitions`.

**Files:** `docs/contracts/crm-api.openapi.yaml`; `tests/Contract/test_tags_fields.py`; `src/Platform.Api/Crm/CrmCollectionCursor.cs`; `tests/Integration/CrmCollectionCursorTests.cs`.

- [ ] Viết tests contract cho chín operation, scope, request/response, preconditions, field types/null/date và list pagination; test cursor canonical, MAC bị sửa, tenant/kind/contact khác, token của `ContactCursor`, malformed/empty/oversized, missing/incorrect key và key rotation. Xác nhận không chứa tag name, field value, query hay key trong cursor/log.
- [ ] Chạy `python3 -m unittest discover -s tests/Contract -p 'test_tags_fields.py' -v` và `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~CrmCollectionCursorTests'`; ghi RED thực tế. Nếu test contract đã pass, ghi rõ và có test RED cho semantics còn thiếu; không sửa contract chỉ để tạo RED.
- [ ] Hoàn thiện contract theo quy trình compatibility ở trên; implement cursor HMAC-SHA256, `FixedTimeEquals`, domain separation, canonical base64url, không dependency/new secret; không để cursor list contact cũ decode được trong collection khác. Nếu contract đổi, chạy `python3 scripts/contracts/generate.py`, `python3 scripts/contracts/check.py` và consumer test liên quan, giữ nguyên kết quả gate thực.
- [ ] Chạy lại hai lệnh: PASS; commit contract/cursor/tests/generated files liên quan.

## Task 2: Persistence và tenant-level definitions

**Consumes:** Plan 2 `crm.tags`, `crm.contact_tags`, Task 1 cursor. **Produces:** field definitions/values storage và API `GET/POST /api/v1/tags`, `GET/POST /api/v1/custom-fields`; `FieldsManage` chỉ owner/admin.

**Files:** migration `20261127030000_CustomFields.cs`, `DatabaseRoleGrants.cs`, `TenantAuthorization.cs`, `TagStore.cs`, `CustomFieldStore.cs`, `TagEndpoints.cs`, `CustomFieldEndpoints.cs`, `Program.cs`; `CustomFieldSchemaTests.cs`, `TagEndpointsTests.cs`, `CustomFieldEndpointsTests.cs`.

- [ ] Viết DB tests cho upgrade từ Plan 2, unique `(tenant_id,key)`, immutable definition, typed constraints, composite FKs đến contact/definition, RLS ENABLE/FORCE và thiếu tenant, app role non-owner/no BYPASSRLS/no direct audit update/delete. Kiểm thử grants mới và validation không cho assume role có đặc quyền; rollback database rỗng được, có dữ liệu fail closed với hướng dẫn backup/forward-fix.
- [ ] Viết API tests: agent tạo tag, operator read-only; owner/admin tạo field, agent/operator 403. List keyset/tie/tenant isolation và cursor sai loại/tenant; concurrent normalized tag name hoặc field key chỉ một create 201, còn lại 409. Unknown/duplicate JSON keys, invalid type/key/name, unauthenticated/revoked và tenant khác fail closed. Audit tạo tag/definition đúng actor/target/correlation, không tạo contact event; khi audit fail rollback definition/tag.
- [ ] Chạy `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~CustomFieldSchemaTests|FullyQualifiedName~TagEndpointsTests|FullyQualifiedName~CustomFieldEndpointsTests'`; ghi RED.
- [ ] Tạo migration/grants và immutable definitions, audit append an toàn theo pattern security-definer, search_path cố định, giới hạn quyền; thêm `FieldsManage`, routes và SQL list/create. Read body ngoài verified write transaction, verify lại membership/staff trước mutation; map conflict/Problem và commit trước response. Không thêm direct audit write grants; không cấp quyền update/delete definition.
- [ ] Chạy lại lệnh trên: PASS; chạy test `ContactTagFoundationTests|ContactListTests` liên quan thay đổi grants/bảng; phải PASS. Commit task.

## Task 3: Gắn/gỡ và liệt kê contact tags

**Consumes:** Task 2 tags, Plan 1 contact lock/version/journal. **Produces:** `GET /api/v1/contacts/{id}/tags`, `PUT/DELETE /api/v1/contacts/{id}/tags/{tagId}`; store chỉ dùng transaction của caller.

**Files:** `TagStore.cs`, `TagEndpoints.cs`, `ContactStore.cs` nếu cần, `TagEndpointsTests.cs`, `TagFieldConcurrencyTests.cs`.

- [ ] Viết tests pagination và cursor ràng buộc contact/tenant; owner/admin/agent write, operator read; contact deleted/other tenant và tag khác tenant trả 404. PUT mới/DELETE tồn tại tăng version đúng một lần, trả ETag mới và tạo đúng một audit + event `crm.contact.updated.v1`; no-op PUT/DELETE giữ ETag/counts, stale vẫn 412. Test duplicate/invalid If-Match, missing header, DB failure và không leak existence.
- [ ] Test controlled concurrent barrier: PUT cùng version với PUT khác, DELETE tag và PATCH/DELETE contact; đúng một mutation thật thắng và loser 412, không ghi audit/outbox thừa. Test query `listContacts?tagId=` của Plan 2 phản ánh attach/detach trong cùng tenant sau commit.
- [ ] Chạy `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~TagEndpointsTests|FullyQualifiedName~TagFieldConcurrencyTests'`; ghi RED.
- [ ] Implement theo lock contact tenant/id, xác minh active và ETag trước no-op, check tag tenant, mutate quan hệ, conditional version bump và journal trong cùng transaction. Đảm bảo tất cả writers dùng cùng thứ tự khóa; fail/rollback trước response. Cross-tenant không phân biệt missing.
- [ ] Chạy lại test task và `ContactListTests`: PASS; commit task.

## Task 4: Contact custom field values

**Consumes:** Task 2 definitions, Task 3 contact-version mutation convention. **Produces:** `GET/PUT /api/v1/contacts/{id}/fields/{fieldId}` với contract SetFieldValue/Contact và ETag.

**Files:** `CustomFieldStore.cs`, `CustomFieldEndpoints.cs`, `ContactStore.cs` nếu cần; `CustomFieldEndpointsTests.cs`, `TagFieldConcurrencyTests.cs`.

- [ ] Viết tests mọi type: string, decimal/large JSON number, boolean, valid/leap/invalid calendar date; sai type, object/array, duplicate property và malformed JSON trả 400 không ghi. Null xóa optional; required null trả 400; missing value 404; unknown/cross-tenant contact/definition 404. Test required default false, no-op typed equality, same/missing/invalid/stale ETag và response contact/version.
- [ ] Test controlled concurrency tag vs field, field vs PATCH/DELETE và hai field writes cùng ETag: một mutation thật thắng; no-op/stale kiểm version trước; failure injection ở audit/outbox rollback value, contact version và mọi journal. Audit/outbox không chứa field value. Kết quả permissions theo Global Constraints.
- [ ] Chạy `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~CustomFieldEndpointsTests|FullyQualifiedName~TagFieldConcurrencyTests'`; ghi RED.
- [ ] Implement validate theo definition trước write, exact JSON number storage/comparison không chuyển qua double gây mất precision, nullable semantics và cùng transaction/version/journal. Nếu PostgreSQL JSONB numeric không biểu diễn một number mà contract cho phép, trả lỗi đóng có reason token và xử lý qua contract review thay vì làm tròn âm thầm. Không thêm giới hạn không được phê duyệt.
- [ ] Chạy lại lệnh trên và tests Task 3: PASS; commit task.

## Task 5: Review và kết thúc Plan 3

**Files:** `docs/engineering/03-ACCEPTANCE-MATRIX.md`, tài liệu migration/permissions/contract liên quan và `docs/handoff/YYYY-MM-DD-wave-2-handoff.md`.

- [ ] Chạy local chỉ tests liên quan: `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~CrmCollectionCursorTests|FullyQualifiedName~CustomFieldSchemaTests|FullyQualifiedName~TagEndpointsTests|FullyQualifiedName~CustomFieldEndpointsTests|FullyQualifiedName~TagFieldConcurrencyTests|FullyQualifiedName~ContactTagFoundationTests|FullyQualifiedName~ContactListTests'` và `python3 -m unittest discover -s tests/Contract -p 'test_tags_fields.py' -v`. Nếu sửa shared auth/contact/journal, chạy thêm tests Plan 1 tương ứng; ghi SHA, command, môi trường, RED/GREEN, số pass/fail/skip thực tế.
- [ ] Review migrations, rollback/forward-fix, least privilege, tenant/RLS, concurrency, no-op, cursor, audit, outbox và generated consumers. Nếu contract thay đổi, chạy compatibility gate phù hợp với actual base; ghi verdict thực, không dùng scoped approval cũ như pass tự động. Cập nhật US-04 với evidence thật; giữ Wave 2/P0 OPEN và security FAIL/deferred.
- [ ] Cập nhật handoff: mục tiêu, file/commit, thay đổi chưa commit, verification thực tế, blockers, bước Plan 4 (timeline/UI). Chạy `git diff --check` và commit tài liệu. Không chạy full suite local; Wave 2 exit dùng GitHub CI và benchmark theo spec.
