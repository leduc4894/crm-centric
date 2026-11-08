# P0 traceability và evidence matrix

**Contract tested** chỉ nghĩa shape/fixture đã kiểm tra. Mọi `RT-*` là test ID dự kiến, chưa có executable implementation, chưa pass. Gate chung: tenant isolation ở API+SQL/RLS+worker+replay.

| Story | Contract / invariant | Contract evidence hiện có | Runtime test ID cần xây | Wave / trạng thái |
|---|---|---|---|---|
| US-01 tenant/invite | tenants/memberships/invites; token hash, expiry, verified email, single-use | required operations, typed requests/errors | RT-IAM-01 invite expiry/revoke/race; RT-TENANT-01 cross-tenant/RLS/pool | 1 / runtime OPEN |
| US-02 OIDC | issuer/sub khác contact, separate OIDC clients | architecture only | RT-OIDC-01 issuer/audience/expiry/alg/PKCE/logout; RT-SSO-01 | 1 / OPEN |
| US-03 contacts | CRUD/ETag/filter/paging; tenant+version predicate | If-Match/error/body negatives | RT-CRM-01 concurrent update/soft-delete/audit; RT-PERF-01 50k+50 users p95 | 2 / runtime OPEN |
| US-04 tags/fields | typed values, tenant constraints | OpenAPI validation | RT-CRM-02 cross-tenant/type/normalization/concurrency | 2 / runtime OPEN |
| US-05 timeline | unique tenant/contact/inbox/kind, provenance | data model/API shape | RT-EVT-01 duplicate/reorder/crash timeline | 2→3 / OPEN |
| US-06 install | manifest v2, publisher trust, hash/scopes approval, SSRF | descriptors/sample/hash pins/legacy reject | RT-MOD-01 signature/challenge/approval race; RT-SSRF-01 DNS/redirect/schema/action/private allowlist | 3 / runtime OPEN |
| US-07 ingest | bound credential, commit before ACK, raw hash conflict | HMAC known-answer/tampering, envelope/headers | RT-HOOK-01 rotation/window/mismatch; RT-EVT-02 100 concurrent duplicates; RT-EVT-03 two installs same ID; RT-EVT-04 commit/ACK failure | 3 / runtime OPEN |
| US-08 automation | bounded DSL, stable IDs, explicit trigger/action installations, typed external refs | DSL type/depth/binding negatives, API paths | RT-AUTO-01 semantics/quota; RT-AUTO-02 crash/revision/pause/loop; RT-ACTION-01 one receiver effect; RT-LINK-01 same ID/different entityType + two destinations + subject mismatch | 4 / runtime OPEN |
| US-09 outage/retry | durable queues, DLQ replay same identity | replay API/protocol design | RT-REC-01 offline 30min/reconnect; RT-REC-02 two workers/lease/crash after send | 3→5 / OPEN |
| US-10 disable | stop new sends, revoke, preserve in-flight/history | lifecycle/API design | RT-MOD-02 disable/uninstall/reactivate/cache invalidation | 3→4 / OPEN |
| US-11 UI extension | SSO, authorized panel, tenant cache key | panel schema/hash, descriptors | RT-PANEL-01 cross-tenant/cache/revoke/XSS/timeout; RT-SSO-01 | 3→5 / runtime OPEN |
| US-12 remote demo | independent process/DB/network, registry only | complete module fixtures | RT-E2E-01 install→SSO→link→event→tag+echo→duplicate→outage→disable without Core change | 5 / OPEN |
| NFR audit/errors | transaction audit, safe Problem/trace | all operation errors use Problem shape | RT-OBS-01 reason/correlation, no secret/PII, runtime audit | all / runtime OPEN |
| NFR DR/migration | RPO≤15min/RTO≤4h, expand-contract, replay | runbook/ADR migration | RT-DR-01 restore/reconcile; RT-MIG-01 prior-fixture upgrade/rollback | 5 / OPEN |

## Wave 0 tooling gates
- Executable: JSON Schema meta-validation; sample/negative validation; OpenAPI validation; current consumer expectations; known-answer HMAC; pinned dependencies và CI definition.
- OPEN: application scaffold/Compose cold start, .NET/React build, generated TS/C# SDK compile, automated baseline breaking-diff detector, dependency/secret/container scans và CI remote run.
- Chưa có consumer production; fixtures không thay compatibility proof cho released consumer. SDK compilation và breaking diff phải pin/gate trước release đầu.

## Evidence format
Ghi commit SHA, command, pinned image/tool versions, environment/resources, fixture seed, pass/fail, redacted trace IDs, SQL assertions và artifact path. Load test có p95/error rate/sample count/warmup/duration; DR có last recoverable commit timestamp/downtime để tính RPO/RTO. Không chỉ ảnh UI.

Không đóng wave/P0 bằng tài liệu này; cập nhật evidence sau mỗi wave exit và chạy suite theo AGENTS.md.
