# P0 access, state và concurrency decisions

## Tenant context và quyền
`X-Tenant-Id` chọn tenant cho authenticated request; backend xác minh membership `(issuer,subject,tenant)` active trước khi set transaction-local DB context. Service credential chỉ được tenant đã bind, header mismatch bị từ chối. Không header/body nào tự cấp quyền. List tenants chỉ trả memberships của principal; create tenant chỉ platform provisioner. Invite acceptance resolve tenant từ hashed single-use token và authenticated verified email; expiry 24h, revoke/accept atomic, không auto-link CRM contact.

| Role | P0 scopes |
|---|---|
| owner/admin | crm.contacts.read/write, crm.fields.manage, crm.identities.manage, team.manage, integrations.install, integrations.links.manage, integrations.replay, automation.manage, audit.read |
| agent | crm.contacts.read/write |
| operator | crm.contacts.read, automation.manage |
| integration service | installation grants cụ thể, không staff role hay team.manage |

`platform.tenants.create` chỉ platform operator ngoài tenant role. Agent/operator không tự cấp quyền. Không đổi owner qua membership PATCH, không xóa/demote owner cuối cùng. P0 không có owner-transfer API. Bulk tagging UI phải gọi các mutation có concurrency/limit; bulk API riêng chưa thuộc contract P0.

Invite POST trả token một lần cho authorized admin qua TLS để chuyển thủ công trong P0; chỉ lưu hash. Không token trong list/audit/logs; frontend không persist localStorage hoặc URL query. Trang nhận invite có thể đọc fragment rồi xóa trước khi POST accept body có redaction. Không triển khai email delivery provider trong P0. Một token chỉ được accept một lần bằng conditional transaction; mismatch verified email/expired/revoked không tạo membership.

## Contact và identity
Contact email nullable, không unique identity. Link `(tenant,issuer,subject)` tối đa một contact active; một contact có thể link nhiều identities. External source link unique `(tenant,installation,entity_type,external_id)`; conflicting relink trả 409, cần unlink/link có audit. Validate contact tenant và installation tenant trước link. Không FK xuyên bounded context; enforcement qua application port và reconciliation, tenant-safe lookup. Link không tự cấp membership hoặc quyền đăng nhập.

P0 external profile cardinality là 1:1 trong mỗi installation+entityType: thêm partial unique active `(tenant,installation,entity_type,contact_id)`. Nhờ đó action/panel resolve contact tới đúng một typed external record; conflicting second link trả 409 LINK_CARDINALITY_CONFLICT. Một contact vẫn có nhiều profiles ở installation/entityType khác. Multi-account cùng loại cần explicit selector/fan-out contract tương lai, không chọn record đầu tiên.

## Optimistic concurrency và audit
Contact GET/create/update trả strong ETag `"vN"`. PATCH/DELETE và contact properties/tags/fields/notes mutations yêu cầu If-Match; thiếu 428 PRECONDITION_REQUIRED, stale 412 VERSION_CONFLICT, wildcard/weak/malformed 400 PRECONDITION_INVALID. Update SQL predicate gồm tenant/id/version; tăng version atomic, audit và outbox cùng transaction. Tag PUT đã tồn tại có thể no-op sau kiểm tra version, không tạo event/audit mutation giả; retry stale vẫn 412 rồi client đọc lại.

Membership, installation và rule mutable control state cũng có version/ETag; published rule revisions không bị overwrite. Rule response và execution phân biệt internal UUID id với immutable public ruleId; API rule GET trả định nghĩa có ruleId/version, danh sách bổ sung internal id để tạo URL. Cursors opaque, ký hoặc validate server-side, bind tenant/filter/sort; contacts sort `(updated_at,id)`, timeline `(occurrence_time,id)` giảm dần. Pagination keyset không hứa snapshot khi đang ghi; backfill cần checkpoint riêng.

## Rule revision và replay
External trigger phải có `triggerInstallationId`; internal `crm.*` trigger không có field này. Publish resolve event descriptor từ installation đó, snapshot entityType và pinned event schema/hash; field/type validation dựa snapshot này. Match worker cả event type và verified installation, không chỉ type string. Remote action phải có `installationId` đích, built-in `crm.*` không được có; snapshot action endpoint/entityType/schema hashes/grants vào revision và action run. Hai installations cùng action ID không tạo ambiguity. Chưa có link đúng contact+destination installation+entityType thì terminal ACTION_CONTACT_UNLINKED, không tự chọn bản ghi hoặc copy link của trigger. Manifest upgrade thay pinned capability cần revalidate/republish rule trước dispatch, không âm thầm thay schema/endpoint của revision cũ.

Rule mới version 1, paused. Publish yêu cầu current+1 và If-Match, transaction giữ immutable revision. Khi event được resolve tới contact và worker lần đầu tạo execution, snapshot danh sách active rule revisions trong cùng transaction với enqueue action work. Retry/replay execution giữ revision cũ; không tạo execution mới chỉ vì rule đã publish revision khác. Pause ngăn execution mới và unsent dispatch; in-flight effect có thể hoàn tất như module disable. Resume chỉ cho work mới; work skipped cần explicit audited remediation, không âm thầm chạy.

Execution unique `(tenant_id,rule_id,rule_version,inbox_id)` cho P0 một contact/event. Link source là `(tenant,installation,descriptor.entityType,event.subject)`; subject bắt buộc, Demo externalEntityId phải bằng subject, không merge hai entityType cùng ID. P0 chỉ resolve một subject thành một contact; fan-out nhiều contact cần contract mới. Timeline unique `(tenant_id,contact_id,inbox_id,entry_kind)` khi inbox_id non-null. Action unique `(tenant_id,execution_id,step_no)`; action_run.id là remote idempotency key. Replay giữ inbox/execution/action IDs. Sau unlink/relink không chuyển lịch sử sang contact mới; replay giữ mapping snapshot, cần operator review nếu link revoked.

## DSL semantics
JSON Pointer chỉ bắt đầu `/data/`, escape ~0/~1 đúng RFC 6901, field thuộc pinned trigger schema allowlist; không wildcard, scripts hoặc dynamic network reference. `exists` true khi key tồn tại, kể cả null; missing=false. `eq` scalar strict type, không coerce string/number/bool; null chỉ bằng explicit null. `gt` finite numeric, missing/null/wrong-type=false. Reject nonfinite JSON numbers. `all`/`any` short-circuit; nonempty ≤10 children/group, ≤4 group levels, ≤64 total nodes, ≤10 actions/rule. Schema kiểm tra depth/shape; publisher runtime kiểm tra schema field types, node count và grants.

Actions chạy tuần tự; retryable failure dừng ở step đó, retry cùng identity, không chạy lại step succeeded; terminal failure dừng các step sau với skipped. Không rollback effect đã thành công; execution lưu partial outcome và reason. Built-ins `crm.addTag.v1` input `{tag: string 1..64}`, `crm.createNote.v1` input `{text: string 1..1000}`; reject unknown keys; remote input/output theo descriptor. Giới hạn mặc định 100 active rules/tenant, 1000 action starts/min/tenant; vượt quota giữ pending, metric/alert, không drop accepted work. Ngăn automation loop bằng causal depth ≤8; vượt ngưỡng terminal AUTOMATION_CAUSAL_LIMIT, audit và review.
