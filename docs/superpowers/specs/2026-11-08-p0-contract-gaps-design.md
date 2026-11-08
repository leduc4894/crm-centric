# Thiết kế khép khoảng trống P0

## Mục tiêu và phạm vi
Theo yêu cầu xử lý gaps ngày 2026-11-08: hoàn thiện baseline đặc tả, schema và kiểm thử contract trước khi xây runtime. Giữ kiến trúc, ranh giới API-only và thứ tự Wave 0→5. Đây không phải triển khai CRM MVP hay bằng chứng đạt acceptance runtime.

## Các cách tiếp cận
1. Đặc tả toàn bộ roadmap trước: tốn công cho P1+ chưa được kiểm chứng.
2. Chỉ sửa schema mẫu: không khép được các bất biến và luồng nghiệp vụ.
3. Chốt bất biến xuyên hệ thống, contract P0 và traceability; triển khai runtime theo wave. Chọn cách 3 theo ủy quyền của người dùng “theo hướng tốt nhất có thể”.

## Quyết định
- Inbox ID nội bộ là tham chiếu event cho execution/timeline; unique ở ingress vẫn gồm tenant, installation, source, event ID.
- Manifest schema v2 có descriptor panel, event, action và permissions; migration từ ví dụ v1 được mô tả trong ADR-018. Chưa có consumer production.
- Webhook HMAC có framing cố định, key binding, retry timestamp mới, payload hash conflict và fixture công khai chỉ dành cho test.
- Rule DSL có all/any, eq/gt/exists, giới hạn độ sâu; published revisions bất biến và action identity ổn định.
- External triggers/actions/panels bind installation rõ ràng; descriptor entityType và typed contactRef. P0 1:1 profile trong installation/entityType; multi-account selection cần contract tương lai. Pending accepted work không time-expire payload cần recovery.
- OpenAPI P0 định nghĩa request/response, concurrency và Problem Details chung; auth/RLS/SSRF vẫn phải kiểm chứng bằng runtime.
- Durability được phân biệt với latency SLO và disaster RPO. Ma trận acceptance không đánh dấu runtime pass khi chỉ có schema test.

## Ràng buộc
Không shared DB với external module; không principal từ Contact; không external side effect trong DB transaction; không tenant_id trong mutation body. Không phát triển P1/P2. Chỉ một agent sửa mã; reviewer chỉ đọc.

## Tiêu chí hoàn tất gói này
Contract tests có RED→GREEN; sample và negative cases được validate; OpenAPI parse/validate; dependency lock và CI tái lập; tài liệu liên quan nhất quán; runtime acceptance vẫn mở. Không merge/push tự động.
