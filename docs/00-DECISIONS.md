# Architecture Decision Log (ADR tóm tắt)

| Mã | Quyết định | Lý do | Hệ quả / trade-off |
|---|---|---|---|
| ADR-001 | SEO Blog độc lập | SEO và marketing có release cadence khác | Lead integration qua HTTP/webhook |
| ADR-002 | CRM-Centric về customer, Platform Kernel về kỹ thuật | Tránh CRM God Object | Cần phân định rõ quyền sở hữu |
| ADR-003 | ASP.NET Core .NET 10 LTS cho CRM Backend | Hiệu năng, types, LTS, phù hợp C# | Cần đội có kinh nghiệm .NET |
| ADR-004 | React + TypeScript + Vite cho app frontend | Tách khỏi backend, UI tương tác | Không dùng SSR cho dashboard chính |
| ADR-005 | Modular Monolith CRM trước | Test và vận hành đơn giản | Phải thực thi module boundaries nghiêm |
| ADR-006 | External modules luôn dùng protocol integration | Deploy khác host | Phải giải quyết distributed failures |
| ADR-007 | OIDC Identity boundary riêng | Bảo mật và SSO chuẩn | Keycloak/managed IdP là service riêng; Core không lưu password |
| ADR-008 | PostgreSQL + EF Core, schema per module | Transaction & migrations | Không foreign key xuyên bounded context |
| ADR-009 | Transactional outbox, at-least-once, idempotency | Chống mất sự kiện | Duplicates, eventual consistency |
| ADR-010 | Contract-first OpenAPI + JSON Schema/CloudEvents | SDK có thể generate, độc lập stack | Contract versioning, CI |
| ADR-011 | Per-tenant installation registry | Module enable/disable theo tenant | Secrets, URL validation, scopes |
| ADR-012 | CRM user != contact != module profile | Lead không nhất thiết đăng nhập | Identity link table |
| ADR-013 | MVP automation interpreter tối thiểu, Temporal P1 | Tránh xây runtime quá sớm | Migrate versioned workflow contracts |
| ADR-014 | Public module navigation bằng link + SSO trước | Isolation tốt | UX đa app, giảm rủi ro micro frontend |
| ADR-015 | Không thực thi arbitrary third-party code trong CRM process | Giảm attack surface | Extensions qua schema/HTTP callbacks |
| ADR-016 | Mỗi domain có source of truth | Tránh ghi đè/xung đột | Có rule mapping + reconciliation |
| ADR-017 | Docker Compose cho dev, IaC cho prod | Phù hợp solo dev | HA và Kubernetes để sau |
| [ADR-018](architecture/08-ADR-018-P0-CONTRACT-BASELINE.md) | P0 contract baseline: manifest v2, inbox identity, webhook framing, bounded DSL, error/concurrency và durability envelope | Khép gaps trước runtime | Pre-release migration/consumer fixtures; runtime gates vẫn mở |

## Ràng buộc không được phá vỡ
1. Module chạy khác host phải hoạt động; không được dùng local disk, shared database hay reference assembly nội bộ CRM.
2. Contact CRM không được dùng làm authentication principal.
3. Token OIDC cần audience, issuer, expiry, signature validation và tenant scoped authorization.
4. Mọi public contract phải có version + migration/deprecation policy.
5. Không ghi đè kết quả giao dịch/hoa hồng từ CRM; owner thuộc Trading/Backcom.
6. CRM phải chạy ổn khi DemoModule/CRM integration mất mạng; dead-letter/replay có thể khôi phục.
7. Mọi mutation tenant-bound đều enforce tenant ở backend và database access path; không tin tenant_id chỉ vì client gửi.

## Mở trước khi code
- Chọn IdP: Keycloak self-hosted (ưu tiên) hoặc managed OIDC provider; không tự build password/auth server.
- Chọn SQL mapping: EF Core 10 với migrations/module; Dapper chỉ khi profiling chứng minh cần.
- Chọn event transport MVP: PostgreSQL outbox + delivery worker và signed webhooks; broker chỉ khi cần fan-out và throughput.
- OpenAPI: chuẩn mới nhất đã xác nhận 3.2.1, nhưng generator/toolchain có thể chưa hỗ trợ đủ; dùng 3.1.x để phát hành MVP nếu kiểm thử toolchain pass.
