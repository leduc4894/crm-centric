# Eventing, Synchronization, Automation

## Integration envelope
Use CloudEvents-aligned metadata: `specversion`, `id`, `source`, `type`, `subject?`, `time`, `datacontenttype`, `dataschema?`, `data`. Installation/key headers là untrusted selectors cho đến khi credential binding/signature verify; chỉ binding cấp tenant context. Không suy tenant authorization từ payload/source. Normative transport: `../contracts/WEBHOOK-PROTOCOL.md`.

## Inbound path
1. Validate transport credential/signature, timestamp, content-type, size and rate limits.
2. Resolve installation and tenant from credential; validate event schema/version.
3. Insert inbox record with `(tenant_id, installation_id, source, event_id)` unique; store raw payload encrypted/limited retention.
4. Transaction: insert accepted inbox and derived domain command/work item. Return accepted only once committed.
5. Worker maps external subject to contact, updates CRM projection and append immutable timeline.
6. Unmatched subject → review queue; do not silently create/merge identity.

## Contact concurrency — SDK và workers (Wave 3–4)

Quyết định hướng thiết kế được người dùng đồng ý; đây là yêu cầu cho plan Wave 3/4, **chưa phải bằng chứng worker, API key hay n8n node đã được triển khai**. Contract contact hiện tại và ADR-020/021 tiếp tục là nguồn chuẩn về headers và status.

- Client bên ngoài gọi contact mutation qua API phải lấy strong ETag hiện tại và gửi `If-Match`; credential/API key xác thực và cấp scope, không bỏ precondition. Conditional POST upsert phải kèm cả `X-Contact-Id` và `If-Match` khớp cùng active contact; không tự chuyển intent sang contact khác khi email được tái sử dụng. Credential chỉ cho tenant và scopes đã bind; không tin tenant từ body hoặc header để tự cấp quyền. Luồng credential cụ thể sẽ được thiết kế trong Wave 3.
- SDK phải giữ ETag và Problem/reason/correlation, hỗ trợ client gửi preconditions và phân biệt 400/412/428. Không tự overwrite hoặc retry mù quáng trên `412`; cách dùng thuận tiện không được thay đổi contract hay ẩn conflict.
- Webhook publisher không cần biết contact ETag chỉ để gửi event. Sau khi event được verify, lưu inbox/work bền vững và ACK theo protocol; integration worker resolve tenant/contact theo mapping đã xác minh, dùng version predicate tương đương ETag khi gọi CRM qua port nội bộ, hoặc If-Match nếu gọi HTTP. Dù đi qua port, không bỏ qua tenant, optimistic concurrency, audit/outbox hay API boundary của module ngoài.
- Khi version conflict, worker đọc lại state tenant-scoped và chỉ retry có giới hạn nếu action/ownership policy cho phép chứng minh intent vẫn đúng; với add-tag, kiểm tra tag đã có và coi no-op là thành công khi phù hợp, không phát sinh audit/event giả. Thay đổi có thể ghi đè dữ liệu như email/phone cần ownership/conflict policy rõ; nếu không xác định an toàn, dừng với reason token, correlation ID và trạng thái cần xử lý, không mặc định last-write-wins. Số lần retry, backoff và đường xử lý cụ thể phải được chốt trong plan tương ứng.
- Idempotency khác optimistic concurrency: dedup inbox/action bằng identity ổn định và giữ identity khi retry/replay; ETag không ngăn event hoặc side effect lặp. Không tự retry mutation HTTP sau timeout/commit không rõ kết quả nếu chưa có cơ chế reconciliation/idempotency chứng minh an toàn.
- Wave 3 kiểm thử credential binding, cross-tenant, concurrent event/contact edits, conflict, replay và no-op bằng PostgreSQL thật; Wave 4 kiểm thử add-tag, conflict không an toàn, crash/replay và một logical action. Contract/schema changes tuân ADR, migration và consumer tests. Node n8n là consumer API độc lập có thể được thiết kế sau khi credential contract ổn định; chưa thuộc scope triển khai Wave 2 và không được giả định đã hỗ trợ API key.

## Outbound path
1. Business transaction changes local state and inserts Outbox in same DB transaction.
2. Dispatch worker claims with SKIP LOCKED, leases, retry with capped exponential backoff + jitter.
3. Delivery signed per receiver, idempotency key stable; store attempts and next_attempt_at.
4. Success response only on durable acceptance by recipient. Failed attempts end in DLQ and alert; operator can replay.
5. Metrics: accepted_events, duplicated_events, dispatch_lag, retries, dead_letters, signature_failures, unmatched_identity, projection_drift.

## Initial sync / reconcile
- Cursor-paginated full backfill from authoritative external APIs + checkpoint token, snapshot semantics if provider supports.
- Incremental pull by `updated_since` / cursor; use webhooks for low-latency, reconcile at intervals.
- Deletions represented by tombstone/consent-aware removal; every field has source-of-truth policy.
- Detect gaps with sequence/checkpoint/hash where available; bulk re-fetch after downtime; no lost events by policy.
- Conflict resolution: ownership-first; CRM may own marketing phone but not LMS progress or financial ledger. Avoid blind last-write-wins.

## Automation P0
- `rule`: tenant-scoped, versioned `triggerEventType`, predicate DSL with allowlist, list of built-in/registered actions, status.
- Execution identity: `(tenant_id, rule_id, rule_version, inbox_id)` unique cho P0 single-contact subject. Inbox có stable internal UUID, ingress unique `(tenant_id,installation_id,source,event_id)`. Không dedup downstream chỉ bằng raw event_id. action_run.id là remote idempotency key bất biến.
- Interpreter supports equality/exists/greater-than on validated schema fields, AND/OR, max depth and execution quota. No arbitrary JavaScript, SQL or C# expressions.
- Actions P0: CRM add-tag, create timeline note, remote demo action. Results: pending/succeeded/retryable_failed/terminal_failed/skipped.
- P0 map một external subject tới một contact; nhiều rules có thể match độc lập. Multi-contact fan-out cần contract tương lai, không implicit email matching. Snapshot mapping và active rule revisions ở lần materialize execution đầu tiên; replay giữ nguyên. Xem `07-P0-ACCESS-AND-STATE.md` cho DSL, quotas, lifecycle và partial failures.

## P1 Temporal
- Use .NET Temporal SDK for durable contact-centric long-running workflows (delay, branch, cancel, re-entry); version workflow definitions/worker code and define migration behavior.
- Keep rule definitions in DB; Temporal is execution engine, not CRM source of truth. Handle duplicate starts and idempotent Activities.
- Never start one long-lived Temporal workflow per historical event without retention/sizing strategy; estimate concurrency and Continue-As-New as needed.

## Invariants
- At-least-once, not exactly-once delivery over network.
- Idempotency at every boundary.
- Acknowledgement follows durable commit.
- No external side effect inside DB transaction.
- All terminal error paths emit reason tokens and correlation IDs.
