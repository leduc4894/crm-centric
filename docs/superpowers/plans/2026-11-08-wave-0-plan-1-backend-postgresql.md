# Wave 0 Plan 1 — Backend và PostgreSQL Implementation Plan

> **Execution choice (user authorized 2026-11-08):** SDD trên feature branch `feat/wave-0-plan-1-backend-postgresql` trong checkout hiện có, không worktree. Một implementer tại một thời điểm, task/final reviewers chỉ đọc; Mỗi task do một subagent implementer thực hiện; implementer không tự spawn agent. Checkboxes ghi kết quả thực tế, không thay final review.

**Goal:** Có backend build được, migration runner riêng và operational HTTP endpoints được chứng minh bằng PostgreSQL/Testcontainers thật.

**Architecture:** API host và console migrator dùng chung infrastructure kỹ thuật, không chứa domain tables. API dùng DB identity hạn chế, không tự migrate; readiness kiểm tra kết nối và tập migration trong budget 3 giây. Test harness cô lập DB và credentials.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10, Npgsql, PostgreSQL, xUnit, Microsoft.AspNetCore.Mvc.Testing, Testcontainers.PostgreSql; Python contract tooling hiện có.

**Spec:** `docs/superpowers/specs/2026-11-08-wave-0-plan-1-backend-postgresql-design.md` — người dùng đồng ý chuyển sang viết plan ngày 2026-11-08.

## Global Constraints

- .NET 10, ASP.NET Core, EF Core 10 và PostgreSQL theo ADR.
- Chỉ một coding agent; reviewer chỉ đọc. Không shared DB hoặc reference nội bộ CRM từ external module.
- Credentials không được commit, in ra terminal hoặc log.
- Không business tables/endpoints, Worker, frontend, Compose, IdP, CI mới, SDK generation hoặc RLS implementation.
- Test RED phải là thất bại hành vi, không phải thiếu Docker, package hoặc lỗi compile.
- Pin SDK patch, packages/locks và PostgreSQL exact image tag + digest sau xác minh ở lúc triển khai.
- Migration chạy riêng, application identity không DDL, không superuser/BYPASSRLS/ownership.
- Không đóng Wave 0/P0 bằng kết quả plan này; không merge/push/deploy tự động.

## Review Focus

1. PostgreSQL mặc định có thể cấp CREATE trên schema `public`: kiểm tra và thu hồi quyền hiệu lực (Task 2).
2. Schema phía trước hoặc history tồn tại nhưng chưa đủ migration: không ready (Task 4).
3. Timeout/cancellation với connection pooling không được để request readiness giữ tài nguyên vô hạn (Task 4).
4. Credentials sai và connection string malformed có thể lọt vào exception/log của framework (Task 5).
5. Header correlation do caller gửi không được giả mạo ID server, kể cả giá trị rất dài (Task 5).

## File map và interfaces chung

| File / nhóm | Trách nhiệm |
|---|---|
| `CrmPlatform.sln`, `global.json`, `Directory.Build.props`, `Directory.Packages.props` | Solution, SDK/package pins, net10.0, nullable, deterministic build, lock generation |
| `src/Platform.Infrastructure/Platform.Infrastructure.csproj`, `packages.lock.json` | EF/Npgsql dependency boundary |
| `src/Platform.Infrastructure/Persistence/PlatformDbContext.cs` | DbContext kỹ thuật, history `platform.__EFMigrationsHistory`, không entities |
| `src/Platform.Infrastructure/Persistence/Migrations/*` | Initial technical migration + generated EF metadata/snapshot |
| `src/Platform.Infrastructure/Persistence/DatabaseReadiness.cs` | Kết nối, SELECT 1, so sánh migration sets |
| `src/Platform.Infrastructure/Persistence/DatabaseRoleGrants.cs` | Grants vào identity application đã provision, không tạo login/password |
| `src/Platform.Infrastructure/Configuration/DatabaseOptions.cs` | Validate cấu hình mà không echo secrets |
| `src/Platform.Api/Program.cs`, `Platform.Api.csproj`, `packages.lock.json` | Composition root và public partial Program cho test host |
| `src/Platform.Api/Operations/HealthEndpoints.cs` | Map hai operational routes |
| `src/Platform.Api/Operations/OperationalErrors.cs` | Problem Details, safe logging, server correlation middleware |
| `src/Platform.Migrator/Program.cs`, `Platform.Migrator.csproj`, `packages.lock.json` | Console runner, exit codes, safe diagnostic |
| `tests/Integration/Integration.csproj`, `packages.lock.json` | xUnit integration project |
| `tests/Integration/Fixtures/{PostgresFixture,ApiFactory,MigratorProcess}.cs` | Cô lập PostgreSQL, HTTP host, gọi process migrator |
| `tests/Integration/{MigrationTests,RolePrivilegeTests,HealthTests,ReadinessTests,OperationalSecurityTests}.cs` | Các nhóm acceptance tests |
| `tests/Contract/test_operational_contract.py` | Validate operational OpenAPI + payload fixtures |
| `docs/contracts/platform-operations.openapi.yaml` | Contract operational riêng, không tham chiếu liên file |
| `scripts/backend-smoke.sh` | Smoke API process thật với DB đã provision |
| `docs/engineering/05-WAVE-0-PLAN-1-VERIFICATION.md` | Versions, command/output, RED/GREEN, phạm vi bằng chứng |

Tên namespace tương ứng project. Các signature dưới đây là contract giữa task, không phải yêu cầu dựng abstraction generic.

## Task 1: Operational contract và liveness chạy được

**Files:** Tạo solution/config/project files ở file map, `Program.cs`, `HealthEndpoints.cs`, test harness, `HealthTests.cs`, operational OpenAPI và Python test; sửa `.gitignore` để bỏ qua `bin/`, `obj/`, local secrets nhưng giữ lockfiles.

**Interfaces:**
- `HealthEndpoints.MapOperationalEndpoints(this IEndpointRouteBuilder endpoints)` trả `IEndpointRouteBuilder`.
- `ApiFactory : WebApplicationFactory<Program>` nhận cấu hình test trong constructor; không đưa credentials vào command line/log.
- Operational OpenAPI 3.1 có `security: []`, chỉ hai GET routes, success JSON enum `live`/`ready`, Problem fields theo spec; `Cache-Control: no-store`, `X-Correlation-Id` response header, 503 cho readiness và 500 cho unexpected error.

- [x] Kiểm tra `dotnet --info`, `docker version`, `docker info` và Python venv hiện có. Không dump env/config. Thiếu prerequisite thì ghi blocker chính xác, không skip tests. Xác minh versions từ nguồn chính thức khi chọn SDK/packages/image, ghi nguồn và digest vào verification file; không cài framework khác ADR.
- [x] Bootstrap tối thiểu solution + API/infrastructure/migrator/integration projects, project references như spec, SDK/package pins và locks. Dùng xUnit runner tương thích `dotnet test`; chưa thêm handler trả thành công. Không dùng template weather forecast.
- [x] Viết `test_operational_contract_is_valid_and_anonymous`, `test_operational_problem_matches_baseline`, `test_operational_examples_validate` trong Python. Assert OpenAPI validate; hai routes và headers đúng; schema Problem bằng baseline; reject thiếu reason/correlation hoặc thêm field; giữ business API security nguyên trạng.
- [x] Chạy `python -m unittest discover -s tests/Contract -v`: RED vì operational contract chưa có. Viết contract đúng rồi chạy GREEN, không đổi dependencies Python nếu tooling hiện có đủ.
- [x] Viết `HealthTests.Live_is_independent_of_database`: cấu hình connection hợp lệ trỏ tới DB không truy cập được; GET `/health/live` assert 200, JSON chỉ `status=live`, no-store và server correlation header.
- [x] Chạy `dotnet test tests/Integration/Integration.csproj --filter FullyQualifiedName~HealthTests`: RED vì route chưa map. Implement liveness và middleware response tối thiểu, không truy vấn DB.
- [x] Chạy lại Python suite, test filter trên và `dotnet build CrmPlatform.sln -c Release`; GREEN. Refactor nếu cần, commit explicit files với message `feat(platform): bootstrap backend and operational liveness`.

## Task 2: Migration runner và role boundaries thật

**Files:** Tạo `PlatformDbContext.cs`, `DatabaseRoleGrants.cs`, initial migration/metadata, `DatabaseOptions.cs`, migrator implementation, `PostgresFixture.cs`, `MigratorProcess.cs`, `MigrationTests.cs`, `RolePrivilegeTests.cs`.

**Interfaces:**
- `PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext`; history schema/name như file map.
- `DatabaseRoleGrants.ApplyAsync(NpgsqlConnection connection, string applicationRole, CancellationToken cancellationToken) : Task`; role identifier phải quote bằng API Npgsql, không interpolate raw SQL identifier.
- CLI mặc định Up: `dotnet run --project src/Platform.Migrator --no-build -c Release`; đọc `ConnectionStrings__Platform` (migration identity), `Database__ApplicationRole` (login đã provision).
- Application đọc cùng tên connection setting nhưng chứa application identity; role name không phải password. Runner tạo correlation GUID, exit 0 thành công/1 lỗi; reason `DATABASE_CONFIGURATION_INVALID` hoặc `DATABASE_MIGRATION_FAILED`.
- `PostgresFixture` cấp mỗi test database/roles riêng và credentials sinh runtime; container admin chỉ dùng provision/inspect test, không làm API/migrator identity. Admin sở hữu DB test; migrator là non-owner có CONNECT/CREATE database và sở hữu technical schema/history, app chỉ CONNECT + USAGE/SELECT history sau Up (controller ruling).

- [x] Viết `MigrationTests.Up_creates_only_technical_schema_and_history`, `Up_twice_is_idempotent`, `RolePrivilegeTests.Application_has_only_readiness_privileges`.
- [x] Assert history có đúng initial migration; không có business tables; API role SELECT history được; INSERT/UPDATE/DELETE history, CREATE TABLE ở `platform`/`public` và CREATE SCHEMA đều bị từ chối; không superuser/BYPASSRLS và không owner. Kiểm tra riêng CREATE privilege ở database/schema, không dựa vào role name.
- [x] Chạy `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~MigrationTests|FullyQualifiedName~RolePrivilegeTests'`: RED do runner chưa thực hiện migration/grants.
- [x] Implement context và migration `InitialPlatformInfrastructure` chỉ EnsureSchema; EF history do provider quản lý. Đảm bảo provider tạo schema trước history trên DB rỗng; nếu cần explicit schema bootstrap dùng migration identity, không dùng app startup. Runner gọi `Database.MigrateAsync`, sau đó cấp quyền readiness cho app role đã provision. Thu hồi CREATE hiệu lực từ PUBLIC trên database/public schema trong DB được quản lý để app không được thừa hưởng; ghi rõ thay đổi privilege và không áp dụng mù quáng vào shared DB có sẵn.
- [x] Không dùng EnsureCreated hoặc bật EF sensitive logging. Validate role tồn tại và khác migration identity; connection config thiếu/malformed fail an toàn. Không log exception object/raw provider message.
- [x] Chạy filter trên và Release build: GREEN. Ghi quyền provision và command không chứa credentials; commit `feat(platform): add explicit postgres migrations and restricted app role`.

## Task 3: Migration failure và rollback có kiểm chứng

**Files:** Sửa `MigrationTests.cs`, `MigratorProcess.cs`, migration và runner nếu test yêu cầu.

**Interfaces:** Down chỉ qua EF API trong integration test (`IMigrator.MigrateAsync("0", cancellationToken)`), không thêm destructive production CLI. Migrations giữ schema kỹ thuật khi Down; runner Up theo Task 2.

- [x] Viết `MigrationTests.Down_then_up_preserves_unowned_objects`: tạo bảng sentinel bằng migration identity trong schema platform, Down, assert sentinel còn và initial history row mất; Up lại thành công.
- [x] Viết `MigrationTests.Ddl_failure_never_reports_success`: tạm REVOKE CREATE database của non-superuser migrator trước Up trên DB rỗng; runner exit 1, reason/correlation hợp lệ; không có initial history row; cấp quyền lại và Up thành công. Dùng test database riêng; không tạo migration lỗi trong production assembly.
- [x] Viết `MigrationTests.Missing_application_role_fails_safely`: runner không báo success; sửa cấu hình role rồi retry Up không làm hỏng DB. Không bắt buộc rollback migration đã commit nếu lỗi ở grants; báo cáo rõ partial provisioning và retry idempotent.
- [x] Chạy filter `FullyQualifiedName~MigrationTests`: baseline GREEN regression 5/5, không cần sửa production; controller chấp nhận không tạo RED giả. Down không cascade/drop object ngoài migration và failure được kiểm chứng.
- [x] Chạy lại migration + role tests: GREEN; ghi migration transaction/partial-provisioning semantics vào verification; commit `test(platform): verify migration failure and rollback safety`.

## Task 4: Readiness kiểm tra DB và migration version

**Files:** Tạo `DatabaseReadiness.cs`, `ReadinessTests.cs`; sửa `HealthEndpoints.cs`, `ApiFactory.cs`, harness theo nhu cầu.

**Interfaces:**
- `DatabaseReadiness.CheckAsync(CancellationToken cancellationToken) : Task<DatabaseReadinessResult>`.
- `DatabaseReadinessResult` là enum `Ready`, `Unavailable`, `SchemaNotReady`; không trả exception chứa secrets.
- Mỗi call linked cancellation token từ caller + `CancelAfter(TimeSpan.FromSeconds(3))`; không retry/cache. Execute `SELECT 1` và đọc applied migrations bằng EF; so sánh set equality với `GetMigrations()` từ binary.
- ready success 200 `{"status":"ready"}`; missing history/mismatch => 503 `DATABASE_SCHEMA_NOT_READY`; network/auth/timeout => 503 `DATABASE_UNAVAILABLE`. Cancellation do caller hủy được truyền xuống, không cố ghi response sau abort.

- [x] Viết `ReadinessTests.Ready_uses_application_identity`, `Unmigrated_database_is_not_ready_and_is_not_modified`, `Missing_or_future_migration_is_not_ready`. Assert 200 sau Up; 503 trước Up hoặc có unknown migration; history có nhưng rỗng vẫn 503; liveness luôn 200. Dùng migration identity để setup history variants trong DB riêng.
- [x] Viết `ReadinessTests.Database_outage_recovers_without_api_restart`: dedicated container, ready 200 → stop DB → ready 503/live 200 → start DB → ready 200 cùng API instance; poll recovery có timeout hữu hạn, không sleep cố định dài.
- [x] Viết `ReadinessTests.Blocked_history_read_times_out_and_releases_connection`: admin giữ ACCESS EXCLUSIVE lock history, readiness kết thúc trong 3 giây budget + 2 giây dung sai harness; bỏ lock rồi request kế tiếp thành công. Test client timeout phải lớn hơn assertion budget.
- [x] Viết `ReadinessTests.Caller_cancellation_releases_connection`: hủy request đang bị lock, dùng app pool size 1, thả lock rồi request tiếp thành công; không tạo leak pooled connection.
- [x] Chạy `dotnet test tests/Integration/Integration.csproj --filter FullyQualifiedName~ReadinessTests`: RED; implement check + route mapping/DI. Missing history permission/SQL state được phân loại an toàn; không tạo schema từ readiness.
- [x] Chạy lại readiness, health, migration và role tests: GREEN; commit `feat(platform): add bounded postgres and schema readiness`.

## Task 5: Operational errors và secret-safe diagnostics

**Files:** Tạo `OperationalErrors.cs`, `OperationalSecurityTests.cs`; sửa API/migrator/config logging nếu cần.

**Interfaces:**
- `OperationalErrors.WriteProblemAsync(HttpContext context, int status, string reasonToken, CancellationToken cancellationToken) : Task`.
- `OperationalErrors.UseOperationalErrors(this WebApplication app) : WebApplication` đăng ký server correlation trước exception handler/routes; GUID hex mới mỗi request, response `X-Correlation-Id` khớp body `correlationId`.
- Problem `type` là absolute URI `urn:crm:problem:<lowercase-reason-with-hyphens>`, title là safe constant, status=503/500, reasonToken theo spec; không thêm raw detail. Error logs chỉ gồm safe event/reason/correlation, không exception/connection string. Startup configuration invalid và migrator failure có safe stderr JSON reason/correlation.

- [x] Viết tests cho missing/malformed connection setting, wrong password với sentinel secret sinh runtime; capture HTTP + application/framework logs + process stdout/stderr trong memory; assert không sentinel/password/connection string/provider exception. Không print captured output thô khi assertion fail; report only violated key/category.
- [x] Viết `OperationalSecurityTests.Unexpected_exception_returns_safe_problem`: chỉ inject throwing readiness implementation trong test DI (không debug endpoint production); assert 500 `INTERNAL_ERROR`, đúng schema và correlation.
- [x] Viết `OperationalSecurityTests.Caller_cannot_set_server_correlation`: gửi `X-Correlation-Id` forged và giá trị 1024 ký tự; request vẫn nhận GUID server khác input, lỗi body khớp header. Assert no-store ở live/ready/problem.
- [x] Viết `OperationalSecurityTests.Missing_configuration_exits_without_listening`: chạy API process không connection config, exit nonzero với safe reason/correlation; không mở host để trả liveness giả thành công.
- [x] Chạy `dotnet test tests/Integration/Integration.csproj --filter FullyQualifiedName~OperationalSecurityTests`: RED. Implement validation/middleware/log filters tối thiểu, gồm hạn chế EF/Npgsql diagnostics chứa thông tin kết nối. Không tắt hết observability; safe reason/correlation vẫn phải có.
- [x] Chạy toàn bộ integration suite và contract suite: GREEN; đối chiếu operational HTTP payload với schema trong Python fixtures và assert runtime fields/status/media type trong .NET. Commit `fix(platform): enforce safe operational errors and correlation`.

## Task 6: Clean-checkout smoke, evidence và handoff

**Files:** Tạo `scripts/backend-smoke.sh`, hoàn thiện verification file; sửa `README.md`, `docs/engineering/03-ACCEPTANCE-MATRIX.md`, spec status thành đã duyệt phạm vi/lập plan (không đánh dấu runtime pass trước khi có kết quả).

**Interfaces:** Script dùng DB/application/migration settings đã provision từ env hoặc secret store; không `set -x`, không echo values, không credentials trong argv. Chạy runner, API process và curl/assert health; trap dọn process ngay khi lỗi; không tạo/xóa DB của người dùng. Nếu cần container/roles disposable dùng test fixture, không mở rộng thành Compose.

- [x] Viết smoke script + hướng dẫn prerequisites, provision identities, credential injection, migration/retry/rollback. Phân biệt test-only superuser provisioning với application/migrator; DB mới dành riêng cho platform. Ghi rõ chưa hỗ trợ chạy nhiều migrator đồng thời.
- [x] Chạy `dotnet restore CrmPlatform.sln --locked-mode` sau lần restore đầu sinh lockfiles; `dotnet build CrmPlatform.sln -c Release --no-restore`; `dotnet test CrmPlatform.sln -c Release --no-build --no-restore`. Expected: tất cả pass, không skipped integration tests vì thiếu Docker.
- [x] Chạy `python -m unittest discover -s tests/Contract -v` với venv đã cài đúng `requirements-contracts.lock`; `git diff --check`. Expected: tất cả pass, business contract không breaking diff.
- [x] Chạy smoke trong checkout/export sạch của tracked source có đủ candidate files, dependencies được restore locked; `bash scripts/backend-smoke.sh`. Expected: API process thật live/ready 200 sau migration. Không gọi script vào DB production; lưu pass/fail đã redacted. Nếu dùng source snapshot trước commit cuối, ghi snapshot hash rồi final commit SHA riêng.
- [x] Điền W0P1-01→10 vào verification report: command, versions/digest, RED/GREEN, test counts, environment, migration rollback evidence, limitations/local-only. Coverage: 01 Tasks 1/6; 02 Tasks 2/3; 03 Tasks 1/4; 04–05 Task 4; 06 Task 2; 07 Task 3; 08 Tasks 3/5; 09 Task 4; 10 Tasks 1/6.
- [x] Cập nhật README theo runtime thực tế; acceptance matrix chỉ bổ sung evidence phần Plan 1 Wave 0. Giữ OIDC/tenant/business/remote E2E/restore OPEN; không coi DB outage test là DR drill.
- [x] Reviewer chỉ đọc diff theo AGENTS/spec và privileges/secret paths. Findings cần sửa phải có regression test phù hợp; chạy lại suite bị ảnh hưởng rồi full required verification. Commit explicit files với message `docs(platform): record backend foundation verification`; không merge/push.

## Self-review của plan

- Các phần scope/ownership/versions/contracts/migrations/readiness/errors/rollback/evidence của spec đều có task phụ trách.
- Năm Review Focus có test ở Tasks 2/4/5. W0P1-01→10 được map trong Task 6.
- Interfaces giữa API/infrastructure/migrator/test harness dùng một bộ tên; không business module dependency hoặc DbContext domain chung.
- Exact patch/image selection được giao builder đúng spec và bắt buộc ghi nguồn tại Task 1; plan chưa cài SDK/packages hoặc chạy runtime tests.
- Trình tự RED/GREEN áp dụng từng deliverable; chỉ bootstrap harness tối thiểu trước RED, không coi lỗi môi trường là test RED.

## Execution handoff

Người dùng đã chọn SDD tuần tự trên feature branch trong checkout hiện có, không worktree. Một coding agent tại một thời điểm; controller thực hiện task review và final read-only review. Task/final reviews đã hoàn tất; F1/F2 được sửa và scoped re-review xác nhận không còn findings. Controller chạy lại: 24 integration và 32 contract tests pass, build không warning/error. Giữ branch, không merge/push/deploy.
