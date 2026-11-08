# CRM-Centric Extensible Platform — Bộ tài liệu CRM MVP v1.0
**Ngày chốt:** 2026-11-08 · **Ngôn ngữ:** Tiếng Việt · **Trạng thái:** Implementation blueprint, chưa phải code chạy.

## Tầm nhìn
- Website Blog/SEO là hệ thống độc lập; tích hợp lead-form/webhook/API khi cần.
- CRM là trung tâm Customer 360 và marketing automation; Identity Provider là boundary riêng, không đồng nhất `contact` và `user`.
- LMS, Trading, Backcom và các module tương lai kết nối qua versioned APIs + signed webhooks + manifests; có thể chạy khác host, khác cloud hoặc on-prem.
- MVP phải chứng minh tích hợp **DemoModule** ở host khác mà **không sửa CRM Core**.
- Mỗi ứng dụng có thể bán/triển khai độc lập về sau; không ép cơ sở dữ liệu chung hoặc truy cập bảng chéo.

## Đường đi cho Codex
1. Đọc `00-DECISIONS.md`, `product/01-PRD-MVP.md`, `architecture/01-C4.md`.
2. Đọc `architecture/02-BOUNDED-CONTEXTS.md`, `architecture/03-IDENTITY-TENANCY-SECURITY.md`, `architecture/04-MODULE-EXTENSIBILITY.md`.
3. Xem `contracts/README.md` và các JSON/OpenAPI mẫu. Đây là **contract minh họa cần hoàn thiện trong implementation**.
4. Đọc `engineering/01-TDD-ACCEPTANCE.md`, `roadmap/01-BACKLOG.md`, `AGENTS.md` trước khi bắt đầu coding.
5. Thực thi từng vertical slice, luôn có test và migration trước khi merge.

## Phân loại
- **P0 / MVP:** identity federation, tenancy, CRM contact/tags, registry, API contract, outbox + webhook, event trigger→condition→action tối thiểu, DemoModule.
- **P1:** email delivery provider, consent/suppression, campaign cơ bản, contact-centric Temporal workflows, segments nâng cao.
- **P2+:** visual builder, module marketplace, AI, analytics warehouse, multiregion, advanced ABAC, billing SaaS.

## Giới hạn
- Các tên module, event, schema và endpoint ở đây là **đề xuất thiết kế** chứ không phải sản phẩm/chuẩn đã được cài đặt.
- Yêu cầu kiểm thử production, penetration testing và privacy/legal review trước khi vận hành dữ liệu thực.
- Không bắt buộc triển khai Temporal và broker ngay ngày đầu; có mốc migration rõ ràng.

## Danh mục
- `00-DECISIONS.md`: quyết định kiến trúc.
- `product/`: PRD, user journeys, UX, MVP scope.
- `architecture/`: C4, domain boundaries, identity, modules, events, automation, data.
- `contracts/`: ví dụ OpenAPI, JSON Schema, manifest, webhook.
- `engineering/`: repo, test, CI, coding standards.
- `operations/`: security, deployment, observability, disaster recovery.
- `roadmap/`: implementation waves, deferred technologies.
- `AGENTS.md`: chỉ thị cho Codex.
