# Wave 2 Plan 8 — TanStack Form Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` task by task. One coding agent at a time per AGENTS.md. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Đưa TanStack Form vào các form Contact hiện có, giữ contract, tenant/session isolation, ETag reconciliation, draft và số custom field chính xác.

**Architecture:** TanStack Form quản lý giá trị, validation và trạng thái trình bày; `CrmClient`, `ContactQueryScope` và `ContactDetail`/`RecordWrite` tiếp tục sở hữu transport, ETag, khóa ghi và reconciliation. Chuyển từng nhóm form trong React hiện tại, giữ Router/Query/Table và giao diện prototype; không thay đổi API hoặc tự retry mutation.

**Tech Stack:** React 19, TypeScript strict, Vite, TanStack Query/Table/Router hiện có, `@tanstack/react-form` stable, Vitest/Testing Library, Playwright với PostgreSQL/Keycloak thật.

**Spec:** [Wave 2 design](../specs/2026-11-27-wave-2-crm-contacts-design.md); [frontend convention](../../engineering/02-REPO-CI-CD.md); [Plan 7 handoff](../../handoff/2026-11-27-wave-2-plan-7-handoff.md); [CRM OpenAPI](../../contracts/crm-api.openapi.yaml); [TanStack Form validation](https://tanstack.com/form/latest/docs/framework/react/guides/validation).

## Global Constraints

- Plan 7 có focused local review, không đồng nghĩa hosted CI hay Wave 2/P0 pass. Actual-base compatibility vẫn review-required theo kết quả gần nhất; security FAIL/deferred. Không tự rebaseline, thay contract hay tắt gate.
- TDD RED → GREEN → refactor; chỉ chạy test liên quan local. Full suite chỉ chạy ở **Plan 9 Wave 2 exit trên GitHub CI**, không chạy full suite local. Không coi browser skip/mocked API là real E2E.
- Chọn `@tanstack/react-form` stable tương thích React 19 và Node 24 tại thời điểm triển khai; review peer/license/advisory, pin exact manifest/lockfile bằng pinned npm. Không thêm Start, Virtual, chart hoặc thay Query/Table/Router.
- Backend là nguồn xác thực, tenant, quyền, validation và version. Giữ request `If-Match` và conditional upsert `X-Contact-Id`; thiếu/stale/403 không bypass; không tự gửi lại mutation sau 412, timeout hay kết quả commit chưa xác định.
- Form state là draft của đúng identity + tenant + session + contact; scope đổi phải hủy/loại bỏ data, không để callback hay response cũ cập nhật form/scope mới. Không đưa PII, credentials, form state hoặc auth code vào logs, URL hay browser persistence mới.
- Dữ liệu custom field số giữ raw token qua `exactNumber`/`FieldValue` và `losslessJson`; không dùng `Number`, `parseFloat`, `valueAsNumber`, JSON parse/stringify làm thay đổi giá trị. `null`, empty string và omitted fields theo contract hiện tại; không âm thầm đổi semantics.
- Giữ labels, keyboard, focus, responsive, HTML-escaping và prototype; nếu có giới hạn validation mới làm thu hẹp contract thì cần review contract/ADR và consumer tests, không tự thêm. Dùng Unicode scalar count khi contract đo độ dài, không mặc định `maxLength` UTF-16 là tương đương.

## Review Focus

1. `412`, thiếu ETag, failed sibling refresh hoặc timeout sau write: giữ draft và write lock cho tới explicit review, không tự resend hay chấp nhận version mới vào dữ liệu cũ (Tasks 1–3).
2. Hai contact cùng email/tên hoặc email được tái sử dụng: conditional POST chỉ dùng contact ID + ETag do người dùng chọn/review; không tự chuyển intent sang contact khác (Task 1).
3. Field number `1e131072`, `1e-16384`, số 29+ chữ số, zero và null/empty: không làm tròn, không nhầm giữa xóa và string rỗng, không thay đổi giá trị chưa sửa (Task 2).
4. Tenant/contact/session/route đổi khi submit hoặc validation pending: không render draft cũ, không navigate hoặc cập nhật state/cache mới từ callback cũ, kể cả request không tôn trọng abort (Tasks 1–4).
5. Form state refetch/reset trong lúc có draft, double submit hoặc 201 thiếu ETag: không mất dữ liệu chưa xác nhận, không gửi hai lần và phải refresh/review theo coordinator (Tasks 1–3).

## Phạm vi và file map

- `apps/crm-web/package.json`, `package-lock.json`: `@tanstack/react-form` exact; giữ các dependency và versions khác.
- `apps/crm-web/src/contacts/ContactForm.tsx`: create, partial PATCH, conditional upsert; `ContactForm.test.tsx` và `ContactList.test.tsx` bao phủ choices và reconcile.
- `src/contacts/ContactAttributes.tsx`: create/attach tag và `FieldEditor`; `ContactAttributes.test.tsx`, `contactSession.test.tsx`; `src/contacts/losslessJson.ts` và `crmClient.ts` giữ wire semantics hiện có.
- `src/contacts/CustomFieldSettings.tsx` và `src/settings/CustomFieldsPage.tsx`: tenant field-definition forms; tests tương ứng. Có thể trích form presentation dùng chung, nhưng không trộn quyền tenant-level với `RecordWrite`/contact ETag.
- `src/contacts/ContactTimeline.tsx`: note form; `ContactTimeline.test.tsx`. `ContactDetail.tsx`/`recordPanels.ts` là write/review coordinator, chỉ sửa khi tests yêu cầu, không chuyển quyền quyết định ETag sang form library.
- `apps/crm-web/tests/e2e/contacts.spec.ts`: journey thật với OIDC, API và PostgreSQL; `docs/handoff/YYYY-MM-DD-wave-2-plan-8-handoff.md`: evidence.
- Nếu trích helpers, đặt `src/contacts/formValidation.ts` cho pure validators (không logs/API calls); typed form values giữ raw strings và hiện diện/null riêng, không đưa ETag vào mutable form values.

## Task 0: Preflight và dependency

**Consumes:** [Plan 7 handoff](../../handoff/2026-11-27-wave-2-plan-7-handoff.md), current git/package state. **Produces:** một version TanStack Form stable được pin cho Task 1.

- [ ] Kiểm tra `git status --short --branch`, `git log -1 --oneline`; ghi SHA, pre-existing work và Plan 7 limitations: deployed SPA fallback chưa được xác minh, actual-base compatibility chưa có candidate verdict mới, security FAIL/deferred. Không suy diễn pass trên SHA khác.
- [ ] Đối chiếu npm metadata và docs chính thức `@tanstack/react-form` tại thời điểm thực hiện: stable release, React 19/Node 24 peers, license/advisory và compatibility với Query/Router hiện có. Pin exact version, cập nhật lockfile bằng npm 11.19.1, không thêm schema library nếu chưa cần. Ghi version/nguồn; nếu toolchain/advisory chặn, ghi blocker, không thay security policy. Commit dependency cùng Task 1 sau verification.

## Task 1: Contact create/edit form

**Consumes:** `ContactForm` props, `CrmClient`, `Snapshot`, `ContactDetail` coordinator. **Produces:** cùng props/observable behavior với values do TanStack Form quản lý; ETag/ID reviewed state nằm ngoài mutable form data.

**Files:** `ContactForm.tsx`, `ContactForm.test.tsx`, `ContactList.test.tsx`, `contactSession.test.tsx`, package/lockfile.

- [ ] Viết tests `preserves_partial_patch_and_explicit_null`, `does_not_overwrite_reviewed_etag_after_query_refetch`, `duplicate_email_requires_selected_id_and_etag`, `stale_or_uncertain_write_keeps_draft_and_requires_review`. Assert PATCH chỉ gửi field thực sự thay đổi, omitted giữ nguyên, email/phone clear gửi null; no-op không gửi. 428 yêu cầu chọn record hiển thị, đọc lại và review đúng ID + ETag; email reuse/412 không tự cập nhật record khác. 403 read-only; invalid/missing ETag và failed sibling refresh giữ lock.
- [ ] Viết tests `freezes_draft_and_prevents_double_submit`, `scope_change_ignores_late_save`, `unicode_validation_matches_contract`: giữ input trong khi pending, double click/Enter chỉ một request, A→B→A/logout/login/contact route change không navigate/cập nhật scope mới khi response cũ đến; test 200 Unicode scalar vs 201 theo contract, không rò PII qua error.
- [ ] Chạy `npm exec --workspace @crm-centric/crm-web -- vitest run src/contacts/ContactForm.test.tsx src/contacts/ContactList.test.tsx src/contacts/contactSession.test.tsx`; ghi RED cho hành vi chưa có hoặc test chứng minh chuyển đổi. Nếu invariant hiện tại đã pass, ghi baseline PASS; không tạo RED giả bằng cách xóa bảo vệ.
- [ ] Chuyển form values/validation sang TanStack Form; giữ state `base`, `blocked`, `choosing`, `review` và cơ chế coordinator theo ngữ nghĩa hiện có; chỉ reset sau xác nhận thành công trong scope hiện tại. Không tự áp refetched snapshot vào draft. Giữ no-op, accessibility/focus và disable trong pending.
- [ ] Chạy lại focused suite, `npm run typecheck --workspace @crm-centric/crm-web` và targeted ESLint: PASS; commit dependency, form và tests.

## Task 2: Tag và custom field values

**Consumes:** `RecordWrite`, `usePanelRead`, `exactNumber`, Task 1 scope rules. **Produces:** forms tag và từng FieldEditor dùng TanStack Form; `ContactDetail` vẫn quyết định ETag/lock.

**Files:** `ContactAttributes.tsx`, `ContactAttributes.test.tsx`, `losslessJson.ts`/tests chỉ nếu cần, `contactSession.test.tsx`.

- [ ] Viết tests tạo/gắn tag với input hợp lệ, no-op/403/412/timeout và double submit; draft không mất khi failure, scope switch bỏ draft cũ. Test `FieldEditor` string/boolean/date/number, required/optional, null vs empty string và chưa sửa; assert chính xác body theo contract và không gửi mutation khi draft không đổi. Các giới hạn hiện chưa được giải quyết về empty string vs null phải được ghi nhận; nếu thay semantics, thực hiện qua quyết định/contract review riêng, không giấu thay đổi trong migration form.
- [ ] Viết round-trip tests raw numeric `1e131072`, `1e-16384`, 29+ chữ số và decimal tương đương; assert raw request token và response giữ chính xác, không qua `Number`. Test stale 412 giữ draft, reload contact + sibling panels trước khi nhận version; sibling 403/404/failure giữ lock, không có write bổ sung.
- [ ] Chạy `npm exec --workspace @crm-centric/crm-web -- vitest run src/contacts/ContactAttributes.test.tsx src/contacts/crmClient.test.ts src/contacts/contactSession.test.tsx`; ghi baseline/RED thực tế.
- [ ] Chuyển tag forms và FieldEditor sang TanStack Form, giữ mỗi field có lifecycle riêng và raw string/token; mọi mutation dùng `run` và `onRecord` hiện có. Không reset do background data trừ khi user đã review thành công; không làm hỏng `register` sibling refresh. Nếu cần phân biệt empty/null mà UI hiện chưa hỗ trợ, ghi blocker/follow-up theo contract thay vì âm thầm xóa hoặc làm tròn.
- [ ] Chạy lại focused suite: PASS; commit task.

## Task 3: Definition forms và note

**Consumes:** `CustomFieldSettings`, `CustomFieldsPage`, `ContactTimeline`, `RecordWrite` và scope Query hiện có. **Produces:** definition và note forms dùng TanStack Form; giữ tenant-level create tách contact mutation.

**Files:** `CustomFieldSettings.tsx`, `CustomFieldsPage.tsx`, `ContactTimeline.tsx`, tests `CustomFieldSettings.test.tsx`, `settings/CustomFieldsPage.test.tsx`, `ContactTimeline.test.tsx`.

- [ ] Viết tests definition key regex, display name scalar limit, dataType/required, duplicate 409, owner/admin vs server 403, timeout/no-retry, failed list refresh và tenant/session switch khi create pending. Định nghĩa tenant-level không gửi contact ETag giả; reuse UI chỉ khi không thay permission/lock semantics.
- [ ] Viết note tests độ dài 1–1000 Unicode scalar, markup hiển thị literal, 412 giữ draft, double submit một request, 201 có ETag và 201 thiếu ETag (GET contact + sibling review trước write), refetch timeline khi draft mở không tự submit/mất draft, timeout cần explicit reconciliation; đổi contact/tenant bỏ scope cũ.
- [ ] Chạy `npm exec --workspace @crm-centric/crm-web -- vitest run src/contacts/CustomFieldSettings.test.tsx src/settings/CustomFieldsPage.test.tsx src/contacts/ContactTimeline.test.tsx`; ghi RED hoặc baseline theo thực tế.
- [ ] Implement TanStack Form cho cả hai definition contexts và note, tách submit/field state khỏi authorization và `RecordWrite`; reset chỉ khi request thành công trong scope hiện tại. Không đổi behavior callback OIDC/routing hoặc persist draft sang tenant khác.
- [ ] Chạy lại focused suite và `ContactAttributes.test.tsx`: PASS; commit task.

## Task 4: Browser, accessibility và Plan 8 review

**Files:** `apps/crm-web/tests/e2e/contacts.spec.ts`, focused tests nếu cần, `docs/handoff/YYYY-MM-DD-wave-2-plan-8-handoff.md`.

- [ ] Bổ sung real-provider browser assertions cho contact partial PATCH, duplicate upsert ID/ETag, note 412 draft, field số chính xác, required/null, settings 403, double submit và A→B→A/logout/relogin khi request pending. Test keyboard/focus, labels, 390px; không log tokens/PII. Ghi rõ giới hạn fixture nếu không thể xác minh một case thực; không giả claim RED khi assertion viết sau code.
- [ ] Chạy `CRM_REAL_OIDC_TEST=1 npm exec --workspace @crm-centric/crm-web -- playwright test tests/e2e/contacts.spec.ts` theo prerequisites và teardown trong [Plan 7 handoff](../../handoff/2026-11-27-wave-2-plan-7-handoff.md); ghi SHA, số pass/fail/skip và môi trường. Nếu không chạy được ghi NOT RUN/blocker, không dùng mocked API thay real E2E.
- [ ] Chạy local focused Vitest: `npm exec --workspace @crm-centric/crm-web -- vitest run src/contacts/ContactForm.test.tsx src/contacts/ContactAttributes.test.tsx src/contacts/CustomFieldSettings.test.tsx src/settings/CustomFieldsPage.test.tsx src/contacts/ContactTimeline.test.tsx src/contacts/contactSession.test.tsx src/contacts/crmClient.test.ts src/contacts/queryScope.test.tsx src/router.test.tsx`; chạy `npm run typecheck --workspace @crm-centric/crm-web`, targeted ESLint và frontend build. Ghi version, SHA, RED/GREEN/PASS/FAIL/SKIP thật; không full suite local.
- [ ] Review dependency/lock/license/advisory, reviewed snapshot ETag, privacy tenant, no automatic retry, empty/null, raw numbers, accessibility và Plan 7 router/callback. Ghi actual-base compatibility/security theo evidence thực, không coi npm audit đơn lẻ là security gate. Cập nhật handoff, Plan 9 hosted full suite + benchmark tiếp theo và `git diff --check`; commit docs phù hợp. Wave 2/P0 còn OPEN.
