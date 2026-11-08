# Eventing, Synchronization, Automation

## Integration envelope
Use CloudEvents-aligned metadata: `specversion`, `id`, `source`, `type`, `subject?`, `time`, `datacontenttype`, `dataschema?`, `data`. Transport headers contain authenticated installation/tenant, not blindly trusted payload context.

## Inbound path
1. Validate transport credential/signature, timestamp, content-type, size and rate limits.
2. Resolve installation and tenant from credential; validate event schema/version.
3. Insert inbox record with `(tenant_id, installation_id, source, event_id)` unique; store raw payload encrypted/limited retention.
4. Transaction: insert accepted inbox and derived domain command/work item. Return accepted only once committed.
5. Worker maps external subject to contact, updates CRM projection and append immutable timeline.
6. Unmatched subject → review queue; do not silently create/merge identity.

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
- Execution identity: `(tenant_id, rule_id, rule_version, event_id)` unique; each action run idempotency token stable.
- Interpreter supports equality/exists/greater-than on validated schema fields, AND/OR, max depth and execution quota. No arbitrary JavaScript, SQL or C# expressions.
- Actions P0: CRM add-tag, create timeline note, remote demo action. Results: pending/succeeded/retryable_failed/terminal_failed/skipped.
- Contact-centric state keyed by contact_id where known; one event can trigger many independent contacts/rules; all runs auditable.

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
