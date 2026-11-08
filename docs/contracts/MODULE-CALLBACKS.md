# Remote action, panel và manifest semantics P0

## Validation ngoài JSON Schema
JSON Schema chỉ kiểm tra shape/version/bounds. Installer còn phải kiểm tra: ID descriptor không trùng; mỗi requiredScope thuộc permissions khai báo và grants được duyệt; schema URI/hash/version tồn tại và khớp bytes; event type đúng module namespace; platform API range P0 chỉ hỗ trợ `^1.0`. Không tải schema theo URL trong từng request.

Schema và manifest được snapshot immutable khi install; hash SHA-256 tính trên exact fetched bytes. HTTPS URL, DNS resolution, redirect, response size và egress checks áp dụng cho manifest, schema URI, navigation, health, panel và action — không chỉ manifest ban đầu. Mỗi outbound call phải kiểm tra lại địa chỉ được resolve; tắt automatic redirects hoặc validate từng hop. Default same origin với baseUrl; origin khác cần explicit tenant-admin egress approval trong installation snapshot. Approved private/on-prem network dùng operator-managed allowlist; tenant admin không được tự allow metadata/loopback/link-local.

Mỗi event/action/panel descriptor bắt buộc có `entityType`. P0 event dùng `subject` bắt buộc làm authoritative external ID; không suy từ email hoặc arbitrary field. Demo `data.externalEntityId` phải bằng subject; mismatch trả 400 EVENT_SUBJECT_MISMATCH trước acceptance (cross-field equality là runtime validation, không claim JSON Schema enforce). Link lookup luôn `(tenant,installation,descriptor.entityType,subject)`. Hai entity types dùng cùng external ID không được gộp. P0 handshake bind source chính xác `/modules/{moduleId}/installations/{installationId}`; sources khác cần explicit versioned contract, không lấy tenant từ source string.

Manifest trust P0: không public marketplace. Operator cấu hình publisher trust store (moduleId, Ed25519 public key ID, allowed origins). URL installation chỉ nhận queryless/fragmentless manifestUrl; platform fetch manifest và sidecar tại `manifestUrl + ".sig.json"` với cùng SSRF/TLS policy, timeout 5s, manifest ≤256 KiB, sidecar ≤4 KiB. Sidecar theo `publisher-signature.schema.json`: algorithm Ed25519, keyId, signature base64url không padding (64 decoded bytes, require canonical encoding). Chữ ký trên exact manifest bytes, không JSON reserialization. Không nhận arbitrary signature URL từ manifest. P0 chỉ hỗ trợ URL flow trong API; upload metadata là roadmap, không bypass publisher verification.

Signature nằm ngoài manifest để không self-sign. Lookup untrusted moduleId/keyId chỉ chọn public trust entry; phải verify signature trước register/approve. Unknown publisher/key/hash/signature fail closed; key rotation/revocation do operator quản lý và audit. Manifest upgrade chỉ chấp nhận module release version tăng, snapshot/hash mới cần approval khi scopes/origins thay đổi; không replay downgrade manifest cũ. Tenant approval không thay publisher verification; handshake chỉ chứng minh endpoint giữ credential. Wave 3 phải pin supported Ed25519 library và chạy real verifier consumer tests, không tự implement crypto.

## Remote action request/response
Action descriptor endpoint nhận POST HTTPS; OAuth client credentials token audience của DemoModule, credential bind đúng tenant/installation, least scopes. Không forward end-user token. Trước claim và ngay trước send, kiểm tra installation active/degraded, rule status và action grants. Không gọi network trong DB transaction.

Headers: `Authorization: Bearer ...`, `Idempotency-Key` là UUID action_run.id bất biến, `X-Correlation-Id` server validated. Body strict JSON:

```json
{
  "actionId": "demo.echo.v1",
  "actionRunId": "53c174ef-9701-4ebd-9b23-9c60627a8488",
  "contactRef": {"entityType": "demo.member", "externalId": "demo-member-001"},
  "input": {"message": "milestone reached"}
}
```

`contactRef` là typed external reference đã link dưới action destination installation; entityType phải bằng descriptor, không suy từ email và không tái dùng link của trigger installation. Không gửi tenant_id làm trust anchor. Wrapper theo `remote-action-request.schema.json`; input còn phải validate pinned action schema. Receiver xác định tenant từ service credential; validate action grant, Idempotency-Key bằng actionRunId và contactRef ownership. Lưu `(credential installation binding, idempotency_key, request_hash, result)` và business effect cùng local transaction trước 200. Cùng key/hash trả result cũ; cùng key/khác hash 409 ACTION_ID_CONFLICT. Không trả 202 trong P0; 200 nghĩa effect+result durable. Response strict: `{"actionRunId":"...","output":{"message":"milestone reached"}}`; validate actionRunId và pinned output schema. Giới hạn request/response 256 KiB, timeout 10s, không redirect.

Timeout/429/5xx retry cùng key/bytes; 400/404/409/422 terminal; 401/403 dừng và yêu cầu credential/grant remediation trước explicit retry. Retry tối đa 12 attempts hoặc 24h (điều kiện nào đến trước), capped backoff 1s→15min + full jitter; sau đó DLQ. Receiver giữ dedup/result tối thiểu đến khi installation uninstall và mọi replay window đã đóng; không được expiry sớm hơn sender retry/replay retention. Compensating action không thuộc P0.

Disable ngăn claim/send mới; unsent actions chuyển skipped với INSTALLATION_INACTIVE. Một HTTP call đã rời CRM có thể hoàn tất; không hứa hủy effect từ xa. Kết quả in-flight vẫn được ghi nhận, không dispatch tiếp. Re-enable yêu cầu fresh credentials/challenge và explicit operator replay, không tự gửi lại skipped work. Uninstall tombstone/revoke, không xóa history.

Skipped unsent work cần remediation được index trong operator dead-letter/review queue với original action run ID; replay API kiểm tra installation, current rule status, original capability snapshot, link và grants trước khi requeue. Rule vẫn paused/revoked link thì fail closed với REPLAY_RULE_INACTIVE/LINK_REVOKED, không âm thầm chạy. Replay không đổi revision/destination/input/idempotency key.

## Panel và SSO
CRM panel API bắt buộc query `installationId` cùng panelId; verify tenant/install/contact grants trước resolve. Không chọn installation đầu tiên chỉ vì cùng panel ID. P0 mỗi contact chỉ có một active external link trong installation+entityType; ambiguous legacy data phải quarantine thay vì tùy chọn record.

Panel endpoint POST HTTPS với service token có audience/scope riêng, body `{"contactRef":{"entityType":"demo.member","externalId":"demo-member-001"}}` theo `remote-panel-request.schema.json`; response khớp outputSchema, không HTML/JS. Same tenant/link/grant/descriptor entityType checks như action. Timeout 3s, response ≤64 KiB, không retry trong UI request; cache theo `(tenant,installation,entity_type,external_id,panel_id,schema_hash)` TTL 60s, stale tối đa 5min, hiển thị fetchedAt/stale. Revoke/unlink/disable invalidate cache; không dùng cached PII khi authorization không còn hợp lệ.

P0 renderer whitelist object/scalar text, number, boolean, date; unsupported schema keywords/UI types bị reject khi install. Demo panel schema dùng title/activityCount/lastActivityAt. Links phải HTTPS và origin đã duyệt. Bảo vệ schema resolver: chỉ snapshot đã pin, không arbitrary remote $ref, không cycles/unbounded depth; schema ≤64 KiB, max nesting 8.

Navigation là link tới module origin đã duyệt. CRM và DemoModule dùng OIDC clients riêng, Authorization Code+PKCE, cùng trusted IdP; SSO qua IdP session, không nhét token vào URL. Module vẫn kiểm tra membership/authorization riêng; principal không phải CRM Contact.
