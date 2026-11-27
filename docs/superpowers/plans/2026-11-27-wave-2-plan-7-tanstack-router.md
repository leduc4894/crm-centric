# Wave 2 Plan 7 — TanStack Router Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` task by task. One coding agent at a time per AGENTS.md. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Chuyển điều hướng Contact UI sang TanStack Router, giữ OIDC callback an toàn, tenant/session isolation và các luồng Query/Table/ETag hiện có.

**Architecture:** Dùng code-based route tree và browser history trong React/Vite, không đưa dữ liệu contact vào router loader/cache. Root giữ `ConfiguredAuthProvider` và `TenantProvider`; routes Contact dùng scope identity/tenant/session của Plan 5, còn server vẫn quyết định quyền. Điều hướng bằng typed links/navigate, giữ callback prelude chạy trước module và không ghi thông tin nhạy cảm vào URL.

**Tech Stack:** React 19, TypeScript strict, Vite, TanStack Query/Table hiện có, `@tanstack/react-router` stable, Vitest/Testing Library và Playwright với OIDC/PostgreSQL thật.

**Spec:** [Wave 2 design](../specs/2026-11-27-wave-2-crm-contacts-design.md); [frontend convention](../../engineering/02-REPO-CI-CD.md); [Plan 6 handoff](../../handoff/2026-11-27-wave-2-plan-6-handoff.md); [prototype](../../../prototypes/centric-ui/index.html).

## Global Constraints

- Plan 6 completed focused local verification/review; actual-base compatibility vẫn `CONTRACT_POLICY_REVIEW_REQUIRED` theo bằng chứng gần nhất, security FAIL/deferred. Không chuyển verdict sang candidate mới, không bypass gate; Wave 2/P0 OPEN.
- TDD RED → GREEN → refactor; chỉ chạy test liên quan local. Full suite chạy tại **Plan 9 Wave 2 exit trên GitHub CI**, không chạy full suite local hoặc tắt required CI.
- Chọn release `@tanstack/react-router` stable, tương thích React 19 và Node 24, review license/peer/advisory lúc triển khai; pin exact vào manifest và lockfile. Router-only: không thêm TanStack Start, Form, Virtual, server rendering hay đổi backend/API/SDK contract.
- Đường dẫn chỉ là state UI, không cấp quyền. Mọi API vẫn xác thực issuer/sub, verified tenant/staff và scope. Không lưu token, auth code/state, PII, search filter hay cursor vào route path/search/hash hoặc logs; không đưa tenant selection thành bằng chứng authorization.
- Giữ ContactQueryScope identity+tenant+session generation, abort/cancel và guard response; không reuse cache phiên cũ kể cả logout/login cùng principal hoặc A→B→A. Không dùng router loader/preload để cache hay fetch contact; Query vẫn là nguồn quản lý dữ liệu server. Tắt speculative preloading mặc định, không tự fetch khi hover.
- Giữ ETag/reviewed snapshot, sibling reconciliation, draft khi 412, handling timeout/ambiguous commit, upsert ID+ETag, lossless numeric field, server filter/cursor và Table order. Navigation không được tự gửi lại mutation hoặc áp dụng response cũ vào record/session mới.
- Giữ prototype, semantic links, Ctrl/Cmd/middle click, focus/keyboard, responsive; không render contact từ tenant khác trong khi route chờ auth hoặc tenant. Không claim P0 acceptance/benchmark.

## Review Focus

1. OIDC `/auth/callback?code=...&state=...`: URL được làm sạch trước tài nguyên/module, callback được xử lý một lần; router không sao chép secret vào history, referrer, logs hoặc redirect ngoài origin (Tasks 1, 4).
2. A→B→A, logout/login cùng identity, hoặc điều hướng sang contact khác trong khi read/write từ scope cũ trả muộn: dữ liệu/draft/callback cũ không hiện hoặc ghi cache mới (Tasks 2–4).
3. Route ID sai định dạng/encoded slash/uppercase UUID, path lạ, direct refresh và back/forward: xử lý xác định, không fetch ID không hợp lệ, không phá callback hoặc base URL (Tasks 1–2).
4. User rời route khi có draft hoặc mutation chưa rõ kết quả: không tự submit/retry, không chuyển draft sang contact/tenant khác; khi quay lại yêu cầu đọc state mới trước write, không tuyên bố kết quả request cũ (Tasks 2–3).
5. Note/tag/field conflict `412`, thiếu ETag, failed sibling refresh hoặc read-only 403 sau route transition: vẫn giữ draft và write lock/review đúng; Query/Table không sắp/filter client hoặc trả dữ liệu tenant khác (Tasks 2–3).

## Route contract và file map

- `/`: shell platform/health và tenant picker hiện có. `/auth/callback`: route callback không điều hướng tự động; bảo toàn bootstrap `window.__crmOidcCallback`, cleanup URL trước tài nguyên và `ConfiguredAuthProvider` xử lý callback một lần. Không thay issuer, logout hay nơi lưu token hiện tại.
- `/app/contacts`: list. `/app/contacts/$contactId`: record; chấp nhận UUID chữ hoa/thường theo `contactIdPattern` hiện có, validate trước mount/fetch, path không hợp lệ dùng not-found an toàn. Không đổi contract backend hoặc tự normalize ID trong request thành ID khác.
- `/app/settings/custom-fields`: trang settings tenant-scoped dùng API definitions hiện có; không gắn settings với một contact giả. Giữ chức năng settings đang được nhúng trong detail nếu còn cần để không phá UI hiện tại; nếu tái sử dụng component đang phụ thuộc `RecordWrite`, tách presentation/tenant definition writes thay vì tạo fake contact/ETag. Chỉ owner/admin được tạo theo backend; agent/operator xử lý 403 an toàn. Route này hiện **chưa có**, là phạm vi UI Plan 7 theo spec.
- `apps/crm-web/src/router.tsx`: `createAppRouter()` tạo code-based `routeTree` với root, platform, callback, list, detail, settings và not-found; `RouterProvider` dùng browser history. Tests tạo router/memory history riêng, không chia singleton giữa tests. Không dùng file generation plugin hoặc dependency mới ngoài router nếu không cần.
- `apps/crm-web/src/App.tsx`, `src/main.tsx`: lắp providers và root layout; `src/contacts/ContactShell.tsx` nhận route params thay vì `useSyncExternalStore/popstate`; `ContactTable.tsx`, `ContactList.tsx`, `ContactDetail.tsx` dùng `Link`/typed navigate thay helpers `navigate/openContacts`. Preserve `TenantPicker`, auth và `HealthStatus` trên các route như trước.
- `apps/crm-web/src/contacts/CustomFieldSettings.tsx` và `src/settings/CustomFieldsPage.tsx`: nếu cần tách logic để dùng settings ở tenant scope; data client vẫn dùng `createCrmClient`, Query scope và API Plan 3. Không thay schema hoặc số lossless.
- Tests: `apps/crm-web/src/router.test.tsx`, `src/contacts/ContactShell.test.tsx`, `src/contacts/contactSession.test.tsx`, `src/settings/CustomFieldsPage.test.tsx`, hiện có `src/auth/*.test.ts`, `config/devServer.test.ts`, `tests/e2e/browser-oidc.spec.ts`, `tests/e2e/contacts.spec.ts`.

## Task 0: Preflight và dependency

**Consumes:** Plan 6 handoff, package/lockfile và git status. **Produces:** stable exact router version cho Task 1.

- [ ] Ghi `git status --short --branch`, SHA, pre-existing work, Plan 6 evidence và các gate còn open; không coi hosted CI hay compatibility đã pass.
- [ ] Kiểm tra npm metadata/docs chính thức của `@tanstack/react-router` tại thời điểm thực hiện, peer React 19/Node 24, stable status, license và advisories. Chọn một version stable được hỗ trợ, pin exact bằng pinned npm và cập nhật lockfile; không dùng prerelease hoặc floating range. Ghi dependency graph và lý do, không in tokens. Commit dependency cùng Task 1 sau focused tests.

## Task 1: Route tree, callback và navigation cơ bản

**Consumes:** current `ConfiguredAuthProvider`, callback prelude, `TenantProvider`, `ContactShell`. **Produces:** `createAppRouter()` và root/callback/platform/list/detail routes; stable link navigation theo Router.

**Files:** `src/router.tsx`, `src/router.test.tsx`, `src/App.tsx`, `src/main.tsx`, `src/contacts/ContactShell.tsx`, `ContactTable.tsx`, `ContactList.tsx`, `ContactDetail.tsx`, package/lockfile.

- [ ] Viết `router.test.tsx`: direct `/`, `/app/contacts`, `/app/contacts/{valid UUID}`; invalid ID/encoded slash/unknown path không gọi contact API; uppercase ID được hỗ trợ; back/forward, refresh/deep-link và links Ctrl/Cmd/middle-click giữ browser behavior. Route API path `/api/...` không trở thành app contact route; not-found không lộ data.
- [ ] Viết callback tests: `/auth/callback?code=synthetic&state=synthetic` prelude dọn URL trước module/resource; `completeCallback` nhận original một lần kể cả StrictMode, URL không bị router chèn lại query, login/logout/replay/invalid state không đổi security behavior; không log synthetic code hoặc đưa vào links/history/referrer. Giữ test existing development và built prelude.
- [ ] Chạy `npm exec --workspace @crm-centric/crm-web -- vitest run src/router.test.tsx src/contacts/ContactShell.test.tsx src/auth/AuthProvider.test.tsx config/devServer.test.ts`; ghi RED do route tree chưa có, kiểm tra số tests và lý do.
- [ ] Implement code-based routes và `RouterProvider`, link/navigate cho list/detail; UUID validation trước request. Không thay `index.html` callback prelude, không gọi navigate trước khi bootstrap đã lấy callback; callback path không redirect sang origin khác. Không prefetch/load contact trong router; giữ native anchors/modified click và scoped Query semantics. Xóa `history.pushState`/popstate helpers chỉ sau khi mọi callsite chuyển.
- [ ] Chạy lại focused tests, `npm run typecheck --workspace @crm-centric/crm-web` và targeted ESLint: PASS; commit task.

## Task 2: Tenant, session và reconciliation qua routes

**Consumes:** Task 1 routes, `ContactQueryScope`, `CrmClient`, Plan 5 record coordinator. **Produces:** điều hướng không làm rò state hoặc suy yếu write lock.

**Files:** `ContactShell.tsx`, `router.tsx`, `queryScope.tsx` chỉ nếu cần; `ContactShell.test.tsx`, `contactSession.test.tsx`, `ContactForm.test.tsx`, `ContactAttributes.test.tsx`, `ContactTimeline.test.tsx`.

- [ ] Viết tests A→B→A và logout/login cùng identity khi đang ở cùng deep link, deferred response không tôn trọng abort và mutation callback muộn; không render/cache state A trong B hay session mới. Chuyển `contactId` trong khi field/note draft hoặc delete confirm mở: dữ liệu cũ được dispose, không chuyển sang record khác, không tự submit.
- [ ] Test route list→detail→list/back/forward sau write thành công hoặc timeout: không hiện cache list cũ; `412` giữ draft khi vẫn ở route, failed sibling refresh giữ khóa, thiếu ETag yêu cầu explicit review, rời và quay lại phải đọc lại trước ghi. Test 403/404 và contact bị soft-delete. Không tự resend mutation sau navigation hay uncertain outcome.
- [ ] Chạy `npm exec --workspace @crm-centric/crm-web -- vitest run src/contacts/ContactShell.test.tsx src/contacts/contactSession.test.tsx src/contacts/ContactForm.test.tsx src/contacts/ContactAttributes.test.tsx src/contacts/ContactTimeline.test.tsx`; ghi RED nếu test mới phát hiện lỗi; baseline PASS phải được ghi trung thực.
- [ ] Sửa scope key/remount và liên kết route theo kết quả test; không đặt access token/tenant data trong router context/cache, không thay Query defaults hoặc direct client fetch ngoài `CrmClient`. Giữ no-prefetch và no automatic mutation retry. Test lại focused suite: PASS; commit task.

## Task 3: Route custom-field settings

**Consumes:** Plan 3 fields API, Task 1 route tree và Query scope. **Produces:** tenant-scoped `/app/settings/custom-fields` để list/create definitions; contact detail fields giữ nguyên.

**Files:** `src/settings/CustomFieldsPage.tsx`, `src/settings/CustomFieldsPage.test.tsx`, `src/contacts/CustomFieldSettings.tsx` nếu tách shared component, `src/router.tsx`, `src/App.tsx`.

- [ ] Viết tests direct/deep link, tenant chưa chọn không fetch, list pagination/cursor theo API, valid definition create và refresh; owner/admin thành công, agent/operator 403 không ghi hoặc vượt quyền; unknown role không suy diễn từ UI. Failed create giữ draft, không tự retry timeout; duplicate key hiển thị error. Test tenant switch lúc list/create pending, response cũ không hiện/không cập nhật scope mới; server vẫn kiểm quyền.
- [ ] Chạy `npm exec --workspace @crm-centric/crm-web -- vitest run src/settings/CustomFieldsPage.test.tsx`; ghi RED trước implement.
- [ ] Implement settings page bằng `createCrmClient`/scoped Query của tenant, không dùng `RecordWrite`/contact ETag giả cho tenant-level create. Giữ component tạo definition trong detail hoặc chia sẻ UI hợp lý; navigation label rõ, permission response từ server là authoritative. Không đưa field values hoặc số qua JS `Number` trong refactor.
- [ ] Chạy lại test trên cộng `src/contacts/CustomFieldSettings.test.tsx` và `src/contacts/ContactAttributes.test.tsx`: PASS; commit task.

## Task 4: Browser thật, build và Plan 7 handoff

**Files:** `tests/e2e/contacts.spec.ts`, `tests/e2e/browser-oidc.spec.ts` nếu cần, `docs/handoff/YYYY-MM-DD-wave-2-plan-7-handoff.md`.

- [ ] Thêm focused browser assertions với stack thật: direct URL/reload record, link list/detail/settings, back/forward, keyboard và Ctrl/Cmd click; login callback, logout/relogin, tenant A→B→A khi response A bị giữ rồi thả trong B. Xác nhận scope cũ không render, mutation không tự resend, 412 vẫn giữ draft; settings create theo quyền. Không dùng secrets thật trong fixture hay logs.
- [ ] Chạy `CRM_REAL_OIDC_TEST=1 npm exec --workspace @crm-centric/crm-web -- playwright test tests/e2e/contacts.spec.ts` và real-provider OIDC focused test theo lệnh trong [Plan 4 handoff](../../handoff/2026-11-27-wave-2-plan-4-handoff.md). Chạy các callback cleanup browser tests trên development và built mode khi có prerequisites. Ghi PASS/FAIL/SKIP và exact SHA; không dùng mocked API hoặc test bị skip để claim real E2E.
- [ ] Chạy local focused Vitest `src/router.test.tsx`, `src/settings/CustomFieldsPage.test.tsx`, `src/contacts/{ContactShell,contactSession,ContactForm,ContactAttributes,ContactTimeline,CustomFieldSettings,queryScope}.test.tsx`, auth/callback và proxy tests liên quan; `npm run typecheck --workspace @crm-centric/crm-web`, targeted ESLint và frontend build. Ghi command, count, SHA, environment và kết quả thực; không full suite local.
- [ ] Review Router dependency/license/advisory, callback no-leak, server authorization, cache/session scope, ETag, lossless number, accessibility/deep links và host SPA fallback. Ghi Plan 8 Form tiếp theo, Plan 9 hosted full suite/benchmark; compatibility actual-base và security theo evidence thực, Wave 2/P0 OPEN. `git diff --check`, cập nhật handoff và commit tài liệu phù hợp.
