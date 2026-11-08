# Test Strategy — TDD + Contract + Failure Injection

## Test pyramid
1. Domain unit tests: contact rules, tag normalization, state transitions, predicate evaluation, dedup.
2. Application tests: use cases with ports mocked; tenant auth guard.
3. Integration: PostgreSQL/Testcontainers, EF migration, outbox transaction, RLS, HTTP integration with fake IdP.
4. Contract: OpenAPI validation, schema snapshots and diff, consumer compatibility, signature fixtures.
5. E2E: React UI + API + IdP + DemoModule + workers via compose, Playwright.
6. Security and resilience: cross-tenant, tokens, SSRF, kill worker, replay, connection outage, concurrency.

## RED→GREEN→REFACTOR
- Viết executable acceptance test trước implementation của mỗi slice.
- Không thêm code chỉ để pass mocked happy path; chạy integration test với DB thật.
- Refactor giữ contract, migration and tests green.
- Mỗi PR có test evidence, API diff, migration notes, rollback steps.

## P0 acceptance checklist (bắt buộc)
- [ ] `tenant_A` không truy cập contact, tag, timeline, installation, event hoặc export của tenant_B.
- [ ] JWT sai issuer/audience, thiếu scope, hết hạn đều 401/403 chính xác.
- [ ] Contact create/update/delete-soft và tags/custom fields có audit và optimistic concurrency.
- [ ] Cùng ingress identity `(tenant,installation,source,event_id)` gửi 100 lần concurrent chỉ tạo 1 inbox row, 1 execution/matching rule revision và 1 logical timeline entry. Hai installations/sources dùng cùng event_id vẫn là hai logical events hợp lệ.
- [ ] Outbox crash before/after HTTP send không tạo double side-effect ở receiver.
- [ ] Module offline 30 phút vẫn không mất accepted event; replay tiếp tục từ checkpoint.
- [ ] Module register mới (DemoModule) không thay code CRM Core; menu, panel, trigger/action xuất hiện qua manifest.
- [ ] Gỡ module khóa actions mới và revoke secret, lịch sử không mất.
- [ ] Migrate database từ phiên bản trước lên phiên bản mới trên dữ liệu fixture; chạy rollback drill cho schema change.
- [ ] Từ empty environment Docker compose start, seeded admin login, E2E demo thành công.
- [ ] Logs có trace/correlation ID; không log token, secret hoặc PII không cần thiết.

- [ ] Restore backup+WAL trong isolated environment đo RPO <=15 phút/RTO <=4 giờ; reconcile external outboxes, replay không double effect; lưu timestamp/data-loss evidence. Chỉ đóng khi runtime drill pass.

## Contract gates
- OpenAPI parse + lint + breaking change detector (pin tool versions).
- Validate sample payloads against JSON Schema, negative cases for unknown/version mismatch.
- Generated TS client builds with `tsc --noEmit`, generated C# client compiles.
- Webhook fixture validates exact bytes/HMAC/timestamp/replay/idempotency.

## Load and chaos cases
- 50k contacts/tenant, 50 concurrent users; baseline before and after index changes.
- 10k inbound events with 10% duplicates, reorder, delays; verify exactly one logical projection per event ID.
- Kill worker between send and ack; drop Redis; disconnect external module; corrupt an isolated payload to DLQ; measure recovery.

## Go / No-Go
Any unremediated P0 vulnerability, data leakage, accepted event lost inside the documented durability failure envelope, duplicate external side effect or migration corruption = NO GO. Disaster recovery có measured RPO/RTO gate riêng; vượt mục tiêu cũng NO GO. Schema fixtures không thay runtime evidence.

Traceability và trạng thái: `03-ACCEPTANCE-MATRIX.md`. Suite hiện có: `python -m unittest discover -s tests/Contract -v` sau khi cài `requirements-contracts.lock`.
