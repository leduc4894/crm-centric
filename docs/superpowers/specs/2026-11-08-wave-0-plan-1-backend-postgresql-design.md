# Wave 0 — Plan 1: Backend và PostgreSQL

Ngày: 2026-11-08. Trạng thái: phạm vi/spec và plan đã được người dùng duyệt; đã cho phép triển khai SDD trên feature branch, không worktree. Runtime evidence xem verification; final reviewer còn pending.

## Mục tiêu và kết quả

Đưa repo từ blueprint/contract tooling sang lát cắt backend chạy được: API có liveness/readiness, migration kỹ thuật chạy riêng, và integration tests dùng PostgreSQL thật. Người phát triển có thể checkout sạch, restore dependencies, build, chạy tests và khởi động API theo hướng dẫn tái lập.

Plan này là phần đầu Wave 0, không đóng Wave 0 hoặc acceptance P0. Contract hiện có vẫn là đặc tả cho các wave sau, không phải danh sách endpoint phải triển khai ngay.

## Nguồn và ràng buộc

- `AGENTS.md`; `docs/00-DECISIONS.md` (ADR-003/005/008/010).
- `docs/architecture/02-BOUNDED-CONTEXTS.md`, `06-DATA-MODEL.md`.
- `docs/engineering/01-TDD-ACCEPTANCE.md`, `02-REPO-CI-CD.md`, `03-ACCEPTANCE-MATRIX.md`.
- `docs/operations/01-DEPLOYMENT-OBSERVABILITY.md`; `docs/roadmap/01-BACKLOG.md`.

.NET 10, ASP.NET Core, EF Core 10 và PostgreSQL theo ADR. Chỉ một coding agent; reviewer chỉ đọc. Không shared DB hoặc reference nội bộ CRM từ external module. Credentials không được commit, in ra terminal hoặc log.

## Các cách tiếp cận

1. Scaffold toàn bộ modules, Worker và frontend: tạo nhiều cấu trúc chưa có hành vi kiểm chứng; tăng phạm vi review.
2. Chỉ tạo API health không persistence: nhanh nhưng chưa chứng minh EF migrations, PostgreSQL và integration harness.
3. API + infrastructure PostgreSQL tối thiểu + migration runner riêng + Testcontainers: chứng minh được đường chạy thực tế với phạm vi nhỏ.

Chọn hướng 3 theo phạm vi và implementation plan đã được người dùng duyệt.

## Phạm vi

- Solution backend, SDK/tool/package locks, cấu hình build thống nhất.
- API host và hai operational endpoints.
- EF Core/Npgsql infrastructure chỉ sở hữu metadata migration kỹ thuật.
- Migration runner chạy chủ động trước API, dùng credential riêng.
- HTTP integration harness với xUnit, WebApplicationFactory và Testcontainers PostgreSQL.
- Tài liệu command, prerequisites, migration/rollback và evidence của plan.

Không tạo tenants, memberships, contacts, audit/outbox tables hay seed admin. Chưa có business endpoints, OIDC, RBAC/RLS hoặc claim tenant isolation. Không Worker, React, Compose, Keycloak, SDK generation, breaking-diff tooling, CI mới hoặc full observability stack; chúng thuộc các plan/wave tiếp theo. API chỉ mở operational endpoints và không expose route nghiệp vụ chưa có authorization.

## Cấu trúc và ownership

Các tên/path dưới đây là đề xuất mặc định:

```text
global.json
Directory.Build.props
Directory.Packages.props
src/Platform.Api/
src/Platform.Infrastructure/          # infrastructure kỹ thuật của host
src/Platform.Migrator/                # console migration runner
tests/Integration/
docs/contracts/platform-operations.openapi.yaml
docs/engineering/05-WAVE-0-PLAN-1-VERIFICATION.md
```

`Platform.Api` và `Platform.Migrator` tham chiếu `Platform.Infrastructure`; infrastructure không phụ thuộc API. Không tạo BuildingBlocks hoặc domain/application project rỗng. DbContext kỹ thuật không trở thành DbContext chung cho các domain modules; các wave sau có context/migrations riêng theo owner.

Pin SDK patch bằng `global.json`; package versions tập trung, commit `packages.lock.json` cho từng project và pin local `dotnet-ef` nếu dùng. PostgreSQL test image dùng exact tag + digest, không `latest`. Builder xác minh các patch/release ổn định, tương thích và còn được hỗ trợ ở lúc triển khai; ghi phiên bản thực tế vào evidence. Spec không tự đoán số patch chưa xác minh.

## Contract vận hành

Viết OpenAPI 3.1 riêng cho operational endpoints trước implementation và đưa vào contract validation hiện có. Không sửa security mặc định của CRM business API. Hai route không yêu cầu tenant hoặc token, chỉ trả thông tin tối thiểu.

| Endpoint | Điều kiện | Kết quả |
|---|---|---|
| `GET /health/live` | HTTP host hoạt động | 200, JSON `status: live`; không truy vấn DB |
| `GET /health/ready` | DB kết nối được, `SELECT 1` thành công và migration đã áp dụng đúng phiên bản được hỗ trợ | 200, JSON `status: ready` |
| `GET /health/ready` | DB không truy cập được hoặc migration thiếu/không tương thích | 503, Problem Details an toàn |

Readiness có budget tối đa 3 giây cho một lần kiểm tra dependency, không retry kéo dài trong request. Cancellation của caller được truyền xuống DB. Không phụ thuộc IdP, DemoModule hoặc external service. Không cache kết quả readiness trong plan này.

Problem Details sử dụng schema tương đương `Problem` trong baseline `crm-api.openapi.yaml`: bắt buộc `type`, `title`, `status`, `reasonToken`, `correlationId`; `detail` tùy chọn, không thêm trường ngoài schema. Các reason token là `DATABASE_UNAVAILABLE`, `DATABASE_SCHEMA_NOT_READY`, `INTERNAL_ERROR`. Operational OpenAPI định nghĩa shape này độc lập để tooling không cần resolve tham chiếu liên file. Response có `Cache-Control: no-store` và correlation ID do server tạo; không tin arbitrary correlation header từ caller. Không trả hostname, database name, username, SQL, stack trace hoặc exception message.

Lỗi bất ngờ đi qua exception handler, có reason token/correlation ID và structured log đã lọc secrets. Chỉ thêm phần xử lý lỗi tối thiểu cần cho endpoints này; chuẩn hóa observability toàn hệ thống thuộc Plan 2.

## Migration và cấu hình

Migration đầu tiên chỉ tạo schema kỹ thuật `platform` và EF migration history trong schema đó, không thêm bảng đánh dấu giả hoặc business table. Kiểm tra EF tạo history/schema theo đúng thứ tự bằng PostgreSQL thật. DbContext không có domain entities; readiness đọc metadata lịch sử qua EF để kiểm tra tập migration bằng tập được build vào binary. Schema phía trước hoặc thiếu migration đều chưa ready; chính sách rolling compatibility rộng hơn cần thiết kế ở wave sau.

API không tự migrate khi startup và không cần quyền DDL. Migration runner là process riêng, thoát 0 khi thành công và khác 0 khi thất bại; stderr an toàn có reason token/correlation ID. Mỗi lần chỉ chạy một runner; orchestration không được khởi chạy đồng thời nhiều migrators trong plan này. Không có HTTP endpoint chạy migration.

Migration identity có quyền tạo schema/history; application identity là login riêng, không superuser, không BYPASSRLS, không owner schema/history và không CREATE database/schema/table. Migration cấp USAGE schema và SELECT history cần cho readiness; không cấp quyền domain mặc định cho tương lai. Test kiểm tra privilege thực tế bằng SQL, không chỉ đọc connection string.

Connection string lấy từ environment hoặc local secret store. Thiếu cấu hình phải fail fast với reason token và correlation ID, không echo giá trị. Credentials test sinh lúc chạy, không commit. Không bật EF sensitive-data logging; không serialize exception tùy ý. Hướng dẫn local không ghi password vào command example. Plan 1 hướng dẫn kết nối PostgreSQL sẵn có; stack Compose dùng một lệnh thuộc Plan 2.

### Rollback

- Migration mới chạy trên DB test rỗng; chạy lần hai không thay đổi history hoặc lỗi.
- Down về baseline chỉ trong DB disposable, rồi Up lại để chứng minh round trip. Down không drop schema `platform` theo cascade và không xóa object ngoài ownership migration; có thể giữ schema rỗng/history kỹ thuật.
- Production mặc định dùng forward-fix và review script; không tự gọi Down hoặc drop database. DB bẩn hoặc migration fail khiến runner fail, không báo thành công hay để API báo ready.
- Không tự hứa atomicity cho mọi DDL tương lai; migration đầu tiên phải kiểm chứng transaction/failure với PostgreSQL.

## Kiểm thử theo RED → GREEN → refactor

Viết contract và executable integration assertions trước code thực thi. Bootstrap project/test dependencies tối thiểu được phép sau khi spec được duyệt; ghi RED là failure đúng hành vi chưa có, không lấy lỗi thiếu Docker/package làm RED.

| ID | Bằng chứng cần có |
|---|---|
| W0P1-01 | Clean restore ở locked mode, Release build thành công |
| W0P1-02 | Empty PostgreSQL → runner Up → schema/history đúng; runner lần hai idempotent |
| W0P1-03 | API với application identity: live=200, ready=200 sau migration |
| W0P1-04 | DB chưa migrate: live=200, ready=503 + reason/correlation; request không tạo schema/history |
| W0P1-05 | Ngắt DB sau ready: live vẫn 200, ready=503 trong budget có dung sai harness; khôi phục DB thì ready=200 không restart API |
| W0P1-06 | App identity bị từ chối CREATE schema/table và sửa migration history; kiểm tra role flags/ownership đúng |
| W0P1-07 | Up → Down → Up trên DB disposable; object không thuộc migration được giữ nguyên |
| W0P1-08 | Cấu hình thiếu, DB auth sai và lỗi migration: reason/correlation có mặt; payload/log không chứa sentinel secret/connection string |
| W0P1-09 | Schema version không khớp binary khiến ready=503; operational response đúng OpenAPI |
| W0P1-10 | Contract suite cũ và operational contract validation đều pass |

Không SQLite/in-memory thay thế PostgreSQL. Tests dùng database/container cô lập; downtime test không ảnh hưởng test khác và cleanup luôn chạy. Docker/Testcontainers thiếu hoặc không chạy phải báo lỗi prerequisite, không skip rồi công bố suite xanh. WebApplicationFactory kiểm tra HTTP pipeline; hướng dẫn smoke dùng process API thật bổ sung cho checkout mới.

Không viết unit tests sao chép registration/configuration; dùng tests ở ranh giới HTTP/DB và role privileges để chứng minh hành vi.

## Tiêu chí nghiệm thu và evidence

Plan 1 đạt khi toàn bộ W0P1 pass, hướng dẫn clean checkout chạy được và người review có thể kiểm tra command/output đã redacted. Báo cáo ghi commit được kiểm tra, OS, SDK/packages/image digest, Docker prerequisite, RED/GREEN evidence, migration/rollback commands, test counts và giới hạn. Nếu chưa có remote CI thì ghi rõ local-only; không báo CI pass.

Giữ contract baseline trước đây, cập nhật ma trận acceptance ở phạm vi Wave 0 mới có bằng chứng; không đóng các RT tenant/OIDC/CRM/integration/DR. Không claim MVP hoàn tất. Không merge/push hoặc triển khai môi trường ngoài chỉ vì plan test pass.

## Quyết định giao cho builder và rủi ro

Builder chốt exact versions sau xác minh, tên namespaces, helpers của test, cách dừng/phục hồi container và hình thức script smoke; không đổi ADR hoặc thêm domain scope. Chọn giải pháp nhỏ nhất đáp ứng spec và ghi lại lựa chọn ảnh hưởng vận hành.

Rủi ro chính: Docker/user namespace permissions, dependency compatibility và initial EF history schema khi context chưa có entity. Cần giải quyết trong harness/migration thật, không dùng fake persistence. Kết quả sandbox diagnostic trước đó không chứng minh Docker đã hoạt động; phải kiểm tra prerequisite khi triển khai.

## Bước sau review

Builder review chỉ đọc: không còn câu hỏi quan trọng cản trở lập implementation plan. Kiểm tra tài liệu bằng `git diff --check`; chưa chạy runtime tests vì đây chỉ là spec.

Người dùng đã duyệt triển khai theo plan bằng SDD, tuần tự trên feature branch trong checkout hiện có, không worktree. Tasks 1–5 đã task-review; Task 6 và final review theo controller. Evidence: `docs/engineering/05-WAVE-0-PLAN-1-VERIFICATION.md`. Không đóng Wave 0/P0.
