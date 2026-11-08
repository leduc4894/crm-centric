# Logical Data Model / PostgreSQL

## Tables P0
| Schema.Table | Key fields | Constraints / notes |
|---|---|---|
| platform.tenants | id, name, status, created_at | global unique id |
| platform.memberships | tenant_id, id, issuer, subject, role, status | unique `(tenant_id,issuer,subject)` |
| platform.roles_permissions | tenant_id, role, scope | deny by default |
| crm.contacts | tenant_id, id, display_name, email_normalized, phone, version, created_at, updated_at, deleted_at | email not globally unique, optimistic concurrency |
| crm.tags | tenant_id, id, name, normalized_name | unique tenant/name |
| crm.contact_tags | tenant_id, contact_id, tag_id | unique tuple |
| crm.custom_field_defs | tenant_id, id, key, data_type, schema_json, status | versioned validation |
| crm.contact_field_values | tenant_id, contact_id, field_id, value_json | validate type, add indexes only as needed |
| crm.contact_timeline | tenant_id, id, contact_id, source, type, occurrence_time, payload_summary | append-mostly, retention |
| integration.module_catalog | module_id, version, manifest_hash, schemas | global metadata |
| integration.installations | tenant_id, id, module_id, version, base_url, state, secret_ref | tenant isolated |
| integration.external_identity_links | tenant_id, installation_id, entity_type, external_id, contact_id, status | unique source key; audit links |
| integration.inbox | tenant_id, installation_id, source, event_id, type, payload, received_at, status | unique dedup tuple |
| integration.delivery_attempts | tenant_id, outbox_id, endpoint, status, attempt, next_at | retry trace |
| outbox.events | tenant_id, id, type, payload, status, lease_until, attempts | atomic with business writes |
| automation.rules | tenant_id, id, version, trigger, condition_json, actions_json, status | immutable published revisions |
| automation.executions | tenant_id, id, rule_id, rule_version, source_event_id, status | unique tuple |
| automation.action_runs | tenant_id, execution_id, step_no, idempotency_key, status | unique idempotency key |
| audit.entries | tenant_id, id, actor, action, target, diff_redacted, timestamp, correlation_id | append-only API |

## Indexing & growth
- `(tenant_id, updated_at, id)` contact paging, `(tenant_id, email_normalized)` for candidate lookup (not uniqueness assumption), `(tenant_id, contact_id, occurrence_time desc)` timeline.
- Delivery ready index `(status,next_at)`; partial indexes for pending rows; add partitioning only after benchmarks.
- PII in JSON payloads: minimize/redact/encrypt; retention by classification, data export/delete plan.

## RLS notes
- RLS policy binds tenant to secure transaction-local setting; do not use a raw user-supplied header as trust anchor.
- Migration role separated from application role; app user no BYPASSRLS.
- Every connection pooling path initializes and clears tenant context; tests simulate cross-tenant access and missing tenant.

## Migrations
- Migration per schema/module; additive first, backfill, new code reads both, finally remove old; rollback plan for destructive operations.
- Never share direct FK to LMS/Trading/Backcom external IDs; use mapping table.
