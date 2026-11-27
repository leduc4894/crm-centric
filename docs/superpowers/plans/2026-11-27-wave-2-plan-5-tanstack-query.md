# Wave 2 Plan 5 — TanStack Query Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` task by task. One coding agent at a time per AGENTS.md. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Đưa TanStack Query vào frontend Contact để quản lý dữ liệu server và làm mới có kiểm soát mà vẫn giữ tenant isolation, ETag, draft và reconciliation của Plan 4.

**Architecture:** Một `QueryClient` riêng cho mỗi phiên identity + tenant + generation, chỉ gắn trong Contact scope. Queries dùng `CrmClient`/generated SDK và AbortSignal hiện có; mutations vẫn đi qua cơ chế khóa và đối chiếu contact/attribute của `ContactDetail`, không để cache hoặc background refetch tự chấp nhận một ETag mới khi UI đang sửa hoặc cần review. Không đổi backend, contract, routing, table/form library hay source-of-truth.

**Tech Stack:** React 19, TypeScript, Vite, `@tanstack/react-query` stable v5, npm lockfile; Vitest/Testing Library, Playwright real OIDC/PostgreSQL fixture.

**Spec:** [Wave 2 design](../specs/2026-11-27-wave-2-crm-contacts-design.md); [Plan 4](2026-11-27-wave-2-plan-4-timeline-contact-ui.md); [Plan 4 handoff](../../handoff/2026-11-27-wave-2-plan-4-handoff.md); [integration concurrency](../../architecture/05-EVENTS-SYNC-AUTOMATION.md).

## Global Constraints

- Plan 4 đã có focused local review; actual-base compatibility vẫn review-required và browser E2E chưa rerun sau fix cuối. Không chuyển bằng chứng sang candidate mới hoặc coi Wave 2/P0 đã đóng; ghi và xử lý gate theo đúng review process, không tự rebaseline.
- TDD RED → GREEN → refactor; chỉ chạy test liên quan local. Full suite chỉ tại **Plan 9 Wave 2 exit trên GitHub CI**, không chạy local. Không tắt workflow/gate bắt buộc.
- Chọn release stable v5 được hỗ trợ, xác minh peer compatibility React 19/Node 24 và advisories tại lúc triển khai; pin chính xác phiên bản trong `apps/crm-web/package.json` và lockfile, dùng locked install. Không thêm Table, Router, Form hoặc Virtual trong plan này.
- Không đổi API/SDK contract, auth/tenant authorization, backend hoặc prototype. SDK `createCrmClient` tiếp tục kiểm soát bearer in-memory, `credentials: omit`, redirect, timeout, status, lossless number và reason/correlation.
- Không để token, PII, raw query/filter hoặc dữ liệu contact vào logs/URL/storage mới; không persist/dehydrate cache hay thêm devtools. Query keys chỉ ở bộ nhớ, token không nằm trong key.
- Từng read/mutation tiếp tục enforce tenant tại server. Khi identity, tenant, token/session generation thay đổi: không render kết quả cũ, cancel/clear cache cũ, không áp dụng callback cũ vào scope mới, kể cả server không tôn trọng abort. Không tái sử dụng cache của phiên trước sau logout/login cùng identity.
- **Không tự động retry mutation** (kể cả 412, timeout, 5xx/ambiguous commit); giữ draft và yêu cầu explicit reconciliation như Plan 4. Không dùng optimistic write cập nhật contact cache. Retry read mặc định tắt trong plan này, người dùng chủ động retry. Không retry 401/403/404/412.
- Custom numeric field phải giữ raw/lossless representation, không parse qua JavaScript `Number`; ETag là part của snapshot đã được review, không được gắn version mới vào data cũ.

## Review Focus

1. Identity/tenant đổi rồi đổi về cùng ID hoặc logout/login trong lúc A response/mutation về muộn: không render/cập nhật cache B hoặc phiên A mới (Tasks 1–4).
2. Background invalidation/refetch giữa khi người dùng sửa: không tự nhận ETag mới hay mất draft, stale 412 yêu cầu đọc lại toàn bộ record và sibling panels (Tasks 2–3).
3. Failed sibling read, thiếu ETag hoặc lỗi 403/404 khi reconciliation: write lock vẫn giữ cho tới khi explicit review thành công; không có version mới gắn vào dữ liệu cũ (Task 3).
4. Cursor lặp, filter đổi trong lúc reconciliation, hoặc query bị abort mà fetch vẫn resolve: lỗi an toàn, không append page/không báo reconciliation thành công (Task 2).
5. 201 note thiếu ETag, timeout sau write, delete không chắc kết quả và numeric values vượt JS precision: không resend, không làm tròn, không xóa draft chưa xác nhận (Tasks 3–4).

## Phạm vi và interface

- `apps/crm-web/src/contacts/queryScope.tsx`: `ContactQueryScope` nhận identity, tenantId, session generation và children; tạo QueryClient trong scope mới, không global singleton. `useContactQueryScope()` cung cấp scoped client + `scopeId` không chứa token; unmount hủy query và clear query/mutation cache. Bảo vệ cả render ngay trong cùng commit khi props thay đổi, không chờ effect mới giấu dữ liệu.
- `apps/crm-web/src/contacts/queryKeys.ts`: key factory `contacts(scopeId, filters)`, `contact(scopeId,id)`, `tags(scopeId)`, `fields(scopeId)`, `contactTags(scopeId,id)`, `fieldValue(scopeId,id,fieldId)`, `timeline(scopeId,id)`. Với page/cursor, dùng một infinite-query key cho cùng filters và truyền cursor qua `pageParam`; domain khác nhau có key riêng, không cache token.
- `apps/crm-web/src/contacts/contactQueries.ts`: các hook/options dùng `CrmClient`, truyền Query `signal` tới read methods. Dữ liệu cache giữ nguyên wrapper `{ data, etag }` và `FieldValue` lossless từ client; không stringify rồi `JSON.parse` hoặc thay thế decoder hiện có.
- `ContactShell.tsx`: tạo scoped provider gắn với auth/session/tenant, giữ per-scope client và AbortController; `ContactList.tsx`, `ContactDetail.tsx`, `ContactAttributes.tsx`, `ContactTimeline.tsx`, `CustomFieldSettings.tsx` và `recordPanels.ts`: chuyển read sang Query từng bước. `ContactForm.tsx` vẫn sở hữu draft và các bước xác nhận, chỉ dùng mutation interface qua record coordinator.
- Query options mặc định `retry: false`, mutation `retry: false`, không persist; tắt `refetchOnWindowFocus`/`refetchOnReconnect` và refetch tự động khi cần tránh thay snapshot đang review. Việc invalidate/read phải theo write coordinator và không tự làm mới draft. Có thể dùng `staleTime` và `gcTime` rõ ràng nhưng không được dựa vào GC để cách ly tenant.
- Test files tạo mới `queryScope.test.tsx`, `contactQueries.test.tsx` và mở rộng các tests contact hiện có. Browser regression dùng `apps/crm-web/tests/e2e/contacts.spec.ts`, opt-in stack thật theo handoff Plan 4.

## Task 0: Preflight Plan 4 và dependency

**Files:** `apps/crm-web/package.json`, `package-lock.json`, handoff cùng ngày.

- [ ] Đọc [Plan 4 handoff](../../handoff/2026-11-27-wave-2-plan-4-handoff.md) và `git status`, ghi exact HEAD và các thay đổi có sẵn. Ghi rõ actual-base compatibility review-required, browser test trước fix cuối, security FAIL/deferred; không coi chúng là pass của Plan 5.
- [ ] Kiểm tra metadata chính thức/npm của `@tanstack/react-query` v5, peer React 19, Node 24, license và dependency advisories; chọn **một** stable release tương thích, ghi version và lý do, pin exact (không `^`/`~`) và cập nhật lockfile qua pinned npm. Không thêm dependency runtime khác trừ khi có lý do được review. Đây là setup cho Task 1; không chạy full suite hoặc audit toàn repo tại local.

## Task 1: Scope, cache isolation và cancellation

**Consumes:** `TenantProvider` identity/selectedTenantId, `AuthClient` hiện tại. **Produces:** `ContactQueryScope`, `useContactQueryScope`, `queryKeys`; mỗi session generation có QueryClient và key space riêng.

**Files:** Create `queryScope.tsx`, `queryKeys.ts`, `queryScope.test.tsx`; modify `ContactShell.tsx` và test tương ứng; package/lock Task 0.

- [ ] Viết tests: `isolates_tenant_identity_and_session_generation`, `logout_and_relogin_same_identity_do_not_reuse_cache`, `late_read_and_mutation_callbacks_cannot_repopulate_new_scope`, `abort_signal_reaches_crm_client`, `query_and_mutation_retries_are_disabled`. Giả lập fetch không tôn trọng abort, A→B→A, refresh token/session generation, 401 invalidate và unmount; assert data A không render trong B hoặc A-session mới, old callback không invalidate/set data scope mới, token không trong key/cache metadata/log.
- [ ] Chạy `npm exec --workspace @crm-centric/crm-web -- vitest run src/contacts/queryScope.test.tsx src/contacts/ContactShell.test.tsx`; RED do provider/query chưa có. Ghi output thật.
- [ ] Implement provider/scope, key factory, cancellation và clear theo Global Constraints; queryFn phải consume signal, scope guard vẫn kiểm tra khi response về. Không thay đổi auth lifecycle hay dùng persisted cache. Dùng session generation khác nhau cả khi logout/login cùng identity, không dùng raw token làm ID.
- [ ] Chạy lại lệnh trên và `npm run typecheck --workspace @crm-centric/crm-web`: PASS. Commit dependency + scope + tests thành thay đổi review được.

## Task 2: Contact list và read queries

**Consumes:** Task 1 scoped provider/key factory, `CrmClient.listContacts/getContact/listTags/listFields/listContactTags/getField/listTimeline`. **Produces:** `contactQueries.ts` read hooks/options, `ContactList` dùng infinite query theo filters và cursor.

**Files:** Create `contactQueries.ts`, `contactQueries.test.tsx`; modify `ContactList.tsx`, `ContactList.test.tsx`, `ContactShell.test.tsx`, `crmClient.test.ts` khi cần.

- [ ] Viết tests: `list_paginates_server_cursor_without_duplicate_or_loop`, `filter_change_cancels_old_result_and_resets_pages`, `superseded_reconciliation_read_is_not_success`, `read_error_and_401_keep_existing_safe_behavior`, `lossless_field_and_etag_are_preserved`. Test same timestamp/cursor loop, pages duplicate ID như Plan 4, changed filters, A→B scope, delayed old response; verify server query/filter/cursor, không client-side filter tất cả contacts.
- [ ] Chạy `npm exec --workspace @crm-centric/crm-web -- vitest run src/contacts/contactQueries.test.tsx src/contacts/ContactList.test.tsx src/contacts/crmClient.test.ts`; RED cho query hooks/list path.
- [ ] Implement query keys/read options, explicit refetch/cancel và pagination. Giữ `WEB_CRM_READ_CANCELLED` cho interrupted reconciliation; không cho cancelled/stale request resolve thành success, không leak old filter data. Dữ liệu và ETag dùng kết quả `CrmClient`, không thay đổi API serialization.
- [ ] Chạy lại focused tests và scope tests: PASS; commit task.

## Task 3: Detail, attributes, timeline và write reconciliation

**Consumes:** Task 2 queries, `ContactDetail` `run`, `requestReview`, `refreshAttributes`, `RecordWrite`/`RegisterPanelRead`. **Produces:** reads/explicit refetch qua Query, coordinator vẫn là nguồn duy nhất để chấp nhận version và cho phép ghi.

**Files:** `ContactDetail.tsx`, `ContactAttributes.tsx`, `ContactTimeline.tsx`, `CustomFieldSettings.tsx`, `ContactForm.tsx`, `recordPanels.ts`, `contactQueries.ts` và tests tương ứng.

- [ ] Viết tests `refetch_does_not_replace_reviewed_etag_during_edit`, `refresh_requires_contact_and_all_siblings_before_unlock`, `failed_sibling_read_keeps_draft_and_lock`, `missing_etag_requires_explicit_review`, `failed_or_ambiguous_write_never_retries`. Cover POST upsert expected ID+ETag, PATCH/DELETE, add/remove tag, field/note; 412 giữ draft, 403 read-only, 404 khóa/xóa an toàn, timeout không tự resend, note 201 thiếu ETag cần contact GET + sibling reconciliation. Kiểm tra delete cần xác nhận mới sau review; no-op và new version đúng. Simulate refetch đến giữa các bước và concurrent panel edits.
- [ ] Chạy `npm exec --workspace @crm-centric/crm-web -- vitest run src/contacts/ContactAttributes.test.tsx src/contacts/ContactTimeline.test.tsx src/contacts/ContactForm.test.tsx src/contacts/CustomFieldSettings.test.tsx`; ghi RED cho tests mới.
- [ ] Chuyển read/panel refresh sang Query, giữ explicit reviewed snapshot riêng với query cache: chỉ accept ETag khi contact và tất cả sibling reads cần thiết cùng scope thành công; nếu panel fail giữ khóa/draft. Mutations có thể dùng `useMutation` với `retry: false` hoặc giữ `CrmClient` trong `RecordWrite`; mọi invalidate chỉ kích hoạt sau kết quả xác nhận, 412/timeout không tự write lại. Không để cache refetch thay snapshot đang edit; không optimistic-update record.
- [ ] Chạy lại focused tests, `contactQueries.test.tsx`, `crmClient.test.ts`: PASS; commit task.

## Task 4: Real browser regression và review Plan 5

**Files:** `apps/crm-web/tests/e2e/contacts.spec.ts`, tests tập trung mới nếu cần, `docs/handoff/YYYY-MM-DD-wave-2-handoff.md`, tài liệu architecture/frontend nếu có.

- [ ] Bổ sung vào browser fixture hiện có các assertion Query-specific: tenant A→B→A với response A bị trì hoãn, logout/login cùng principal, hai session conflict và explicit draft review; test read refetch không làm mới ETag đã review hoặc tự gửi note. Kiểm thử custom field 29+ chữ số và field vượt JS range không bị làm tròn; nếu fixture chỉ chứng minh 29 chữ số, test extreme qua client Vitest và ghi rõ phạm vi browser. Không in secrets/tokens.
- [ ] Chạy focused tests local: `npm exec --workspace @crm-centric/crm-web -- vitest run src/contacts/queryScope.test.tsx src/contacts/contactQueries.test.tsx src/contacts/ContactShell.test.tsx src/contacts/ContactList.test.tsx src/contacts/ContactAttributes.test.tsx src/contacts/ContactTimeline.test.tsx src/contacts/ContactForm.test.tsx src/contacts/CustomFieldSettings.test.tsx src/contacts/crmClient.test.ts`; `npm run typecheck --workspace @crm-centric/crm-web`; ESLint chỉ các file đã đổi và build frontend. Ghi command, SHA, pass/fail/skip, pinned versions và logs an toàn.
- [ ] Chạy browser thật `CRM_REAL_OIDC_TEST=1 npm exec --workspace @crm-centric/crm-web -- playwright test tests/e2e/contacts.spec.ts` theo prerequisites/teardown trong Plan 4 handoff. Xác nhận executed, 0 skipped; nếu không thể chạy ghi NOT RUN và blocker, không dùng mocked API thay evidence. Bảo đảm Plan 9 hosted job chọn contacts spec với opt-in và xác minh executed/pass.
- [ ] Review cache isolation, callback cũ, query keys, ETag reconciliation, no implicit mutation retry, permission, lossless number, accessibility và diff dependency/license/security. Ghi test và actual-base compatibility của candidate theo kết quả thật; không gọi Plan 4/5 gate pass chỉ từ handoff cũ. Cập nhật docs/handoff, plan 6 Table là bước tiếp theo, Plan 9 full CI/benchmark, security FAIL/deferred và P0 OPEN. `git diff --check` và commit tài liệu phù hợp; không full suite local.
