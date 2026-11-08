# UX / Navigation / User Journeys

## Sitemap CRM P0
- `/login` → redirect đến IdP.
- `/app/contacts` → search, filters, bulk tagging có limit.
- `/app/contacts/:contactId` → header, properties, tags, timeline, extension panels.
- `/app/automations` → rule list, rule detail, execution history, pause/resume.
- `/app/settings/team` → invites/roles.
- `/app/settings/custom-fields` → field schema.
- `/app/settings/modules` → directory / installations / health / permissions.
- `/app/audit` → audit trail (tenant admin).

## Tích hợp UI module
- P0 Navigation: đăng ký external URL, validate HTTPS/origin; mở trang module qua SSO.
- P0 Contact panel: remote module trả dữ liệu theo JSON schema đã whitelist; host render text, number, date, link được sanitize; không nhận HTML/JS thô.
- P2: sandboxed iframe / microfrontend sau security review (CSP, postMessage handshake, permission, version pin).

## Journey A: Lead từ blog độc lập
Blog lead form → CRM endpoint với signed integration credentials → normalize/consent source → dedup/contact insert → `crm.contact.created.v1` → timeline → rule add tag. Form công khai phải có captcha/rate limit và không expose service secret.

## Journey B: Module Trading mới
Admin cài manifest → verify ownership and endpoint → grant scopes per tenant → connection handshake → register triggers/actions/nav/panels → receives signed `trade.closed` → map external trader to contact (explicit mapping) → timeline → automation.

## Journey C: Mất mạng
Module event persisted in its outbox → delivery retry; CRM accepts event transactionally then returns 2xx → duplicate same event ID treated idempotently; failed delivery visible in module diagnostics and replayable.

## UX Rules
- Bất kỳ sự kiện nào chưa map được contact phải vào Unmatched Events queue, không tự động merge theo email.
- Hiển thị freshness/last sync; eventual consistency không bị che giấu.
- Module unavailable: show disabled state and last known status, core CRM vẫn hoạt động.
