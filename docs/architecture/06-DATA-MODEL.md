# Logical Data Model / PostgreSQL — P0 baseline

Đây là logical model cần migration/Testcontainers ở từng wave, chưa có DB đã deploy. Mọi tenant-bound table có composite unique `(tenant_id,id)` nếu dùng UUID id; same-context FK phải gồm tenant. Không FK/DbContext xuyên domain module/external DB.

## Tables P0
| Schema.Table | Key fields | Constraints / notes |
|---|---|---|
| platform.tenants | id, name, status, created_at | global unique id |
| platform.memberships | tenant_id, id, issuer, subject, role, status, version | unique `(tenant_id,issuer,subject)`; last owner protected |
| platform.invites | tenant_id, id, email_normalized, role, token_hash, expires_at, accepted_at, revoked_at | unique token_hash; hash-only storage; atomic single-use transition |
| platform.roles_permissions | tenant_id, role, scope | deny by default |
| crm.contacts | tenant_id, id, display_name, email_normalized, phone, version, created_at, updated_at, deleted_at | email not globally unique, optimistic concurrency |
| crm.identity_links | tenant_id, id, contact_id, issuer, subject, linked_at, revoked_at | partial unique active `(tenant_id,issuer,subject)`; explicit audit, không tự tạo membership |
| crm.tags | tenant_id, id, name, normalized_name | unique tenant/normalized_name |
| crm.contact_tags | tenant_id, contact_id, tag_id | unique tuple |
| crm.custom_field_defs | tenant_id, id, key, data_type, required, schema_version, status | unique tenant/key; P0 definition immutable sau creation |
| crm.contact_field_values | tenant_id, contact_id, field_id, value_json | validate type, add indexes only as needed |
| crm.contact_timeline | tenant_id, id, contact_id, inbox_id?, entry_kind, source, type, occurrence_time, payload_summary | partial unique `(tenant_id,contact_id,inbox_id,entry_kind)` khi inbox_id non-null |
| integration.module_catalog | module_id, version, manifest_hash, publisher_key_id, schemas | global verified immutable metadata; no tenant secrets |
| integration.installations | tenant_id, id, module_id, module_version, manifest_hash, snapshot_json, state, version, last_health_at | control version/ETag khác module_version; immutable snapshots |
| integration.credential_bindings | tenant_id, installation_id, key_id, secret_ref, status, not_before, not_after | unique `(installation_id,key_id)`; narrow restricted auth lookup |
| integration.external_identity_links | tenant_id, id, installation_id, entity_type, external_id, contact_id, version, revoked_at | partial unique active source key và `(tenant_id,installation_id,entity_type,contact_id)` cho P0 1:1; CRM checked through tenant-safe port |
| integration.inbox | tenant_id, id, installation_id, source, event_id, payload_hash, type, payload, received_at, status, next_at, attempts, lease_until | unique dedup tuple; id internal stable UUID |
| integration.inbox_work | tenant_id, id, inbox_id, status, lease_until, lease_token, next_at, attempts | unique `(tenant_id,inbox_id)`; atomic with accepted inbox |
| integration.unmatched_events | tenant_id, id, inbox_id, entity_type, external_id, status, mapping_snapshot, resolved_at | unique `(tenant_id,inbox_id)`; resolution requeues original identity |
| integration.delivery_attempts | tenant_id, outbox_id, endpoint, status, attempt, next_at | retry trace |
| integration.dead_letters | tenant_id, id, work_kind, work_id, installation_id, reason_token, failed_at, replayed_at | replay same work identity, không tạo logical event mới |
| outbox.events | tenant_id, id, owner_module, type, payload, destination_id, status, next_at, lease_until, lease_token, attempts | atomic business writes; stable delivery `(tenant_id,id,destination_id)` |
| automation.rules | tenant_id, id, public_rule_id, current_revision, status, version | unique tenant/public_rule_id; version là control-state ETag |
| automation.rule_revisions | tenant_id, rule_id, revision, trigger_installation_id?, trigger_schema_hash, trigger, condition_json, actions_json, capability_snapshots, created_at | unique tuple; immutable published definition, destinations/entity types/schemas pinned |
| automation.executions | tenant_id, id, rule_id, rule_version, inbox_id, contact_id, mapping_snapshot, status | unique `(tenant_id,rule_id,rule_version,inbox_id)` |
| automation.action_runs | tenant_id, id, execution_id, step_no, destination_installation_id?, entity_type, external_id, input_snapshot, capability_snapshot, status, next_at, lease_until, lease_token | unique `(tenant_id,execution_id,step_no)`; id = remote Idempotency-Key |
| audit.entries | tenant_id, id, actor, action, target, diff_redacted, timestamp, correlation_id | append-only API |

## Indexing & growth
- `(tenant_id, updated_at, id)` contact paging, `(tenant_id, email_normalized)` for candidate lookup (not uniqueness assumption), `(tenant_id, contact_id, occurrence_time desc)` timeline.
- Delivery ready index `(status,next_at)`; partial indexes for pending rows; add partitioning only after benchmarks.
- PII in JSON payloads: minimize/redact/encrypt; retention by classification, data export/delete plan.

## Transaction và worker ownership
- Business owner appends outbox/audit through technical infrastructure ports, same connection/transaction as mutation; explicit technical exception của ADR-008, không sửa domain tables module khác.
- Inbox ingest atomic với inbox_work. Sau đó idempotent commands qua ports: CRM dedups timeline by inbox ID; Automation dedups executions/actions. Crash giữa modules được khôi phục bằng retry; không distributed transaction.
- SKIP LOCKED claims dùng expiring lease và fencing lease_token; update outcome chỉ khi token còn thuộc worker. Network vẫn có thể duplicate, receiver transactional idempotency bắt buộc. Không giữ DB transaction qua HTTP.
- Timeline giữ occurred_at, received_at/provenance; arrival order không phải business order. Mutable external facts update theo source version/checkpoint; nguồn thiếu version dùng authoritative reconciliation, không blind last-write-wins.
- Full event payload mặc định 30 ngày sau terminal resolution, không prune work đang pending/unmatched/retry/DLQ; tombstone theo webhook protocol. Soft-delete không tương đương purge PII; legal hold/deletion/backup policy phải chốt trước real-data deployment.

## RLS notes
- RLS policy binds tenant to secure transaction-local setting; do not use a raw user-supplied header as trust anchor.
- Migration role separated from application role; app user no BYPASSRLS, không owner tables; FORCE ROW LEVEL SECURITY cho core tenant tables.
- Every connection pooling path initializes and clears tenant context; tests simulate cross-tenant access and missing tenant.
- Pre-auth credential lookup dùng minimal restricted function/role chỉ trả binding/secret reference/status; không global bypass để query domain rows. Sau verify mới mở tenant transaction. Maintenance/restore jobs có audited privileged policy riêng.
- Invite acceptance đã authenticated nhưng chưa biết tenant cũng dùng narrow hash-token lookup chỉ trả binding/expiry/status/expected verified email; sau validation mới mở tenant transaction và conditional accept. Không cấp domain-data bypass cho caller có raw invite token.

## Migrations
- Migration per schema/module; additive first, backfill, new code reads both, finally remove old; rollback plan for destructive operations.
- Never share direct FK to LMS/Trading/Backcom external IDs; use mapping table.
- Gói baseline chưa có DB migration; constraints/indexes phải kiểm chứng với collision fixtures và rollback/forward-fix khi triển khai từng wave.
