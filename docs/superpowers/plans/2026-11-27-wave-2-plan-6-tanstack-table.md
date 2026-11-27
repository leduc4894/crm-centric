# Wave 2 Plan 6 — TanStack Table Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` task by task. One coding agent at a time per AGENTS.md. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Dùng TanStack Table cho bảng contact hiện có, giữ nguyên giao diện prototype, dữ liệu server và các bảo vệ tenant/ETag của Plans 1–5.

**Architecture:** `ContactList` tiếp tục lấy các trang qua TanStack Query `contactsOptions`; một component headless table nhận các contact đã được server filter/sắp xếp và chỉ render chúng. Backend vẫn sở hữu filter, thứ tự `(updated_at DESC,id DESC)` và cursor; Table không tự sort/filter/paginate tập dữ liệu một phần. Create/reconciliation và record detail vẫn dùng client/scope của Plan 5.

**Tech Stack:** React 19, TypeScript, Vite, TanStack Query đã pin, `@tanstack/react-table` stable, npm lockfile, Vitest/Testing Library và Playwright với stack OIDC/PostgreSQL thật.

**Spec:** [Wave 2 design](../specs/2026-11-27-wave-2-crm-contacts-design.md); [Plan 5 Query](2026-11-27-wave-2-plan-5-tanstack-query.md); [Plan 5 handoff](../../handoff/2026-11-27-wave-2-plan-5-handoff.md); [prototype](../../../prototypes/centric-ui/index.html); [CRM OpenAPI](../../contracts/crm-api.openapi.yaml).

## Global Constraints

- Plan 5 hoàn tất focused review theo handoff, không có nghĩa Wave 2/P0 đã pass. Actual-base compatibility vẫn `CONTRACT_POLICY_REVIEW_REQUIRED`; security FAIL/deferred. Không tự thay baseline hoặc tắt gate. Test Plan 5 đã nêu rõ những trường hợp không có RED độc lập; không chuyển verdict sang candidate Plan 6.
- TDD RED → GREEN → refactor. Chỉ chạy test liên quan local; full suite chạy tại **Plan 9 Wave 2 exit trên GitHub CI**, không chạy full suite local. Test integration/browser cần backend thật khi tuyên bố E2E.
- Chọn một release `@tanstack/react-table` **stable, không prerelease**, tương thích React 19 và Node 24 tại thời điểm thực hiện; đọc docs đúng major được chọn, kiểm tra license và advisory, pin exact trong manifest/lockfile. Không cài Router, Form, Virtual hoặc thay Query trong plan này.
- Không sửa public contract, API, DB, tenant authorization, ETag hoặc logic mutation. Giữ query keys theo identity/tenant/session, hủy request và không render stale response sau logout/tenant switch; không đưa token, contact data hoặc filter vào logs/storage mới.
- Table chỉ render dữ liệu đã load; không bật client-side sort/filter/pagination, không suy tổng số trang hay total count, không giả lập filter/sort chưa được API hỗ trợ. `Tải thêm` dùng `nextCursor`/`fetchNextPage` của Query hiện có, không dùng offset/page index của Table.
- Giữ read-only/403, explicit review khi 412/ambiguous write, duplicate-email candidate và lossless number; không retry mutation hoặc đổi hành vi `ContactForm`. UI theo prototype, semantic table, keyboard, responsive và text escaping.

## Review Focus

1. Hai trang có contact trùng ID hoặc dữ liệu cập nhật: dedup hiện có tiếp tục đúng, row identity dùng UUID ổn định, không lẫn dữ liệu giữa dòng (Tasks 1–2).
2. Lọc đổi trong lúc request trang cũ trả muộn, cursor lặp hoặc trang lỗi: server vẫn là nguồn thật; không append trang sai, giữ lỗi và khả năng retry hiện có (Tasks 1–2).
3. Chuyển tenant/logout rồi quay lại trong lúc fetch: bảng và state không giữ contact cũ hay response muộn, kể cả client bỏ qua abort (Tasks 1–2).
4. Tạo contact trùng email, ETag stale hoặc timeout/reconciliation: choices lấy đúng dataset hiện tại, giữ draft và yêu cầu review, không tự overwrite hay báo success giả (Task 2).
5. Tên contact chứa markup, null email/phone, ID viết hoa, bàn phím và màn hình hẹp: render an toàn, đúng liên kết và dùng được (Tasks 1–3).

## Cấu trúc file và interface

- `apps/crm-web/src/contacts/ContactTable.tsx`: `ContactTable({ items }: { items: Contact[] })`; `Contact` dùng type xuất từ `ContactForm.tsx`. Column IDs `displayName`, `email`, `phone`, header hiện tại `Tên liên hệ`, `Email`, `Điện thoại`; link detail dùng `navigate`, null hiển thị `—`.
- `apps/crm-web/src/contacts/ContactList.tsx`: tiếp tục flatten/dedup pages theo UUID như hiện tại, filter, retry, `fetchNextPage` và `ContactForm` choices; chỉ thay markup bảng bằng `ContactTable`. Không chuyển query/cursor sang Table state.
- `apps/crm-web/src/contacts/ContactTable.test.tsx`, `ContactList.test.tsx`, `ContactForm.test.tsx`, `ContactShell.test.tsx`: unit/integration React test cho table và các luồng cũ.
- `apps/crm-web/src/contacts/shell.css`: chỉ sửa cần thiết để table cuộn ngang trên màn hình hẹp, keyboard/focus và style prototype. `apps/crm-web/tests/e2e/contacts.spec.ts`: xác minh table trong journey thật.
- `apps/crm-web/package.json`, `package-lock.json`: pin dependency; `docs/handoff/YYYY-MM-DD-wave-2-plan-6-handoff.md`: evidence và trạng thái.

## Task 0: Preflight và dependency

**Consumes:** Plan 5 handoff và current Git state. **Produces:** lựa chọn version có thể kiểm chứng cho Task 1; không chạy tests ở bước documentation/preflight.

- [ ] Kiểm tra `git status --short --branch`, `git log -1 --oneline`, đọc Plan 5 handoff; ghi chính xác SHA và pre-existing changes, không coi browser/compatibility cũ là pass của candidate mới.
- [ ] Tra metadata chính thức/npm của `@tanstack/react-table` stable, peer React 19/Node 24, license và advisories; chọn release/major được hỗ trợ và ghi version + docs tương ứng. Không chọn prerelease hoặc `latest` không cố định. Pin version chính xác bằng npm 11.19.1, cập nhật lockfile; chỉ commit dependency cùng Task 1 sau tests. Nếu cần network/tooling hoặc có advisory chưa được đánh giá, ghi blocker, không in token.

## Task 1: ContactTable và kiểm thử render

**Consumes:** `Contact[]` và `navigate` hiện có. **Produces:** `ContactTable({ items }: { items: Contact[] })` trong `ContactTable.tsx`, row ID từ `contact.id`, columns cố định và markup semantic.

**Files:** Create `ContactTable.tsx`, `ContactTable.test.tsx`; modify `ContactList.tsx`, `shell.css`, package/lockfile Task 0.

- [ ] Viết test `renders_server_order_with_stable_contact_ids`: hai rows cố ý có tên theo thứ tự ngược alphabet; kỳ vọng thứ tự server nguyên vẹn sau rerender/reorder, mỗi link trỏ `/app/contacts/{id}`, UUID uppercase hoạt động. Test `renders_nulls_and_markup_safely`: email/phone null là `—`, tên `<script>...` là text, không tạo script; headers/cells có semantic và link keyboard focus.
- [ ] Viết test `uses_table_row_model_without_client_transforms`: kiểm tra component thật qua injected/spied Table hook hoặc seam hẹp, xác nhận thư viện nhận `getRowId` ổn định và chỉ dùng core row model, không kích hoạt sorting/filtering/pagination phía client. Đây là test dependency/integration có ý nghĩa, không chỉ kiểm tra DOM có ba cột.
- [ ] Chạy `npm exec --workspace @crm-centric/crm-web -- vitest run src/contacts/ContactTable.test.tsx`; ghi RED do component chưa có.
- [ ] Implement headless Table theo API stable major đã chọn; render với markup và styles phù hợp prototype, `getRowId` là UUID từ server, không set client row models cho sorting/filtering/pagination. `ContactList` truyền `items` đã dedup; không thay Query, form choices, route, query/tenant state hay ETag.
- [ ] Chạy lại lệnh trên và `npm run typecheck --workspace @crm-centric/crm-web`: PASS. Chạy ESLint chỉ các source/test đổi và `git diff --check`: PASS. Commit dependency, component và focused tests.

## Task 2: Query, tenant và mutation regression

**Consumes:** Task 1 table, Plan 5 `contactsOptions`, `ContactQueryScope`, `scopedRecordClient`. **Produces:** regression evidence giữ nguyên filter, cursor, isolation và create/reconciliation.

**Files:** Modify `ContactList.test.tsx`, `ContactForm.test.tsx`, `ContactShell.test.tsx`, `queryScope.test.tsx` nếu cần; sửa code Task 1 chỉ khi test phát hiện regression.

- [ ] Viết tests `table_preserves_server_pages_and_filter_race` với q literal `%_+`, tagId, updatedSince, hai trang overlap và cursor vòng lặp; assert request/nextCursor từ server, không sort/filter lại phần client, thay filter reset dataset và response cũ không render. Cover fail trang tiếp theo và `Thử lại`, no fabricated total/count.
- [ ] Viết test `table_scope_switch_drops_old_rows` với A→B→A, logout/login cùng identity và delayed response bỏ qua abort; assert không có row tenant cũ, table state không lẫn giữa session và không làm rò ID qua navigation. Dùng test AuthClient/Query scope thực theo Plan 5, không chỉ mock state mới.
- [ ] Viết test `table_keeps_create_candidates_and_conflict_review`: visible choices chỉ từ dataset hiện tại; email trùng cần chọn và review đúng contact ID/ETag, 412 giữ draft, canceled reconciliation không mở khóa hay tự resend. Kiểm tra đổi tenant khi dialog mở xóa state cũ.
- [ ] Chạy `npm exec --workspace @crm-centric/crm-web -- vitest run src/contacts/ContactList.test.tsx src/contacts/ContactForm.test.tsx src/contacts/ContactShell.test.tsx src/contacts/queryScope.test.tsx src/contacts/contactQueries.test.tsx`; ghi kết quả. Tests bổ sung phải chứng minh được assertion chạy trên Table mới; nếu hành vi đã pass trước code thay đổi, ghi baseline PASS, không gọi là RED giả.
- [ ] Nếu phát hiện regression: tạo focused RED, sửa tối thiểu, rerun đến GREEN. Nếu không thay code, chỉ commit các tests mới có giá trị và ghi kết quả thực tế. Không đổi lock/reconciliation của Plan 5 để làm test dễ pass.

## Task 3: Browser thật và accessibility

**Consumes:** Task 1–2. **Produces:** evidence rằng table chuyển đổi không làm hỏng luồng Contact hoặc tenant boundaries.

**Files:** Modify `apps/crm-web/tests/e2e/contacts.spec.ts` chỉ khi cần assertion; optional `ContactTable.test.tsx`/`shell.css` cho accessibility.

- [ ] Bổ sung assertion vào real-provider journey: list nhiều contacts giữ thứ tự server, link detail và back; filter/cursor giữ request server; delayed tenant A→B→A không thấy row A trong B. Kiểm tra table headers/row links bằng keyboard, viewport 390px không mất quyền truy cập cột và text markup hiển thị literal. Không khẳng định load-test/benchmark từ fixture này.
- [ ] Chạy `CRM_REAL_OIDC_TEST=1 npm exec --workspace @crm-centric/crm-web -- playwright test tests/e2e/contacts.spec.ts` theo prerequisites/teardown trong [Plan 4 handoff](../../handoff/2026-11-27-wave-2-plan-4-handoff.md). Ghi số passed/failed/skipped và SHA, xác nhận test chạy thật, không thay bằng mocked API hoặc coi skip là pass. Bổ sung test trước production fix khi sửa lỗi phát hiện; không tuyên bố RED nếu chỉ thêm assertion sau code.
- [ ] Chạy focused unit tests Task 1–2, `npm run typecheck --workspace @crm-centric/crm-web`, targeted ESLint và `npm run build --workspace @crm-centric/crm-web`: ghi kết quả thực. Nếu fixture không hỗ trợ nhiều trang thật, ghi giới hạn và bổ sung test integration tương ứng thay vì fake evidence.
- [ ] Review accessibility/style so với prototype, verify no client-side sort/filter và dependency/license; commit assertion/necessary fixes. Full hosted suite và benchmark không thuộc task này.

## Task 4: Plan 6 handoff

**Files:** `docs/handoff/YYYY-MM-DD-wave-2-plan-6-handoff.md`; tài liệu frontend/acceptance chỉ nếu evidence mới cần cập nhật.

- [ ] Ghi objective, decisions, exact dependency version, SHA, files/uncommitted work, lệnh và kết quả RED/GREEN/PASS/FAIL/SKIP/NOT RUN, environment, browser evidence và review findings. Ghi `CONTRACT_POLICY_REVIEW_REQUIRED` trên actual base theo evidence mới nhất, security FAIL/deferred và các gate Wave 2/P0 còn OPEN; không sửa public contract hoặc nới CI để đánh dấu pass.
- [ ] Ghi Plan 7 Router kế tiếp; Plan 8 Form và Plan 9 Wave 2 exit (full GitHub CI + 50k contacts/tenant, 50 concurrent users, p95 <400ms) vẫn chưa thực thi. Chạy `git diff --check`, commit tài liệu phù hợp; không chạy full suite local.
