# CRM-Centric Extensible Platform — Bộ tài liệu CRM MVP v1.0
**Ngày chốt:** 2026-11-08 · **Ngôn ngữ:** Tiếng Việt · **Trạng thái:** Implementation blueprint, chưa phải code chạy.

## Tầm nhìn
- Website Blog/SEO là hệ thống độc lập; tích hợp lead-form/webhook/API khi cần.
- CRM là trung tâm Customer 360 và marketing automation; Identity Provider là boundary riêng, không đồng nhất `contact` và `user`.
- LMS, Trading, Backcom và các module tương lai kết nối qua versioned APIs + signed webhooks + manifests; có thể chạy khác host, khác cloud hoặc on-prem.
- MVP phải chứng minh tích hợp **DemoModule** ở host khác mà **không sửa CRM Core**.
- Mỗi ứng dụng có thể bán/triển khai độc lập về sau; không ép cơ sở dữ liệu chung hoặc truy cập bảng chéo.

## Đường đi cho Codex
1. Đọc [`docs/00-DECISIONS.md`](docs/00-DECISIONS.md), [`PRD`](docs/product/01-PRD-MVP.md), [`C4`](docs/architecture/01-C4.md).
2. Đọc `docs/architecture/`, đặc biệt identity/tenancy, module extensibility và `07-P0-ACCESS-AND-STATE.md`.
3. Đọc [`contract baseline P0`](docs/contracts/README.md), webhook protocol và module callbacks. Schema/tests đã chạy không đồng nghĩa runtime đã triển khai.
4. Đọc [`acceptance`](docs/engineering/01-TDD-ACCEPTANCE.md), [`ma trận bằng chứng`](docs/engineering/03-ACCEPTANCE-MATRIX.md), [`backlog`](docs/roadmap/01-BACKLOG.md), `AGENTS.md` trước khi coding.
5. Thực thi từng vertical slice, luôn có test và migration trước khi merge.

## Kiểm chứng contract hiện có
```powershell
python -m venv .venv
./.venv/Scripts/python.exe -m pip install -r requirements-contracts.lock
./.venv/Scripts/python.exe -m unittest discover -s tests/Contract -v
```
Linux/macOS dùng `.venv/bin/python`. CI dùng cùng lockfile/command, Python 3.15.2. Đây là contract tooling; backend .NET/frontend React vẫn theo ADR.

Breaking baseline: [ADR-018](docs/architecture/08-ADR-018-P0-CONTRACT-BASELINE.md). Hạng mục runtime còn mở nằm trong ma trận acceptance; repo vẫn chưa có ứng dụng chạy.

Chi tiết thay đổi, kiểm chứng và giới hạn: [báo cáo baseline](docs/engineering/04-P0-BASELINE-VERIFICATION.md).

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
