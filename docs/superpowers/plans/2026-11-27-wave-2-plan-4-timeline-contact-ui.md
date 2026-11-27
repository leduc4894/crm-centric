# Wave 2 Plan 4 — Contact Timeline and UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` task by task. One coding agent at a time per AGENTS.md. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Cho phép nhân viên quản lý contacts, tags, custom fields và ghi chú qua giao diện thật, đồng thời đọc timeline tenant-scoped có provenance.

**Architecture:** Thêm timeline/note storage và API dựa trên verified tenant transactions, contact version và audit/outbox atomic hiện có; chưa triển khai external ingestion. React/Vite dùng authenticated tenant context và generated CRM SDK, mô phỏng thiết kế từ prototype, cô lập state theo identity/tenant và xử lý stale responses cũng như ETag conflict.

**Tech Stack:** .NET 10, PostgreSQL/Testcontainers, xUnit, OpenAPI/TS SDK, React 19/Vite, Vitest, Playwright, OIDC/Keycloak fixture hiện có.

**Spec:** [Wave 2 design](../specs/2026-11-27-wave-2-crm-contacts-design.md); [CRM OpenAPI](../../contracts/crm-api.openapi.yaml); [data model](../../architecture/06-DATA-MODEL.md); [state/concurrency](../../architecture/07-P0-ACCESS-AND-STATE.md); [prototype](../../../prototypes/centric-ui/index.html); [Plan 3 handoff](../../handoff/2026-11-27-wave-2-branch-handoff.md).

## Global Constraints

- Kế thừa Plan 1–3: email active unique và POST upsert theo ADR-020/021; không lặng lẽ overwrite contact theo email. Giá trị custom number được lưu chính xác, kể cả ngoài phạm vi JavaScript `Number`.
- TDD RED → GREEN → refactor. Chỉ chạy local các test liên quan; full suite chỉ tại Wave 2 exit trên GitHub CI, không chạy full suite local và không tắt gate bắt buộc. PostgreSQL integration dùng Testcontainers thật.
- Tenant từ authenticated issuer/sub, verified staff/membership; UI không cấp quyền, API/RLS/SQL vẫn enforce. Owner/admin/agent read/write contact, operator read-only; chỉ owner/admin quản lý definition. Không đưa token vào URL, localStorage, log hoặc fixtures có secret thật.
- Mọi mutation note cần strong If-Match và commit note, contact version, audit, outbox trong cùng transaction; missing 428, malformed/weak/wildcard/multiple 400, stale 412. Failed/canceled/ambiguous response không được khẳng định mutation thất bại hoặc tự retry ghi.
- Contract-first; breaking diff cần ADR, migration plan và consumer tests. Plan 3 candidate-owned reviewed-breaking verdict không tự bao phủ candidate mới; không rebaseline hoặc bypass compatibility/security gates.
- Timeline chỉ lưu/đọc phần CRM và notes. External inbox, mapping, dedup/replay thực thuộc Wave 3; không claim RT-EVT-01, remote E2E hay P0 đã pass. Security FAIL/deferred; benchmark và full CI tại Wave 2 exit.

## Review Focus

1. Chuyển tenant, logout hoặc đổi principal giữa request: hủy request, xóa state cũ và không render response cũ kể cả khi server trả muộn (Tasks 3–5).
2. Hai client sửa cùng contact hoặc ghi note cùng ETag: đúng một mutation thắng, 412 không mất nội dung người dùng nhập và không tự overwrite (Tasks 2, 4–5).
3. Note/timeline có HTML/script, Unicode, ký tự điều khiển hoặc dữ liệu sai tenant: escape khi render, bảo toàn nội dung được contract cho phép, không lộ cross-tenant (Tasks 1–2, 5).
4. Field number lớn/chính xác cao: UI không làm tròn, không gửi giá trị khác do `Number`/`JSON.parse`; round-trip giá trị chính xác hoặc khóa thao tác an toàn khi không bảo đảm được (Tasks 3, 5).
5. Pagination cùng timestamp, anchor bị xóa hoặc cursor bị đổi tenant/contact: thứ tự deterministic, tiếp tục theo tuple, sai ngữ cảnh 400 và không hứa snapshot (Tasks 1–2).

## Phạm vi sản phẩm và quyết định contract

- Dùng các operation hiện có `listTimeline` và `createContactNote`, `TimelineEntryPage`, `TimelineEntry`, `CreateNote`; text dài 1–1000 Unicode scalar values theo contract, không tự thu hẹp kể cả ký tự U+0000. List sort `(occurred_at DESC,id DESC)`, limit mặc định 20, 1–100, cursor tenant/contact/sort-bound; empty hợp lệ. Note có `source: "crm"`, `type: "crm.note.created.v1"`, `inboxId: null`, `summary` là note text. Không lưu payload external tùy tiện.
- `POST .../notes` trả 201 `TimelineEntry` và bổ sung strong ETag header của contact sau mutation vào 201 OpenAPI, theo phê duyệt của người dùng ngày 2026-11-27. Đây là additive contract change cần chạy compatibility và consumer tests; nếu chưa đủ bằng chứng chấp nhận, UI phải GET contact mới trước mutation tiếp theo và không giả định ETag từ 201. Không thay response body hiện có.
- Note tạo một timeline entry và tăng version contact; audit/outbox dùng `contact.updated` hiện có, không đưa note text vào audit/outbox. `source/type/time` lấy từ server, không tin body. Network timeout sau gửi là trạng thái chưa xác định: làm mới contact/timeline để người dùng đối chiếu; không tự động gửi lại note.
- Timeline schema có `tenant_id`, `id`, `contact_id`, nullable `inbox_id`, `entry_kind`, `source`, `type`, `occurred_at`, `summary` và provenance cần thiết; composite FK contact, partial unique `(tenant_id,contact_id,inbox_id,entry_kind)` khi inbox ID khác null, không tạo FK xuyên integration context. `FORCE RLS`, indexes theo tenant/contact/time/id. Wave 3 sở hữu việc materialize event/inbox; Plan 4 kiểm thử constraint bằng fixture, không coi đó là test pipeline end-to-end.
- UI dùng prototype cho sidebar, topbar, màu sắc, spacing, bảng, tag chips, form; không sao chép số liệu mẫu, trạng thái, tabs hoặc hành động không có backend thành dữ liệu thật. Luồng gồm list search/filter tag/updatedSince, cursor next, detail, create/edit/soft-delete, tag attach/detach, field values/definitions theo quyền, timeline/notes. Sử dụng dữ liệu API và SDK thật, responsive/keyboard/accessible labels, loading/empty/error và retry an toàn.
- API trả giá trị JSON number rất lớn dưới dạng JSON number; generated TypeScript types dùng `number`, không thể một mình bảo đảm chính xác. Client phải đọc raw response và encode numeric token bằng bộ xử lý lossless được kiểm chứng, không chuyển qua `Number` trước hiển thị/ghi. Nếu cần dependency, chọn phiên bản stable được pin trong lockfile, review dependency và test; nếu chưa có giải pháp được xác nhận, field number hiển thị read-only với lời giải thích, không gửi giá trị làm tròn. Không thay contract để che sự mất chính xác.

## File map và interfaces

- `docs/contracts/crm-api.openapi.yaml`, `tests/Contract/test_contact_timeline.py`, generated `sdks/`: timeline/note contract, optional additive ETag và consumer check.
- `src/Platform.Infrastructure/Persistence/Migrations/20261127040000_ContactTimeline.cs`, `DatabaseRoleGrants.cs`: table, constraints, RLS và least privilege. `src/Platform.Infrastructure/Crm/TimelineStore.cs`: SQL tenant-safe; không commit.
- `src/Platform.Api/Crm/TimelineCursor.cs`: HMAC domain-separated cursor dùng signing key đã có, ràng buộc tenant/contact/sort và `(UTC occurred_at,id)`. `TimelineEndpoints.cs`, `Program.cs`: GET timeline và POST note, transaction và Problem response.
- `tests/Integration/TimelineSchemaTests.cs`, `TimelineEndpointsTests.cs`, `TimelineConcurrencyTests.cs`: schema, permissions, cursor, rollback và real PostgreSQL races.
- `apps/crm-web/src/contacts/crmClient.ts`: wrapper quanh `@crm-centric/crm-client` generated SDK, timeout/abort, error và ETag; separate lossless number parsing/encoding module. Dùng relative API base, `credentials: omit`, `redirect: error`, access token in-memory; xem tên package thực tế từ `sdks/typescript/crm/package.json` trước wiring.
- `apps/crm-web/src/contacts/ContactList.tsx`, `ContactDetail.tsx`, `ContactForm.tsx`, `ContactAttributes.tsx`, `ContactTimeline.tsx`, styles và tests cùng thư mục. `App.tsx` đặt `TenantProvider` bao shell và contacts để `useTenant` hoạt động; routing theo `/app/contacts` và `/app/contacts/:contactId` với browser history, không thêm router dependency nếu chưa cần.
- `apps/crm-web/config/devServer.ts` và test tương ứng: proxy các API routes cần thiết tới backend theo pattern hiện có; không proxy arbitrary URLs. `apps/crm-web/tests/e2e/contacts.spec.ts`: browser flow có backend thật, PostgreSQL và OIDC test fixture.

## Task 1: Timeline contract, schema và cursor

**Consumes:** Plan 3 candidate và CRM OpenAPI. **Produces:** `TimelineCursor.Encode(Guid tenant, Guid contactId, DateTime occurredAt, Guid id)` / `TryDecode(string token, Guid tenant, Guid contactId, out DateTime occurredAt, out Guid id)` với HMAC domain riêng; table tenant-bound sẵn sàng cho Task 2.

**Files:** OpenAPI/SDK nếu đổi, `tests/Contract/test_contact_timeline.py`, migration timeline, `DatabaseRoleGrants.cs`, `TimelineCursor.cs`, `tests/Integration/TimelineSchemaTests.cs`.

- [ ] Viết tests contract cho list/note, type/source/response, note If-Match và ETag 201 đã được người dùng phê duyệt; kiểm tra contract consumer cũ vẫn đọc body. Viết schema tests: tenant composite FK, RLS FORCE/role non-owner/no bypass, no context và tenant khác không đọc/ghi, partial unique inbox có collision và không hạn chế các note null inbox, index order, upgrade từ Plan 3 và guarded downgrade khi có dữ liệu. Tests cursor MAC/empty/malformed, cross-tenant/contact/kind, timestamp/UUID tie và key thiếu/sai; cursor không chứa note text.
- [ ] Chạy `python3 -m unittest discover -s tests/Contract -p 'test_contact_timeline.py' -v` và `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~TimelineSchemaTests'`; ghi RED thực tế.
- [ ] Implement migration/constraints/RLS, bảo toàn note text theo contract kể cả U+0000 (PostgreSQL text không chứa NUL; dùng biểu diễn lossless đã kiểm chứng, không âm thầm từ chối/cắt). Thêm cursor và read/append privilege tối thiểu; app role không có quyền trực tiếp bỏ qua contact version/audit/outbox. Cập nhật OpenAPI với ETag đã duyệt, chạy compatibility/consumer checks và sinh SDK bằng script; ghi migration/forward-fix và key rotation.
- [ ] Chạy lại tests: PASS; nếu contract đổi chạy `python3 scripts/contracts/generate.py`, `python3 scripts/contracts/check.py` và consumer liên quan, ghi compatibility thực. Commit task.

## Task 2: Timeline đọc và tạo note atomic

**Consumes:** Task 1 table/cursor, Plan 3 DB-owned atomic mutation/journal. **Produces:** `MapTimelineEndpoints(IEndpointRouteBuilder)` cho `GET /api/v1/contacts/{id}/timeline` và `POST /api/v1/contacts/{id}/notes`; `TimelineStore` chỉ chạy trong tenant transaction của caller.

**Files:** `TimelineStore.cs`, `TimelineEndpoints.cs`, `Program.cs`, migration/function bổ sung nếu cần, `TimelineEndpointsTests.cs`, `TimelineConcurrencyTests.cs`.

- [ ] Viết API tests: `ContactsRead` với owner/admin/agent/operator, write chỉ owner/admin/agent; anonymous/revoked/invalid tenant và same UUID khác tenant; active vs deleted contact. List 0/1/20/21 rows, equal timestamp UUID tie, anchor bị xóa, cursor sai tenant/contact/MAC, limit bounds, no snapshot khi concurrent write. Text note Unicode, markup và U+0000 round-trip theo schema; invalid/unknown/duplicate JSON 400.
- [ ] Test missing/invalid/stale If-Match, 201 với timeline entry và ETag mới, journal một `contact.updated` không có note text. Barrier test note vs note, note vs PATCH/tag/field/delete cùng version: một winner, loser 412, không thêm entry/journal. Inject lỗi timeline/audit/outbox và failed commit: không persist một phần, không gửi 201. Direct app-role function invocation không được bỏ qua version/journal; revoked membership không race qua check.
- [ ] Chạy `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~TimelineEndpointsTests|FullyQualifiedName~TimelineConcurrencyTests'`; ghi RED.
- [ ] Implement safe body read ngoài tenant write lock, revalidate quyền trong transaction, atomic note/version/journal qua DB-owned operation có scope hẹp và fixed search_path. Commit trước success; ambiguous commit trả lỗi đóng, không tự lặp mutation. Dùng limit+1 và cursor theo tuple, tenant predicate và RLS; trả 404 cho missing/deleted/foreign contact.
- [ ] Chạy lại tests: PASS; chạy thêm `TagFieldConcurrencyTests|ContactEndpointsTests` nếu thay shared mutation/auth. Commit task.

## Task 3: CRM client, tenant isolation và shell

**Consumes:** generated SDK operations của Plans 1–3 và Task 2, `AuthProvider`, `TenantProvider`; prototype. **Produces:** `crmClient` typed operations cho list/get/create/patch/delete, tags/fields, timeline/note kèm tenant, access token, abort signal và ETag; shell/routing hoạt động.

**Files:** `apps/crm-web/package.json` và lockfile nếu thêm workspace dependency, `src/contacts/crmClient.ts`, `src/contacts/losslessJson.ts`, unit tests, `src/App.tsx`, styles, `config/devServer.ts` và tests.

- [ ] Viết Vitest tests cho request wire shape/SDK path, bearer và tenant headers, omit credentials, no redirect, AbortSignal/timeout, 401 invalidate, 403/404/412/503 và malformed response, correlation/reason an toàn; không log token/PII. Verify number tokens `1e131072`, `1e-16384`, số 20+ chữ số, decimal và equivalent forms round-trip mà không mất chữ số; không dùng JS `Number` hoặc tự làm tròn.
- [ ] Viết tests shell deep-link/back/forward, no selected tenant không fetch, logout và tenant A→B reset state ngay, response cũ bị bỏ dù hoàn tất sau switch; auth/health/tenant picker hiện tại vẫn hoạt động. Test proxy chỉ routes API được allowlist, không thay OIDC/logout.
- [ ] Chạy `npm exec --workspace @crm-centric/crm-web -- vitest run src/contacts/crmClient.test.ts src/contacts/ContactShell.test.tsx config/contactsProxy.test.ts`; ghi RED. Dùng pinned Node/npm trong repo.
- [ ] Implement wrapper dùng generated SDK cho các operation có thể xử lý an toàn, lossless raw handling cho numeric fields; invalidate request theo identity+tenant+generation, không để response cũ thay state. Đưa prototype vào shell, routes list/detail và responsive styles; giữ tenant picker/auth/health hiện có. Pin mọi dependency mới trong lockfile, không hardcode secrets hoặc URLs.
- [ ] Chạy lại focused Vitest và `npm run typecheck --workspace @crm-centric/crm-web`: PASS; commit task.

## Task 4: Contact list và CRUD UI

**Consumes:** Task 3 client/shell, Plan 1/2 API. **Produces:** list với search/tag/updatedSince/keyset; create/edit/delete và detail navigation, bảo toàn upsert ETag/ID.

**Files:** `ContactList.tsx`, `ContactForm.tsx`, `ContactDetail.tsx`, CSS và `ContactList.test.tsx`, `ContactForm.test.tsx`.

- [ ] Viết tests loading/empty/error/retry, literal search và filter, cursor reset khi filter đổi, stale list responses, duplicate nextCursor, next page và browser navigation. Không render prototype stats/sample data như thật. Test create mới 201 và email đã có 428: chỉ cho update sau khi người dùng chủ động chọn đúng contact, gửi `If-Match` + `X-Contact-Id`; email reuse/stale 412 giữ form, tải lại và không overwrite. Không tự tìm hoặc merge contact theo email.
- [ ] Test GET/ETag→PATCH, soft-delete xác nhận, operator 403/read-only, timeout sau mutation hiển thị kết quả chưa xác định và refresh có chủ đích, 412 giữ input và cho xem dữ liệu mới. Chuyển tenant/logout trong khi form mở xóa dữ liệu cũ, không hiển thị lỗi chứa PII. Focus, labels và keyboard cho form/dialog.
- [ ] Chạy `npm exec --workspace @crm-centric/crm-web -- vitest run src/contacts/ContactList.test.tsx src/contacts/ContactForm.test.tsx`; ghi RED.
- [ ] Implement list/detail/form theo prototype và Task 3, không tự retry writes; xử lý permission theo server và disable/hide khi biết quyền, 403 vẫn xử lý. Dùng API thật cho count/status; không hiển thị thống kê không được API hỗ trợ.
- [ ] Chạy lại tests: PASS; commit task.

## Task 5: Tags, fields, timeline và notes UI

**Consumes:** Tasks 2–4, Plan 3 API. **Produces:** detail hiển thị và sửa tags/fields, quản lý definitions theo quyền, timeline và note.

**Files:** `ContactAttributes.tsx`, `ContactTimeline.tsx`, `CustomFieldSettings.tsx`, tests `ContactAttributes.test.tsx`, `ContactTimeline.test.tsx`, `CustomFieldSettings.test.tsx`.

- [ ] Viết tests tag create/list/attach/remove, no-op/stale ETag, empty/foreign and permission errors; field definition create/list cho owner/admin, agent/operator bị server từ chối. Field string/boolean/date/number, optional null, required rejection và số chính xác cực lớn không bị làm tròn. Không hiển thị giá trị tenant cũ khi đổi tenant.
- [ ] Test timeline order/next/empty/error, provenance và escape HTML/script, note form 1–1000 Unicode, ETag 201 hoặc GET refresh trước write tiếp theo; 412 giữ note draft, timeout hiển thị trạng thái chưa xác định và không tự gửi lại. Trường hợp bị xóa/403, retry, keyboard/focus; response trễ sau contact/tenant switch bị bỏ.
- [ ] Chạy `npm exec --workspace @crm-centric/crm-web -- vitest run src/contacts/ContactAttributes.test.tsx src/contacts/ContactTimeline.test.tsx src/contacts/CustomFieldSettings.test.tsx`; ghi RED.
- [ ] Implement components với ETag mới nhất, refresh sau từng mutation, dùng typed client/lossless handling và text rendering an toàn. Không render HTML từ note hoặc external source, không đưa text vào logs. Tuân theo prototype, hỗ trợ mobile/keyboard.
- [ ] Chạy lại focused tests: PASS; commit task.

## Task 6: Browser flow và Plan 4 handoff

**Files:** `apps/crm-web/tests/e2e/contacts.spec.ts`, test fixture liên quan, `docs/engineering/03-ACCEPTANCE-MATRIX.md`, `docs/handoff/YYYY-MM-DD-wave-2-handoff.md`.

- [ ] Viết Playwright focused flow với API thật, PostgreSQL và issuer OIDC fixture: login/tenant A, create contact, search, detail, gắn tag, tạo/đặt custom field, thêm note và đọc timeline; reload chứng minh persistence. Hai sessions cùng contact chứng minh 412 và draft được giữ; switch tenant B không thấy A kể cả delayed responses. Kiểm tra XSS text và responsive keyboard. Fixture synthetic, không commit secrets.
- [ ] Chạy test mới RED trước hoàn thiện fixture hoặc UI, rồi GREEN bằng `npm exec --workspace @crm-centric/crm-web -- playwright test tests/e2e/contacts.spec.ts`; document lệnh khởi động/teardown test stack thực tế và mọi prerequisite, không dùng mocked API để claim E2E. Test opt-in/skip hoặc stack không sẵn có phải ghi NOT RUN/skip; không tuyên bố pass.
- [ ] Chạy local tập trung: `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~TimelineSchemaTests|FullyQualifiedName~TimelineEndpointsTests|FullyQualifiedName~TimelineConcurrencyTests'`, `python3 -m unittest discover -s tests/Contract -p 'test_contact_timeline.py' -v`, Vitest từng file Task 3–5, frontend typecheck và Playwright focused ở trên. Nếu shared code/contract đổi, chạy regression/consumer liên quan. Ghi SHA, môi trường, RED/GREEN, số pass/fail/skip và artifact thực tế; không chạy full suite local.
- [ ] Review prototype fidelity, responsive/accessibility, tenant state, secrets/logging, exact numeric round-trip, migration/rollback, contract diff và actual-base compatibility theo governance thật. Update matrix chỉ cho phần đã chứng minh; RT-EVT-01 external pipeline, Wave 2/P0 vẫn OPEN, security FAIL/deferred. Ghi handoff, blockers, file/commit/uncommitted và Plan 5 (full GitHub CI + benchmark); `git diff --check` và commit tài liệu.
