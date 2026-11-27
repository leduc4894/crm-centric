# Wave 1 — thiết kế đề xuất cho identity và tenancy

Trạng thái: bản đề xuất để review, chưa phê duyệt và chưa triển khai. Nguồn: [backlog](../../roadmap/01-BACKLOG.md), [identity/security](../../architecture/03-IDENTITY-TENANCY-SECURITY.md), [quyết định P0](../../architecture/07-P0-ACCESS-AND-STATE.md), [ma trận nghiệm thu](../../engineering/03-ACCEPTANCE-MATRIX.md), [cấu hình IdP](../../operations/05-IDP-CONFIGURATION.md).

## Mục tiêu và cách chia

Đề xuất đi theo lát cắt dọc, bắt đầu bằng API xác thực và tenant context được kiểm chứng qua PostgreSQL, sau đó mở rộng thành quản lý tenant/membership/invite, RBAC/audit và đăng nhập browser. Kết thúc Wave 1 cần hai phiên tenant A/B độc lập chứng minh không rò rỉ, cùng token security suite. Không coi fake issuer, metadata Keycloak hoặc test đơn lẻ là bằng chứng đăng nhập E2E.

Hai cách xem xét: (1) hoàn thành một lát cắt API từ token đến membership và RLS trước, rồi mở rộng tới UI; (2) xây login UI trước rồi nối phân quyền và DB. Đề xuất (1): sớm kiểm chứng ranh giới bảo mật và đường truy cập dữ liệu, nhưng UI hoàn chỉnh xuất hiện muộn hơn. Cách (2) cho thấy màn hình sớm nhưng chưa chứng minh tenant isolation. Không thêm auth server tự xây hoặc đồng nhất contact và user.

## Lát cắt đầu tiên đề xuất

- Xác thực bearer token bằng thư viện chuẩn, kiểm tra chữ ký, issuer, audience, expiry, nbf và thuật toán; xử lý JWKS rotation và lỗi IdP theo hướng fail closed. Phân biệt cấu hình issuer công khai với đường backchannel có thể truy cập; không thay đổi issuer để vượt kiểm tra. Chỉ cho phép HTTP trong cấu hình local/test được giới hạn rõ ràng; production dùng HTTPS.
- Principal được định danh bằng `(issuer, subject)`; `X-Tenant-Id` chỉ là lựa chọn, không cấp quyền. Tra membership active từ dữ liệu đáng tin cậy trước khi tạo tenant context. Không tự link contact theo email.
- Tạo migration cho tenant và membership với khóa/constraint tenant phù hợp; xây đường bootstrap được kiểm soát để giải quyết bài toán tra membership trước khi thiết lập tenant context, không cấp bypass RLS chung cho application role. Thiết kế SQL và quyền cụ thể phải được kiểm tra trong kế hoạch trước khi code.
- Áp dụng PostgreSQL RLS fail closed cho dữ liệu tenant thuộc lát cắt, transaction-local context và bảo đảm reset trên pooled connections. Mọi truy vấn vẫn có tenant predicate; RLS là phòng thủ bổ sung.
- Cung cấp đường API tối thiểu theo OpenAPI hiện hành để principal liệt kê membership/tenant và chứng minh truy cập được bảo vệ; không tạo endpoint công khai mới trái contract. Tenant provisioning chỉ dành cho platform provisioner, không suy quyền từ header.
- Test trước implementation: token sai issuer/audience/chữ ký/thuật toán/thời gian; membership thiếu, bị thu hồi, tenant giả và cross-tenant; RLS khi thiếu context và tái sử dụng connection; test tích hợp PostgreSQL thật với Testcontainers. Kiểm tra mọi lỗi đóng có reason token và correlation ID, không log token.

## Phần tiếp theo và điều kiện kết thúc

Thực hiện quản lý membership/invite theo contract: token chỉ lưu hash, hạn 24 giờ, verified email, single-use atomic và không tự link contact; RBAC theo bảng scope P0, không cho thay owner hoặc xóa owner cuối. Audit mutation trong cùng transaction. Browser dùng Authorization Code + PKCE qua thư viện chuẩn; kiểm chứng logout và session với IdP thực. Chạy cross-tenant trên API và SQL/RLS, cùng các regression contract/security và full suite theo wave exit. Ghi nhận kết quả thực tế trong acceptance matrix và handoff.

Không bao gồm contact CRUD, module runtime, automation, marketing editor hay production deployment. P0 chỉ hoàn tất sau các nghiệm thu remote DemoModule, concurrent duplicate, tenant isolation, outage/recovery và restore theo AGENTS.md. Thay đổi contract phá tương thích cần ADR, migration plan và consumer tests; không mặc định thay contract.

## Quyết định triển khai còn cần kế hoạch làm rõ

Cơ chế bootstrap tra membership với quyền DB tối thiểu; lifecycle provisioning tenant đầu tiên; mapping cấu hình issuer/backchannel an toàn; lựa chọn và pin thư viện OIDC tương thích lockfile; mapping chi tiết operation OpenAPI, errors và test IDs. Các lựa chọn phải giữ nguyên các invariant nêu trên; không nới quyền để xử lý khó khăn triển khai.
