# P0 recovery/incident runbook baseline

Đây là procedure thiết kế, chưa được diễn tập. Trước public production, operator phải bổ sung environment inventory, owner/on-call, backup storage/key references và lệnh deploy/restore theo IaC thực tế; không đưa secrets vào repo/logs. Runtime tests RT-REC/RT-DR/RT-OBS trong acceptance matrix vẫn OPEN.

## Network outage / worker crash
1. Xác định tenant/installation bị ảnh hưởng qua lag, DLQ và redacted traces. Core readiness không phụ thuộc module health; không tắt toàn CRM chỉ vì module offline.
2. Kiểm tra accepted inbox/outbox đã commit, lease expiry, retry next_at và quota. Không sửa payload/ID hoặc xóa queue để làm dashboard xanh.
3. Khôi phục endpoint/credential đúng binding; thử health và một synthetic event ở test tenant. Cho worker retry bằng lease mới; receiver dedup cùng action identity kể cả crash-after-send.
4. DLQ replay qua authorized API với reason, giữ inbox/execution/action IDs. Schema/auth conflicts cần remediation trước replay. Đối chiếu counts: accepted = pending+unmatched+processing+succeeded+terminal, không đánh rơi status.
5. Lưu evidence offline ≥30min, reconnect, queue drain và one-logical-effect SQL/receiver assertions.

## Credential compromise / security incident
1. Operator ghi incident ID/correlation và phạm vi đã xác minh; disable installation, revoke compromised webhook/OAuth keys ngay, không chờ overlap.
2. Ngăn claim/send mới, invalidate panel cache; ghi nhận HTTP đã in-flight có thể hoàn tất. Bảo toàn redacted audit/backup, không export raw PII vào ticket.
3. Kiểm tra credential binding, tenant access và action receipts; xử lý containment theo phạm vi thực tế. Tenant suspect request trước auth không được dùng để gán ownership.
4. Cấp fresh credentials qua secret manager, verify publisher/snapshot/scopes/challenge; operator re-enable có audit. Không tự replay toàn bộ terminal/skipped work.
5. Post-incident ghi exposure window, effects, remediation và test tái hiện; communications/privacy escalation theo policy tổ chức trước production.

## Backup / restore drill (isolated environment, hàng tháng)
1. Chọn synthetic fixture có tenants A/B, memberships, links, immutable rule revisions, accepted events, pending/DLQ và remote action receipts. Ghi timestamp các committed markers và expected effects.
2. Xác minh backup full + WAL archive ≤15min lag, integrity, encryption-key restore và retention; backup phải bao gồm inbox/dedup tombstones, registry snapshots, rule/action state, audit. DemoModule DB/outbox/action ledger cũng có backup policy riêng; không shared DB.
3. Ghi incident start time; cô lập môi trường đích khỏi production egress. Restore full+WAL tới recovery target; xác minh actual last recoverable marker. Mặc định giữ API read-only và outbound workers paused.
4. Kiểm tra schema version, RLS/app role (không dùng migration role cho smoke), tenant isolation, links, secrets references và IdP binding. Không dùng credential restore cũ nếu đã revoked sau backup; revalidate against current secret manager revocations.
5. Tính RPO = incident reference time − latest recovered committed marker; RTO = validated service restoration time − incident start. Đạt ≤15min/≤4h bằng số đo, không dựa backup schedule.
6. Lập uncertain interval giữa restore point và sự cố. Inbox/action IDs có thể mất ở interval này: KHÔNG tự replay rồi tạo IDs mới. Reconcile sender outboxes và authoritative receiver receipts/audit để phục hồi original inbox/rule/action identities/outcomes. Nếu không xác định chắc action đã có effect hay chưa, quarantine và manual review; không retry bằng key mới. Receiver không được prune idempotency ledger trước khi recovery window đóng.
7. Retry work đã xác minh bằng original IDs/keys; thử crash-after-send và duplicate cả trước/sau restore point. Assert không duplicate logical effects; unknown/quarantined work phải hiện rõ, không tính là recovery hoàn chỉnh.
8. Chỉ bật writable traffic/outbound sau counts reconciliation, tenant/security checks và hết unresolved accepted-work gaps thuộc recovery gate. Lưu commands redacted, versions, RPO/RTO, SQL/receiver assertions và artifact paths. Drill fail hoặc gap chưa resolve = NO GO.

## Migration/rollback
Expand schema trước, test prior-version fixture, backfill/check constraints rồi deploy compatible readers/writers. Khi lỗi, pause dispatch nếu contract incompatible, rollback image chỉ khi schema còn compatible; destructive changes dùng forward-fix/restore procedure đã diễn tập. Không sửa trực tiếp audit/inbox để giả completion. Secret rotation/revocation không bị undo bởi application rollback.
