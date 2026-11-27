# ADR-020 — Contact upsert theo email trong tenant

- Ngày: 2026-11-27
- Trạng thái: Được người dùng phê duyệt để triển khai ngày 2026-11-27; chưa chứng minh compatibility hoặc rollout.
- Điều chỉnh đề xuất đối với [access/state](07-P0-ACCESS-AND-STATE.md), [data model](06-DATA-MODEL.md) và [Wave 2 design](../superpowers/specs/2026-11-27-wave-2-crm-contacts-design.md).

## Bối cảnh và quyết định sản phẩm

Người dùng chọn email duy nhất trong từng tenant và yêu cầu tạo contact bằng email đã tồn tại sẽ cập nhật contact hiện có. Chỉ các trường được gửi được cập nhật; trường bỏ qua được giữ nguyên. Contact giữ ID ổn định, độc lập với identity user; email không cấp membership và không tự liên kết issuer/sub. Email nullable theo contract hiện có, vì người dùng chưa yêu cầu thay đổi điều đó.

Hai cách trước đó là cho phép contact trùng email hoặc từ chối email trùng; người dùng chọn cập nhật contact hiện có. Quy tắc mới có thể ghi đè dữ liệu nếu hai người dùng cùng địa chỉ email; đó là hệ quả nghiệp vụ của việc dùng email để đối chiếu contact, không phải bằng chứng về danh tính con người.

## Quyết định kỹ thuật

- Duy trì unique key theo `(tenant_id, email_normalized)` cho contact active có email, thực thi ở PostgreSQL để chịu được yêu cầu đồng thời. Nhiều contact không có email vẫn được phép. Chuẩn hóa triển khai: PostgreSQL `lower(btrim(email))`, bỏ dấu cách thông thường đầu/cuối và chuyển chữ thường theo database collation; không xóa dấu chấm hoặc phần `+` của local part. Tách riêng quy tắc so sánh email của identity/invitation.
- Email của contact soft-deleted được phép tái sử dụng; một contact active mới được tạo thay vì tự phục hồi record đã xóa. PATCH đổi email sang email của contact active khác trả xung đột, không tự gộp.
- POST email mới hoặc null tạo contact (`201`); POST email đã tồn tại cập nhật contact (`200`) và trả ID, version, strong ETag mới. `displayName` vẫn bắt buộc theo CreateContact; thuộc tính tùy chọn vắng mặt giữ nguyên, explicit null xóa trường nullable. Không dùng dữ liệu tenant từ body.
- Người dùng yêu cầu ETag cho cập nhật: POST gặp email active hiện có phải có một strong `If-Match` khớp version hiện tại; thiếu trả 428, stale trả 412, giá trị yếu, nhiều giá trị hoặc sai định dạng trả 400. Không ghi dữ liệu hay journal khi precondition thất bại. Upsert và PATCH/DELETE dùng cùng semantics; mutation cạnh tranh phải kiểm tra version atomic. Khi hai yêu cầu cùng ETag thay đổi cùng contact, tối đa một yêu cầu thành công. Contact mới không yêu cầu If-Match; nếu gửi If-Match hợp lệ mà không có contact active khớp, trả 412 và không ghi. API trả version/ETag sau mutation.
- Mỗi thay đổi thực tế commit cùng audit và outbox; không remote I/O. No-op không tăng version và không phát audit/outbox giả; mutation thực tăng version một lần và phát một audit cùng một intent tương ứng.

## Contract, migration và rollback

Contract trước ADR chỉ công bố `201` và cho phép email trùng. Upsert là thay đổi hành vi có thể phá consumer, kể cả khi thêm `200` vào OpenAPI. Thay đổi cần contract diff, ADR, migration plan và consumer tests: consumer cũ chỉ chờ `201`, consumer mới phân biệt `201`/`200`, lỗi và ETag. Không tự thay trusted baseline hoặc khẳng định tương thích chỉ vì thay đổi schema là additive.

Trước khi tạo unique index, kiểm tra duplicate active theo tenant và chuẩn hóa; không chọn winner hoặc gộp dữ liệu tự động. Nếu có duplicate thì dừng migration, xuất báo cáo an toàn và xử lý có chủ đích; không log email hay dữ liệu cá nhân. Chưa có bằng chứng môi trường triển khai có hay không có dữ liệu. Migration phải chạy transactional, bảo toàn audit/outbox và isolation; rollback sau khi có dữ liệu cần backup và xem xét forward-fix, không âm thầm mất dữ liệu. Chuẩn bị hướng dẫn consumer rollback trước rollout.

## Kiểm thử cần thiết

Contract và consumer tests cho create mới, upsert, schema và ETag; PostgreSQL Testcontainers cho tenant isolation, null, chuẩn hóa, cạnh tranh create cùng email, PATCH conflict, soft-delete/reuse, nâng cấp dữ liệu trùng, rollback và atomic audit/outbox; kiểm thử đồng thời theo chính sách được duyệt. Test liên quan chạy local; full suite Wave 2 exit chạy GitHub CI theo chỉ đạo người dùng. Security tiếp tục FAIL/deferred; Wave 2/P0 chưa được tuyên bố đạt.
