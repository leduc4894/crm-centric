# Webhook protocol v1 — normative P0

Áp dụng cho `POST /api/v1/integrations/events`. HMAC là integration credential, không phải JWT của người dùng. Key trong fixture là dữ liệu test công khai; tuyệt đối không dùng trong môi trường triển khai.

## Request và framing
- TLS bắt buộc; `Content-Type: application/cloudevents+json`; UTF-8, không BOM; không compression (`Content-Encoding` absent/identity). Body tối đa 256 KiB; giới hạn được enforce trước JSON parse/HMAC buffering.
- Headers bắt buộc, mỗi header đúng một giá trị: `X-Installation-Id` (UUID lowercase canonical), `X-Webhook-Key-Id` (`[A-Za-z0-9_-]{1,64}`), `X-Event-Id` (`[A-Za-z0-9._:-]{1,128}`), `X-Event-Timestamp` (Unix seconds decimal, không leading zero), `X-Event-Signature` (`v1=` + 64 lowercase hex).
- Không query string; không chấp nhận duplicate security headers, obs-fold, CR/LF hoặc whitespace padding trong giá trị. Chỉ `POST` với path cố định; proxy không được thay đổi signed body. TLS termination không biến forwarded headers thành trust anchor.
- Bytes được ký là UTF-8 của 7 dòng sau, mỗi dòng kết thúc bằng LF, nối trực tiếp raw body bytes. Không thêm LF sau body trừ khi nó có sẵn trong raw body.

```text
crm-webhook-v1
POST
/api/v1/integrations/events
<X-Installation-Id>
<X-Webhook-Key-Id>
<X-Event-Timestamp>
<X-Event-Id>
```

`signature = "v1=" + lowercase_hex(HMAC-SHA256(key_bytes, framed_bytes))`. Key là 32 random bytes tối thiểu, chỉ lưu secret reference; không dùng chuỗi hex/base64 làm key bytes. Compare digest constant-time sau kiểm tra định dạng/độ dài. Không canonicalize JSON trước khi ký/xác minh.

## Binding, validation và ACK
1. Rate/size limits, strict transport parsing; lookup `(installation_id,key_id)` bằng credential index chuyên biệt. Index chỉ trả binding/secret reference/status cần xác minh, không cho phép query tenant data trước authentication. Unknown binding dùng cùng lỗi `WEBHOOK_AUTH_INVALID`, tránh existence oracle.
2. Binding quyết định tenant; header installation phải đúng binding, installation active/degraded có quyền publish. Disabled/uninstalled/revoked key không được ingest. Không tin `tenant_id`, source path hay contact ID từ payload.
3. `abs(server_now - delivery_timestamp) <= 300` giây; đúng biên 300 được chấp nhận, 301 bị từ chối. Xác minh signature trước khi parse payload. Timestamp tương lai được xử lý cùng cửa sổ này. Server cần clock monitoring.
4. Validate envelope, event type/version đã đăng ký và schema pinned; event ID trong header bằng `body.id` chính xác. Source phải bằng giá trị exact được bind khi handshake theo MODULE-CALLBACKS. Subject bắt buộc, entityType từ descriptor; Demo data.externalEntityId phải bằng subject. Extension fields không bao giờ tạo tenant/scopes. Event time là thời điểm nghiệp vụ, độc lập delivery timestamp.
5. SHA-256 raw body được dùng phát hiện conflicting reuse. Unique key `(tenant_id,installation_id,source,event_id)` được enforce bằng DB constraint, không check-then-insert. Cùng key/cùng hash trả 202 với cùng inbox ID và status duplicate; khác hash trả 409 `EVENT_ID_CONFLICT`. Sender phải giữ nguyên bytes khi retry, kể cả JSON whitespace.
6. Inbox + work item được commit trước ACK 202. DB unavailable/commit unknown không trả accepted; dùng 503 `INGEST_UNAVAILABLE`. Sender retry cùng identity/bytes. Worker xử lý sau, unmatched cũng là accepted và hiển thị trong queue.

## Retry, rotation và retention
- Sender lưu raw bytes trong durable outbox. Mỗi lần retry ký lại với delivery timestamp hiện tại và key đang active, giữ nguyên body/id/source. Không gửi lại timestamp đã hết cửa sổ; không thay business `time` để retry.
- Timeout/429/5xx: capped exponential backoff + jitter, tôn trọng Retry-After. 400/409/413/415 là terminal, không retry mù. 401 dừng để re-handshake/rotate; 403 installation revoked không được tự bật lại.
- Rotation: new keyId, overlap 24h mặc định; hết overlap hoặc explicit revoke thì key cũ bị từ chối ngay. Compromise revoke không chờ overlap. Credentials được cấp ngoài public manifest và không ghi audit payload.
- Pending/processing/unmatched/retry/DLQ work phải giữ payload cần thiết cho recovery; không time-expire accepted work đang mở. Full payload retention mặc định 30 ngày sau terminal resolution và khi không còn replay/hold liên quan; tenant policy không được prune open work. Dedup tombstone `(identity, hash, inbox_id)` giữ suốt vòng đời installation và không prune khi outbox/replay còn tồn tại. Sau uninstall giữ tombstone 90 ngày rồi purge theo retention policy; installation ID không tái sử dụng, credentials đã revoke. Không replay terminal event đã purge payload; trả `EVENT_PAYLOAD_EXPIRED`. Source/ID có thể là personal data: minimize/redact/encrypt theo classification, không mặc định coi tombstone vô danh.

## Error mapping
Mọi lỗi dùng Problem Details trong OpenAPI với correlationId an toàn do server sinh/validate. Không trả raw body, signature, key, token hay exception detail.

| HTTP | reasonToken |
|---|---|
| 400 | WEBHOOK_HEADER_INVALID / EVENT_SCHEMA_INVALID / EVENT_HEADER_ID_MISMATCH / EVENT_SOURCE_INVALID / EVENT_SUBJECT_MISMATCH |
| 401 | WEBHOOK_AUTH_INVALID / WEBHOOK_TIMESTAMP_INVALID |
| 403 | INSTALLATION_INACTIVE / EVENT_SCOPE_DENIED |
| 409 | EVENT_ID_CONFLICT |
| 413 / 415 / 429 | PAYLOAD_TOO_LARGE / CONTENT_TYPE_UNSUPPORTED / RATE_LIMITED |
| 503 | INGEST_UNAVAILABLE |

Lỗi trước khi có tenant binding chỉ ghi security telemetry đã redact, không gán tenant từ request. Signed duplicate trong cửa sổ là retry hợp lệ, không tạo side effect mới; captured request không được tạo event mới hoặc vượt cửa sổ.

## Consumer fixtures và runtime gates
`webhook-signature.fixture.json` có raw UTF-8 body, header values, body hash và expected signature; test Python đối chiếu known-answer, byte tampering và metadata tampering. Đây là fixture tương thích giao thức, chưa có production verifier.

Wave 3 phải chạy verifier .NET thực với fixture này và các ca: ±300/301s, wrong installation/key/source, header/body mismatch, duplicate headers, rotation/revoke, UTF-8, 256 KiB boundary, 100 concurrent duplicates, same ID/different payload và retry sau offline 30 phút. Wave 5 chạy crash/restore; không đánh dấu chúng pass bằng fixture test.
