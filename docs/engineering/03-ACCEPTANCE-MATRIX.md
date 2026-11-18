# P0 traceability và evidence matrix

> **Phạm vi CI MVP 2026-11-12:** áp dụng [quyết định Plan 4](09-WAVE-0-CI-MVP-SCOPE.md). W0P4 cũ có mục chuyển sang hardening; không suy diễn Wave 0 hoặc P0 đã pass. Các invariant và RT-* trong bảng vẫn giữ nguyên.

**Contract tested** chỉ nghĩa shape/fixture đã kiểm tra. Các `RT-*` là acceptance test ID; một số phần đã có executable tests như evidence bên dưới, nhưng các ID vẫn OPEN cho tới khi đủ phạm vi. Gate chung: tenant isolation ở API+SQL/RLS+worker+replay.

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
- Plan 1 local runtime evidence: .NET scaffold, locked restore/Release build, PostgreSQL migrations/privileges, operational health/errors và process smoke; xem [verification](05-WAVE-0-PLAN-1-VERIFICATION.md). Plan 1 integration đã ghi ở handoff.
- Plan 2 local runtime evidence: SDK-free exact-SHA clean archive up/smoke/down, pinned images,
  isolated Keycloak, safe JSON/Activity, fake OIDC, real Compose failure/recovery/secret/lock tests.
  [W0P2-01→12 mapping và commands/counts](06-WAVE-0-PLAN-2-VERIFICATION.md#task-8--clean-source-evidence-2026-11-10).
  Local Plan 2 final review Approved (0 Critical/Important);
  [review và scope rulings](../superpowers/reviews/2026-11-10-wave-0-plan-2-final-review.md).
  Không đồng nghĩa Wave 0/P0 hoàn tất.
- Plan 3 local toolchain evidence (Wave 0): React/Vite workspace có typecheck/lint/test/build,
  bốn generated TS/C# SDK compile và đóng gói, consumer độc lập không phụ thuộc CRM server,
  breaking/security/schema gate tại base Git, aggregate gate fail-closed và negative cases.
  [W0P3-01→12 mapping và commands/counts](07-WAVE-0-PLAN-3-VERIFICATION.md).
  Đây là bằng chứng local của toolchain, không phải runtime acceptance.
- Plan 4 local CI/security implementation and [W0P4-01→12 evidence](08-WAVE-0-PLAN-4-VERIFICATION.md): actual scans ran and rejected current inputs; backend/Compose original jobs failed. Independent trust, source/tool coverage, security/image findings, final-SHA full jobs and remote acceptance remain OPEN/BLOCKED. Governance UNKNOWN/OPEN; no enforcement claim.
- OPEN: passing dependency/secret/container gates, remote CI run, và toàn bộ RT-* runtime bên dưới.
  Runtime sentinel audits đã pass local; không thay thế CI scanner gate.
- Plan 1 không đóng RT-* ở trên: OIDC/tenant/business/remote E2E/restore vẫn OPEN; database outage/recovery không phải DR drill.
- Chưa có consumer production; fixtures không thay compatibility proof cho released consumer. SDK compilation và breaking diff phải pin/gate trước release đầu.

## Evidence format
Ghi commit SHA, command, pinned image/tool versions, environment/resources, fixture seed, pass/fail, redacted trace IDs, SQL assertions và artifact path. Load test có p95/error rate/sample count/warmup/duration; DR có last recoverable commit timestamp/downtime để tính RPO/RTO. Không chỉ ảnh UI.

Không đóng wave/P0 bằng tài liệu này; cập nhật evidence sau mỗi wave exit và chạy suite theo AGENTS.md.

## Wave 1 identity and tenant foundation — local evidence

Implementation commits through `5ec82de` add bearer validation, restricted
membership lookup, verified tenant transactions and the contract-compatible tenant
list. Public OpenAPI and JSON Schema files have no diff from `40bc290`. PostgreSQL
integration tests exercise RLS, connection-pool reset, membership revocation and
role privilege checks; OIDC tests use a local fake issuer, including key rotation
and transport rejection. Focused Task 4 regression: 39 passed, 0 failed, 0 skipped;
contract shape tests: 27 passed, 0 failed, 0 skipped. Full Release integration suite on `d93b738`: 118 passed, 0 failed, 0 skipped (36.3245 minutes), using native SDK 10.0.403 and PostgreSQL Testcontainers. These are partial evidence,
not complete RT-OIDC-01 or RT-TENANT-01: browser PKCE/logout, real-client audience,
worker/replay isolation and full Wave 1/P0 acceptance remain open. The SQL bootstrap
trust boundary and migration/rollback are documented in
[IdP configuration](../operations/05-IDP-CONFIGURATION.md#wave-1-api-identity-and-tenant-foundation).

Local Wave 0 workflow rehearsals with a simulated push identity (base `40bc290`,
candidate `b7a8b21`) reported backend 127/127, contracts-sdk 88/88, web-browser
58/58 and security 1/1 pass with no skips. The Compose job **failed** with
`CI_COMMAND_FAILED`; a diagnostic run and a focused rerun reproduced
`LOCAL_START_TIMEOUT` in the parallel-project isolation test. A later simulated
push rehearsal on candidate `78038b5` passed Compose 16/16, 0 skipped, in
1794.5 seconds. Both runs used real local Compose; no timeout or runtime change was
made between failure and success. The timeout cause remains undetermined and the
test remains a reliability concern. These local rehearsals do not replace a hosted
CI run on the final candidate.

## Wave 1 tenant lifecycle — local evidence

On code commit `0b79061`, tenant provisioning and membership read/update use
transactional audit and tenant-scoped authorization; a separately verified
staff binding is required for `team.manage`. The application database credential
is a trusted API credential and does not itself authenticate a caller-supplied
issuer/subject. Real PostgreSQL focused regressions reported 37/37 for lifecycle,
migrations, privilege and authorization; 22/22 for staff authorization and
tenant isolation; 13/13 for provisioning and existing tenant endpoints; and
38/38 for membership endpoints, authorization and isolation, with an additional
signed service-owner denial test 1/1. Review fixes passed 7/7, including a real
reader/writer deadlock and backend termination during commit; loss of a successful
commit acknowledgement was not simulated. Nine real HTTP response examples were
validated against the existing OpenAPI membership schemas. Some individual tests
were added after implementation; complete per-behavior RED evidence is not claimed.

The local security rehearsal on `81ecf23` failed `CI_SCAN_FINDINGS`: the required
vulnerability scanner identified `CVE-2026-112366` in `picomatch` 4.0.7 as HIGH,
with no fixed version in its result; gitleaks passed. [NVD](https://nvd.nist.gov/vuln/detail/CVE-2026-112366)
reports affected versions through 4.0.7 and the [maintainer issue](https://github.com/micromatch/picomatch/issues/247)
remains open. Advisory sources disagree; a separate Snyk advisory has been revoked.
No exception or policy relaxation is implied. The earlier run without the prepared
scanner path failed `CI_SCAN_INCOMPLETE`. These local tests do not close RT-IAM-01,
RT-TENANT-01, Wave 1, P0 or hosted CI; invites, browser sessions and wider tenant
and recovery acceptance remain open.

Prepared full Contract unittest discovery passed 88/88 with no skips in 1439.065s;
it began on `0b79061` and continued during a later source fix, so it does not prove
a complete final-candidate gate. The earlier unprepared run failed 15 and skipped 7;
its failed result is retained in the handoff. The final-code security rehearsal
remains blocked as above.

Full Release integration run started on `0b79061` finished FAIL: 180 passed,
1 failed, 0 skipped (50.474 minutes). `Parallel_projects_do_not_share_resources`
failed `LOCAL_START_TIMEOUT` while starting parallel Compose projects. Source
changed during the run; it is not a final-tree result. The failure reproduces
an earlier intermittent timeout; root cause has not been established.

A separate local simulated `contracts-sdk` workflow rehearsal on `81ecf23` passed
88/88, 0 skipped in 2507.26s (`CI_JOB_COMPLETE`); it is not hosted CI. A final-tree
non-Compose integration regression is in progress and has reported a Keycloak
management-health startup failure; no final count is claimed yet.

Final-code Release non-Compose regression on `81ecf23` failed: 165 passed, 1 failed,
0 skipped in 16.7393 minutes. `Keycloak_private_files_and_data_work_as_nonroot_with_management_health`
failed because management health did not become ready. Startup log capture contained
no ERROR, Exception, OOM or startup-complete markers; resource contention while the
contract/browser rehearsal ran is possible but not established as the cause.

Sequential focused diagnostics on `81ecf23`: Keycloak management-health test
passed 1/1 (55.36 seconds) after the non-Compose failure; parallel Compose project
test failed again 0/1 with `LOCAL_START_TIMEOUT` (3.2358 minutes). An independent
two-stack diagnostic reproduced one timeout: both databases became healthy,
both bootstrap/migration containers exited 0 and both APIs became healthy;
one Keycloak exited 137 with Docker `OOMKilled=true`, while the other eventually
became healthy. This establishes the cause for that diagnostic instance only,
not every previous timeout. No timeout, runtime or security policy was changed.

A subsequent sequential local `backend` workflow rehearsal on `1ca5f3a`
(code unchanged from `81ecf23`; intervening commit is documentation) passed
191/191 with 0 skipped in 848.16 seconds. The earlier 165/166 non-Compose failure
remains recorded; the successful rerun does not establish its cause. This local
simulation does not replace hosted CI.

A sequential local `web-browser` rehearsal on `1ca5f3a` failed with
`CI_COMMAND_FAILED` / `PLAN3_SOURCE_MUTATED` after 310.21 seconds, with no case
counts. Documentation was edited during that rehearsal; a separate rerun on a
clean committed candidate is required. The failure is retained as evidence.
