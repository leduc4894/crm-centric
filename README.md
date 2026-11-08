# CRM-Centric Extensible Platform — Bộ tài liệu CRM MVP v1.0
**Ngày chốt:** 2026-11-08 · **Ngôn ngữ:** Tiếng Việt · **Trạng thái:** Backend foundation chạy được; Wave 0/P0 còn mở.

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

Breaking baseline: [ADR-018](docs/architecture/08-ADR-018-P0-CONTRACT-BASELINE.md). Hạng mục runtime còn mở nằm trong ma trận acceptance; API hiện chỉ có operational endpoints, chưa có business runtime.

Chi tiết thay đổi, kiểm chứng và giới hạn: [báo cáo baseline](docs/engineering/04-P0-BASELINE-VERIFICATION.md).

## Backend foundation (Linux)
Prerequisites: SDK **10.0.403** (global.json), Bash, curl, GNU timeout, Python 3,
readable Linux `/proc` (including child socket/fd metadata), Docker cho integration tests, Python venv theo `requirements-contracts.lock`. PostgreSQL test
image và versions đầy đủ: [verification](docs/engineering/05-WAVE-0-PLAN-1-VERIFICATION.md).

```bash
dotnet restore CrmPlatform.sln --locked-mode
dotnet build CrmPlatform.sln -c Release --no-restore
dotnet test CrmPlatform.sln -c Release --no-build --no-restore
.venv/bin/python -m unittest discover -s tests/Contract -v
# Disposable DB/roles, process smoke, automatic container cleanup:
dotnet test CrmPlatform.sln -c Release --no-build --no-restore --filter FullyQualifiedName~SmokeTests
```

API chỉ có anonymous `GET /health/live` và `/health/ready`. Live không gọi DB;
ready kiểm tra DB/migration version trong budget 3 giây, lỗi trả safe Problem
với reasonToken/correlationId. API không tự migrate.

### Provision và chạy với DB riêng đã chuẩn bị
Administrator tạo **database mới dành riêng platform**, admin sở hữu DB. Tạo hai
LOGIN riêng: migrator và application, không superuser/BYPASSRLS, không role
membership nâng quyền. Trong DB mới này, admin REVOKE ALL database privileges
và schema public privileges FROM PUBLIC; cấp CONNECT, CREATE database cho
migrator, chỉ CONNECT database cho app. App không sở hữu database/schema/history
và không có CREATE/TEMPORARY hiệu lực. Không áp dụng ACL này mù quáng vào shared DB.
Migrator sở hữu technical schema/history, cấp app chỉ USAGE platform và SELECT
history. Test-only container superuser chỉ provision/inspect, không chạy API/runner.

Secret manager/local process environment inject các key sau, không nhập credential
trong argv, command history hoặc log; không bật shell tracing:

| Process | Environment keys |
|---|---|
| Migrator | `ConnectionStrings__Platform` (migrator identity), `Database__ApplicationRole` (existing app login name) |
| API | `ConnectionStrings__Platform` (application identity), optional `ASPNETCORE_URLS` |
| Smoke script | `SMOKE_MIGRATION_CONNECTION`, `SMOKE_APPLICATION_CONNECTION`, `Database__ApplicationRole`; optional `SMOKE_PORT` (default 5080, loopback, reserved exclusively for this run) |

```bash
# With required settings injected, run sequentially:
dotnet src/Platform.Migrator/bin/Release/net10.0/Platform.Migrator.dll
dotnet src/Platform.Api/bin/Release/net10.0/Platform.Api.dll
# Alternative: migration + temporary API + curl checks + process cleanup:
bash scripts/backend-smoke.sh
```

Smoke dùng DB non-production đã provision; không tạo/drop database/roles, không
restore/build ngầm. Script giữ credentials trong environment, không in values,
chạy runner tối đa 60 giây, poll API hữu hạn và trap cleanup khi thành công/lỗi.
Smoke verifies listening socket ownership against the launched PID before and after
HTTP probes; an occupied port cannot supply success for another process. Cleanup
sends TERM, allows three seconds, then KILLs/reaps the owned child if necessary.
Diagnostic của script là reasonToken/correlationId an toàn; process output bị ẩn.
Không hỗ trợ nhiều migrators đồng thời; caller phải serialize migration/smoke.
Up có thể retry idempotent sau khi sửa nguyên nhân. Grants sau migration transaction
có thể lỗi khi history đã commit: sửa provisioning rồi retry Up. Production dùng
reviewed forward-fix; không tự Down/drop. Down→Up chỉ được kiểm chứng trong DB
disposable, giữ object ngoài migration; đây không phải restore/DR drill.

## Phân loại
- **P0 / MVP:** identity federation, tenancy, CRM contact/tags, registry, API contract, outbox + webhook, event trigger→condition→action tối thiểu, DemoModule.
- **P1:** email delivery provider, consent/suppression, campaign cơ bản, contact-centric Temporal workflows, segments nâng cao.
- **P2+:** visual builder, module marketplace, AI, analytics warehouse, multiregion, advanced ABAC, billing SaaS.

## Giới hạn
- Business modules, events và endpoints vẫn là thiết kế; chỉ operational API và migration kỹ thuật đã triển khai.
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
