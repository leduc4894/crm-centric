# PRD — CRM MVP (P0)

## Vấn đề
Một tổ chức cần lưu trữ leads/contacts, phân quyền nhân viên, theo dõi các điểm tương tác từ module bên ngoài và tự động hóa đơn giản; sau này mở rộng thành CRM+Marketing Automation mà không viết lại.

## Đối tượng
- Platform Owner: quản lý môi trường, policies, observability.
- Tenant Admin: thiết lập team, invites, integrations, custom fields.
- CRM Agent: tìm kiếm, tạo/sửa contacts, tags, timeline.
- Marketing Operator: định nghĩa rule trigger→condition→action (theo quyền).
- Module Developer: đăng ký manifest và gửi event đã ký.

## User stories P0
| ID | Story | Acceptance ngắn |
|---|---|---|
| US-01 | Admin tạo tenant, invite member | Không lộ dữ liệu tenant khác; invite có expiry |
| US-02 | User đăng nhập qua OIDC | OIDC PKCE, logout/session policy, issuer/audience verified |
| US-03 | Agent tạo/sửa/tìm contact | unique ID, optimistic concurrency, pagination/filter, audit |
| US-04 | Gắn tags/contact custom fields | schema validation và tenant isolation |
| US-05 | Xem Contact 360 timeline | Event hiển thị theo thời gian, source attribution, dedup |
| US-06 | Admin cài module bằng manifest | Validate schema/capabilities/endpoint/domain/permissions |
| US-07 | Remote module gửi event có chữ ký | Chống replay, idempotent upsert, event v1 |
| US-08 | Workflow P0 tự động xử lý event | trigger match → condition → built-in action addTag; execution history |
| US-09 | Module bị offline, retry | Outbox persisted, retry exponential backoff, DLQ, replay |
| US-10 | Module bị disable | Không nhận action mới; lịch sử và dữ liệu được bảo toàn |
| US-11 | Module UI link và contact panel | Render schema/data được ủy quyền, không chạy arbitrary code |
| US-12 | DemoModule trên container/host riêng | Test end-to-end đạt, không sửa CRM Core |

## Chức năng có điều kiện / loại khỏi P0
- Deals/pipeline đầy đủ, marketing email campaigns và gửi bulk email: P1.
- Drag-and-drop workflow builder: P2, P0 chỉ form cấu hình rule với audit/version.
- Trading journal, LMS lesson, Backcom ledger: không xây trong CRM MVP; chỉ tích hợp demo.
- Payment/billing/commerce, no-code custom module marketplace, AI scoring: roadmap.

## Non-functional requirements P0 (mốc mục tiêu cần benchmark, không phải cam kết thực tế)
- Contact list p95 < 400ms tại 50k contacts/tenant, 50 concurrent users trong môi trường benchmark xác định.
- 99.9% no-loss durable accepted events trong fault-injection; chấp nhận at-least-once, dedup đúng.
- Tenant A không thể đọc/sửa/truy xuất event của Tenant B, cả API, search, worker và export.
- Audit mọi contact mutation, role/permission change, module install/remove và automation action.
- Recovery: RPO <= 15 phút, RTO <= 4 giờ là mục tiêu thiết kế; đo bằng restore drill.
- API breaking changes có gate trong CI; rolling upgrades bảo toàn backward compatibility.

## Definition of Done của MVP
[Xem checklist chi tiết: `engineering/01-TDD-ACCEPTANCE.md`]. P0 hoàn thành khi DemoModule remote chạy E2E, SSO, tenant isolation, event replay và all tests pass. Không xem demo UI là bằng chứng hoàn thành hệ thống.
