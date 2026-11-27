# Wave 2 Plan 2 — Contact Search and Pagination Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. One coding agent at a time per AGENTS.md. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Triển khai `listContacts` với tìm kiếm, lọc và keyset pagination tenant-scoped theo contract hiện tại.

**Architecture:** Endpoint dùng authorization và transaction Plan 1; PostgreSQL thực thi filter và keyset theo `(updated_at DESC, id DESC)`. Cursor được xác thực bằng HMAC với khóa cấu hình dùng chung giữa các instance, ràng buộc tenant, filter và sort; không phụ thuộc anchor row còn tồn tại. Migration tạo nền tags tối thiểu để `tagId` hoạt động thật, còn API và mutation tags thuộc Plan 3.

**Tech Stack:** .NET 10, Npgsql/EF migrations, PostgreSQL/Testcontainers, xUnit, OpenAPI và generated TypeScript SDK hiện có.

**Spec:** [Wave 2 design](../specs/2026-11-27-wave-2-crm-contacts-design.md); [CRM OpenAPI](../../contracts/crm-api.openapi.yaml); [data model](../../architecture/06-DATA-MODEL.md); [Plan 1 handoff](../../handoff/2026-11-27-wave-2-branch-handoff.md).

## Global Constraints

- Plan 1 đã hoàn tất theo handoff; giữ ADR-020/021, unique active email và conditional upsert; không thay đổi semantics CRUD.
- Chỉ chạy test liên quan local theo RED → GREEN → refactor. Full suite chỉ chạy tại Wave 2 exit trên GitHub CI, không chạy full suite local. Không tắt gate bắt buộc.
- Test integration dùng PostgreSQL thật qua Testcontainers và application role; tenant từ membership đã xác thực, không tin request để cấp quyền. Mỗi query có tenant predicate và RLS.
- Giữ OpenAPI contract-first; breaking diff phải có ADR, migration plan và consumer tests. Không tự rebaseline hoặc mở rộng scoped review Plan 1. Verdict Plan 1 `CONTRACT_BREAKING_REVIEWED` là reviewed-breaking, không là compatible hay independent enforcement; policy gắn exact evidence và có expiry, không giả định bao phủ Plan 2.
- Mọi error path đóng có reason token và correlation ID; không log q, cursor, secrets hay PII. Không trả dữ liệu bị soft-delete.
- Chưa benchmark 50k/50 concurrent và chưa claim Wave 2/P0 exit; security vẫn FAIL/deferred.

## Review Focus

1. Cursor bị sửa, dùng tenant khác, filter khác, sort/version khác hoặc quá dài: trả 400 `CURSOR_INVALID`, không query sang tenant khác (Task 2–3).
2. Nhiều contact cùng timestamp hoặc anchor bị xóa/đổi: page tĩnh không lặp/mất; anchor xóa không khiến cursor vô hiệu, ghi rõ không có snapshot khi ghi đồng thời (Task 3).
3. `q` chứa `%`, `_`, backslash, Unicode, hoặc input lặp/không hợp lệ: ký tự được tìm như literal và invalid request trả 400, không thành wildcard/SQL injection (Task 3).
4. `tagId` của tenant khác hoặc contact/tag bị xóa: không lộ dữ liệu, không giả vờ filter; cùng UUID ở tenant khác không tạo join chéo (Task 1, 3).
5. Khóa cursor thiếu/sai hoặc app instances dùng key khác: fail closed, không in key; mô tả vận hành, rotation và kiểm thử cross-instance (Task 2).

## Contract và quyết định truy vấn

- `GET /api/v1/contacts` giữ các tham số `limit` mặc định 20, min 1/max 100; `cursor` ≤2048 ký tự; `q` ≤200 Unicode scalar values; `tagId` UUID; `updatedSince` RFC 3339 timestamp. Response `ContactPage` gồm `items` và `nextCursor` nullable. Số thứ tự `updated_at DESC, id DESC` dùng đúng index Plan 1; `updatedSince` bao gồm biên (`updated_at >=`).
- `q` tìm substring không phân biệt hoa/thường theo PostgreSQL collation hiện hành trên `display_name` hoặc `email`, dùng `ILIKE ... ESCAPE` với `%`, `_`, `\\` được escape literal; null email không match email. `q` rỗng bị từ chối 400 `REQUEST_INVALID`; không trim hay tự thay đổi nội dung. Khác collation có thể khác Unicode case folding; ghi rõ trong mô tả contract, không hứa accent-insensitive.
- Query parameters chỉ xuất hiện một lần; từ chối parameter không biết, limit không canonical integer 1–100, UUID không canonical lowercase `D`, timestamp không có timezone/không parse được và q có NUL/invalid Unicode. `updatedSince` chuẩn hóa UTC; với precision vượt microsecond của PostgreSQL, so sánh theo phép làm tròn lên microsecond để giữ đúng ngữ nghĩa `updated_at >=` mà không từ chối timestamp hợp lệ. Tất cả filter kết hợp bằng AND.
- Khi `tagId` không tồn tại hoặc thuộc tenant khác, kết quả 200 rỗng; không phân biệt hai trường hợp. List không tạo audit/outbox mutation. Cursor opaque về mặt contract, không là token xác thực; API vẫn kiểm tra quyền mỗi request. Dữ liệu filter không được đưa plaintext vào cursor hoặc log.
- Nếu các quyết định trên cần sửa OpenAPI, review contract diff trước code, chạy generated checks/consumer test liên quan. Không thay đổi behavior đang có của POST/PATCH/DELETE.

## File map và interfaces

- `src/Platform.Infrastructure/Persistence/Migrations/20261127020000_ContactTagsFoundation.cs` và `DatabaseRoleGrants.cs`: bảng `crm.tags`, `crm.contact_tags`, index, RLS và grant read-only; Plan 3 mở rộng mutation.
- `src/Platform.Api/Crm/ContactListQuery.cs`: `TryParse(IQueryCollection query, out ContactListQuery? value)` với record gồm `int Limit`, `string? Q`, `Guid? TagId`, `DateTimeOffset? UpdatedSince`, `string? Cursor`.
- `src/Platform.Api/Crm/ContactCursor.cs`: `Encode(Guid tenant, ContactListQuery query, DateTime updatedAt, Guid id)` và `TryDecode(string cursor, Guid tenant, ContactListQuery query, out DateTime updatedAt, out Guid id)` trên instance nhận HMAC key từ cấu hình đã validate. Không lưu raw `q` trong token; dùng fingerprint có khóa của tenant/filter/sort, constant-time MAC compare.
- `src/Platform.Infrastructure/Crm/ContactStore.cs`: `ListAsync(NpgsqlConnection, NpgsqlTransaction, Guid tenantId, string? q, Guid? tagId, DateTimeOffset? updatedSince, DateTime? anchorTime, Guid? anchorId, int limit, CancellationToken)` trả tối đa `limit+1` `ContactRecord` theo thứ tự trên.
- `src/Platform.Api/Crm/ContactEndpoints.cs`, `Program.cs`/configuration: parse, permission `ContactsRead`, gọi store, tạo cursor, trả `ContactPage`; không đọc list vào bộ nhớ rồi mới lọc.
- Tests mới: `tests/Integration/ContactTagFoundationTests.cs`, `ContactCursorTests.cs`, `ContactListTests.cs`, `tests/Contract/test_contact_list.py`; tái sử dụng `PostgresFixture`, `ApiFactory`, `FakeOidcIssuer`.

## Task 1: Nền lưu tags cho filter

**Files:** Create migration và `tests/Integration/ContactTagFoundationTests.cs`; modify `DatabaseRoleGrants.cs` và migration snapshot nếu tooling yêu cầu.

**Interfaces:** `crm.tags (tenant_id,id,name,normalized_name)` có unique `(tenant_id,id)` và `(tenant_id,normalized_name)`; `crm.contact_tags (tenant_id,contact_id,tag_id)` unique tuple, FK cùng tenant tới contacts/tags. Giữ tên/cấu trúc này cho Plan 3.

- [ ] Viết PostgreSQL tests: upgrade từ Plan 1, unique/tenant-aware FK, trùng normalized name bị từ chối trong tenant nhưng cho phép tenant khác, RLS ENABLE/FORCE, thiếu context không thấy rows, application role chỉ SELECT và không owner/BYPASSRLS. Seed qua migration role hai tenant có cùng tag UUID để chứng minh join ràng buộc tenant; app role không insert/update/delete tags. Migration rollback DB rỗng được; rollback khi có dữ liệu fail closed và nêu backup/forward-fix.
- [ ] Chạy `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~ContactTagFoundationTests'`; ghi RED do bảng chưa có.
- [ ] Tạo schema/index/FK/policies và SELECT grants tối thiểu, mở rộng role validation theo cấu trúc hiện tại. Không tạo endpoint hoặc quyền ghi tags ở task này.
- [ ] Chạy lại lệnh trên: PASS; ghi kết quả thực và commit migration/tests.

## Task 2: Query validation và cursor xác thực

**Files:** Create `ContactListQuery.cs`, `ContactCursor.cs`, `tests/Integration/ContactCursorTests.cs`; modify cấu hình API/test fixture và tài liệu vận hành khóa.

**Interfaces:** `ContactListQuery` và `ContactCursor` theo file map. Cursor v1 gồm version, timestamp UTC PostgreSQL precision, UUID anchor và fingerprint keyed của tenant+canonical filter+sort; MAC HMAC-SHA256 trên toàn bộ cấu trúc. Encode base64url canonical, giới hạn 2048. Key cấu hình `ContactCursor:SigningKey` là base64 của ít nhất 32 random bytes, lấy từ secret store/environment; không commit, in hoặc log. `limit` không thuộc filter binding, cho phép client đổi page size.

- [ ] Viết tests parse default/bounds, query lặp/unknown, invalid UUID/timestamp, chuỗi literal đặc biệt và Unicode; không phát sinh request values trong lỗi/log. Test cursor round-trip, different tenant/filter, sửa từng vùng payload/MAC, invalid base64/version/precision, length >2048, sử dụng API so sánh MAC constant-time và kiểm thử key sai; `q` không xuất hiện trong token decode. Hai instance cùng key decode được; khác key bị từ chối.
- [ ] Chạy `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~ContactCursorTests'`; ghi RED.
- [ ] Implement parse và cursor; inject key thông qua cấu hình, validate lúc khởi động hoặc trước list và trả lỗi đóng, không fallback hardcoded. Phân biệt filter absent và q rỗng bị từ chối; canonical hóa timestamp và UUID trước fingerprint. Ghi runbook cấp key qua secret store, đồng bộ key đa instance, rotation làm cursor cũ invalid và client phải restart từ trang đầu; không log key hoặc ví dụ secret thật. Cập nhật test configuration/local stack để chạy được mà không đưa key thật vào repo.
- [ ] Chạy lại lệnh trên: PASS; commit code/tests/runbook.

## Task 3: List endpoint, filtering và paging

**Files:** Modify `ContactEndpoints.cs`, `ContactStore.cs`, `docs/contracts/crm-api.openapi.yaml` khi cần; create `tests/Integration/ContactListTests.cs`, `tests/Contract/test_contact_list.py`.

**Interfaces:** Dùng Task 1 tags và Task 2 parser/cursor; SQL `WHERE tenant_id=@tenant AND deleted_at IS NULL`, optional filters AND, keyset `(updated_at,id) < (@anchorTime,@anchorId)`, `ORDER BY updated_at DESC,id DESC LIMIT limit+1`; tag EXISTS phải join bằng tenant/contact/tag và parameterize tất cả giá trị. Trả tối đa limit và cursor từ item cuối nếu có item tiếp theo.

- [ ] Viết contract tests cho params, `ContactPage`, response Problem và mô tả literal search/order/updatedSince; SDK consumer compile gọi `listContacts` với params hiện tại. Nếu chỉ bổ sung mô tả, xác nhận generated artifacts thay đổi hay không; nếu schema thay đổi chạy contract compatibility process thực với actual base.
- [ ] Viết integration tests: 0, 1, 20, 21, 100, 101 rows; timestamp bằng nhau có UUID tie-break; duyệt dataset tĩnh không trùng/mất; 200 rỗng và nextCursor null ở trang cuối. Test anchor bị xóa sau trang đầu và cursor vẫn tiếp tục theo tuple; test cập nhật sau trang đầu và xác nhận không hứa snapshot, tenant isolation vẫn giữ. Kiểm tra `q` literal `%_\\` và SQL-like text, q Unicode, null email, updatedSince inclusive, AND với tagId; tag cùng UUID tenant khác không tạo match. Test cross-tenant cursor/filter, tamper, malformed, duplicate và limit bounds đều đúng Problem/correlation.
- [ ] Kiểm thử owner/admin/agent/operator có `ContactsRead`, principal thiếu quyền/tenant không active bị từ chối; soft-deleted không xuất hiện, unavailable DB trả 503 `CRM_STORE_UNAVAILABLE`. Xác nhận response chỉ các field contract và list không thêm audit/outbox. Chạy `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~ContactListTests'` và `python3 -m unittest discover -s tests/Contract -p 'test_contact_list.py' -v`; ghi RED.
- [ ] Implement endpoint/store và mapping; giữ transaction verified/role check, commit trước success. Dùng `ILIKE` escape chính xác, không dynamic SQL từ request. Đọc `limit+1` duy nhất, không N+1. Chạy targeted contract tests và cùng lệnh integration: PASS. Nếu contract đổi, chạy `python3 scripts/contracts/generate.py`, `python3 scripts/contracts/check.py` và consumer test liên quan; ghi diff/gate thực tế.
- [ ] Commit implementation, tests và contract/generated artifacts liên quan.

## Task 4: Kiểm tra chỉ mục và kết thúc plan

**Files:** Update `docs/engineering/03-ACCEPTANCE-MATRIX.md`, tài liệu truy vấn/migration/key operation và `docs/handoff/YYYY-MM-DD-wave-2-handoff.md`.

- [ ] Với fixture PostgreSQL đại diện, dùng `EXPLAIN (ANALYZE, BUFFERS)` cho list tenant + keyset và tag EXISTS; xác minh index `(tenant_id,updated_at DESC,id DESC)` hiện có và index contact_tags phù hợp. Ghi query plan, cardinality, PostgreSQL version và nhận xét; không khẳng định p95 hoặc thay benchmark exit. Chỉ thêm index khi evidence cho thấy cần, migration/additive và kiểm thử rollback tương ứng.
- [ ] Chạy targeted local: `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~ContactTagFoundationTests|FullyQualifiedName~ContactCursorTests|FullyQualifiedName~ContactListTests'` và `python3 -m unittest discover -s tests/Contract -p 'test_contact_list.py' -v`; bổ sung test Plan 1 liên quan nếu có sửa code chung. Ghi command, SHA, pass/fail/skip và môi trường thật; không chạy full suite local.
- [ ] Review threat model cursor, key provisioning/rotation, contract diff, role grants, tenant/filter isolation và keyset với writes. Ghi US-03/performance còn OPEN cho tới exit; Plan 3 phải dùng schema tags và thêm mutation authorization/audit/concurrency. Security FAIL/deferred và P0 OPEN; scoped reviewed-breaking của Plan 1 không tự bao phủ candidate mới.
- [ ] Cập nhật handoff với kết quả thực, SHA, uncommitted work, blockers và bước Plan 3; `git diff --check` rồi commit tài liệu. Full suite trên GitHub CI và benchmark 50k/50 users/p95 <400ms tại Wave 2 exit.
