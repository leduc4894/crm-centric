# Wave 1 Plan 1 — Identity and Tenant Foundation Implementation Plan

> Thực hiện tuần tự bởi một coding agent, theo TDD. Mỗi task có commit nhỏ sau khi kiểm tra; không chạy song song các coding agent.

**Goal:** Xác thực bearer token, liệt kê tenant của principal và tạo đường sử dụng tenant context được xác minh, với RLS và integration tests PostgreSQL thật.

**Architecture:** ASP.NET Core xác minh JWT bằng discovery/JWKS chuẩn; repository chỉ nhận issuer/sub đã xác minh. Một hàm SQL `SECURITY DEFINER` hẹp tra membership trước khi có tenant context; các truy vấn tenant-bound sau đó chạy trong transaction có `set_config(..., true)`, kèm predicate tenant và RLS. List tenant là truy vấn bootstrap theo principal, không cần header.

**Tech stack:** .NET SDK 10.0.403, ASP.NET Core JWT bearer, EF Core 10, PostgreSQL/Npgsql, xUnit, Testcontainers; fake OIDC issuer hiện có.

**Spec:** [Wave 1 design](../specs/2026-11-15-wave-1-identity-tenancy-design.md); [access decisions](../../architecture/07-P0-ACCESS-AND-STATE.md); [contract](../../contracts/crm-api.openapi.yaml).

## Phạm vi và bất biến

- Chỉ `GET /api/v1/tenants` là public business endpoint của plan; chưa có tenant creation, invite, role mutation hay browser login. Tenant context được kiểm thử qua test-only endpoint dùng cùng service, không xuất bản endpoint thử nghiệm.
- Issuer/sub là identity, email không cấp membership; JWT phải kiểm tra issuer, audience, signature, exp, nbf và thuật toán. Không log bearer token hoặc secret. Lỗi trả Problem có reason token/correlation ID.
- App role không superuser, BYPASSRLS, owner bảng hay quyền DDL. RLS là defense-in-depth, không thay tenant predicate. Không tin `X-Tenant-Id` cho tới khi membership được xác minh.
- Tất cả test database dùng Testcontainers PostgreSQL và application role thật; không dùng in-memory DB. Không sửa contract có tính phá tương thích; nếu cần phải dừng và theo ADR/consumer process.
- `SECURITY DEFINER` không thể chứng thực issuer/sub do client DB cung cấp: API xác thực token và là bên duy nhất được phép giữ app credential. Bị chiếm app credential có thể gọi hàm với principal tùy ý; RLS/context không bảo vệ trước arbitrary SQL bằng credential đó. Không tuyên bố ngược lại. Hàm chỉ trả các trường cần thiết, không ghi; đặt `search_path` cố định, schema-qualify mọi đối tượng, revoke PUBLIC, giới hạn execute cho app role; kiểm thử các quyền này. Dữ liệu nhạy cảm hơn và quyền ghi không đi qua hàm.
- RLS trên membership bật `ENABLE ROW LEVEL SECURITY` cho app role; owner migrator được phép bypass để thực hiện lookup qua definer. Không dùng `FORCE ROW LEVEL SECURITY` ở bảng này khi chưa thiết kế role riêng cho definer; kiểm thử app role không sở hữu bảng và không có bypass. Các bảng tenant-bound về sau phải có chính sách phù hợp riêng.

## Review focus

1. Token sai issuer/audience/chữ ký/alg, expired/nbf, claim thiếu hoặc rỗng → 401, không gọi lookup; kiểm thử Task 1 và 4.
2. Membership revoked/suspended hoặc tenant suspended → không cấp tenant context; kiểm thử Task 2–3. Transaction tenant giữ row lock trên membership tới commit/rollback: revoke chờ transaction hiện tại, request sau khi revoke commit bị từ chối; Task 3 kiểm thử hai connection. Không cache authorization.
3. Connection pool sau commit, rollback, exception/cancellation → không giữ tenant cũ; Task 3 kiểm thử `MaxPoolSize=1`.
4. App role gọi SQL trực tiếp → không đọc/ghi bảng memberships ngoài RLS, không execute hàm bằng PUBLIC, không thay đổi hàm/schema; Task 2 kiểm thử. Hàm cho phép principal bất kỳ theo trust boundary đã nêu.
5. Cursor/limit malformed, principal khác hoặc membership bị thu hồi giữa hai trang → không rò rỉ tenant; Task 4 kiểm thử giới hạn, cursor và thay đổi membership.

## Toolchain trước khi bắt đầu

- [ ] Xác minh `dotnet --version` là `10.0.403` và `docker info` thành công; không in connection string/secret. Nếu host thiếu SDK, dùng SDK đã provision hoặc image digest trong `infra/compose/images.lock.env`; Testcontainers phải truy cập được Docker daemon và mapped ports. Không khẳng định test đã chạy chỉ vì image có sẵn.
- [ ] Đọc `AGENTS.md`, handoff hiện hành, `tests/Integration/Fixtures/{PostgresFixture,FakeOidcIssuer,ApiFactory}.cs`, `DatabaseRoleGrants.cs`, migration hiện hành và `crm-api.openapi.yaml` trước khi sửa. Dùng `dotnet restore CrmPlatform.sln --locked-mode` trước regression; khi thêm package cập nhật locks có chủ đích rồi chạy lại locked restore.

## Task 1: JWT validation và Problem responses

**Files:** `Directory.Packages.props`, `src/Platform.Api/Platform.Api.csproj`, lockfiles chịu ảnh hưởng; tạo `src/Platform.Api/Identity/OidcOptions.cs`, `Identity/AuthenticationSetup.cs`; sửa `src/Platform.Api/Program.cs`, `Operations/OperationalErrors.cs`; tạo `tests/Integration/OidcAuthenticationTests.cs`, mở rộng `tests/Integration/Fixtures/FakeOidcIssuer.cs` khi cần.

**Interface:** `AddPlatformAuthentication(IServiceCollection, IConfiguration)` cấu hình JWT bearer; `OidcOptions` chứa exact issuer, audience, metadata address và cờ chỉ cho phép HTTP loopback trong development/test. Chuẩn hóa claim mapping tắt để lấy đúng `iss`/`sub`. Test-only route được đăng ký từ test host, yêu cầu authentication và trả principal không chứa token.

- [ ] Viết test valid RS256 trả 200; sai issuer/audience/signature, thiếu `sub`, expired, future `nbf`, `alg=none` và HS/RSA confusion trả 401 Problem với `reasonToken=AUTHENTICATION_REQUIRED` và correlation ID. Không assert hay in raw token. Chạy `dotnet test tests/Integration/Integration.csproj --filter FullyQualifiedName~OidcAuthenticationTests` và ghi RED do thiếu authentication.
- [ ] Pin `Microsoft.AspNetCore.Authentication.JwtBearer` phiên bản `10.0.14` trong central package management, thêm package reference, cập nhật lockfiles bằng restore thông thường rồi kiểm tra `dotnet restore CrmPlatform.sln --locked-mode`. Dùng middleware chuẩn, chỉ cho phép RS256; validate issuer/audience/lifetime/signing key, yêu cầu `exp`, `sub` và `iss` không rỗng, clock skew 0. Metadata HTTP chỉ được dùng khi environment Development hoặc Testing và host là loopback; production bắt buộc HTTPS; issuer chính xác không bị thay bằng backchannel address. Lỗi cấu hình dừng trước listener với reason token/correlation ID.
- [ ] Test issuer xoay signing key và JWT mới được chấp nhận sau refresh JWKS; discovery unavailable không cho token chưa xác thực qua. Không assert tức thời key cũ bị thu hồi khi cache còn hợp lệ: ghi rõ thời gian cache/refresh và kiểm tra cấu hình. Thêm validation redirect/metadata theo thư viện và không cho redirect sang HTTP trong production.
- [ ] Chạy lại focused test GREEN; chạy `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~OidcAuthenticationTests|FullyQualifiedName~HealthTests|FullyQualifiedName~OperationalSecurityTests'` và ghi kết quả. Health endpoints giữ public; các test test-only không thay production surface.
- [ ] Commit scoped files `feat(identity): validate oidc bearer tokens`.

## Task 2: tenant/membership schema và restricted bootstrap

**Files:** `src/Platform.Infrastructure/Persistence/PlatformDbContext.cs`, migration EF mới và snapshot trong `Persistence/Migrations/`, `DatabaseRoleGrants.cs`; tạo `src/Platform.Infrastructure/Identity/MembershipLookup.cs`; tạo `tests/Integration/TenantSchemaTests.cs`.

**Interface:** `MembershipLookup.ListAsync(NpgsqlConnection, string issuer, string subject, Guid? afterTenantId, int take, CancellationToken)` trả tối đa `take` hàng `(Guid Id, string Name, string Status)` cho membership active; hàm SQL `platform.list_principal_tenants(text,text,uuid,integer)` `SECURITY DEFINER`, `STABLE`, `search_path = pg_catalog`, dùng tên schema đầy đủ, khóa thứ tự UUID và giới hạn 1..101. Hàm thứ hai `platform.lock_active_membership(text,text,uuid)` trả bool và khóa tenant rồi membership `FOR SHARE` trong transaction gọi; không đánh dấu STABLE nếu SQL/locking không cho phép. Chỉ trả tenant active cho tenant context; list tuân theo mô tả contract active membership và trả status tenant thực.

- [ ] RED Testcontainers: migrate DB, seed tenant A/B và membership với migration identity; xác nhận unique `(tenant_id,issuer,subject)`, foreign key tenant, status/role hợp lệ, `version>0`, tên 1..200; app role không trực tiếp đọc membership khi thiếu context, không DML/DDL, không owner/bypass; PUBLIC không execute bootstrap. Kiểm tra migration upgrade từ schema cũ và rollback trong DB thử nghiệm. Chạy `dotnet test tests/Integration/Integration.csproj --filter FullyQualifiedName~TenantSchemaTests`.
- [ ] Tạo `platform.tenants(id uuid PK, name text, status text, created_at timestamptz)` và `platform.memberships(tenant_id uuid, id uuid, issuer text, subject text, role text, status text, version bigint)`, composite unique `(tenant_id,id)`, unique `(tenant_id,issuer,subject)`, FK tới tenant, check constraints. Roles: owner/admin/agent/operator; status membership active/inactive, tenant active/suspended. Đặt RLS membership theo transaction-local `crm.tenant_id` với `NULLIF(current_setting('crm.tenant_id',true),'')::uuid`; invalid value fail closed. Thu hồi mặc định PUBLIC. Cấp app `SELECT` có RLS trên membership và EXECUTE chỉ trên hai hàm; không cấp truy cập trực tiếp tenants hoặc DML. Cập nhật grants idempotent của migrator.
- [ ] Xác nhận bootstrap chỉ list membership active của issuer/sub truyền vào; không trả membership role hay issuer/sub của người khác; không trả hàng nếu thiếu identity; search_path spoof, direct DML, PUBLIC execute và app role schema CREATE bị từ chối. Kiểm tra chính xác app có thể truyền principal tùy ý như trust boundary công bố, không viết test khẳng định điều không thể bảo đảm.
- [ ] Chạy focused GREEN và `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~TenantSchemaTests|FullyQualifiedName~MigrationTests|FullyQualifiedName~RolePrivilegeTests'`; kiểm tra rollback migration xóa function trước tables và không phá initial migration. Commit `feat(tenancy): add restricted membership lookup`.

## Task 3: verified tenant transaction và RLS

**Files:** tạo `src/Platform.Infrastructure/Identity/VerifiedTenantTransaction.cs`, `tests/Integration/TenantIsolationTests.cs`; dùng `MembershipLookup` và `PostgresFixture`.

**Interface:** `VerifiedTenantTransaction.BeginAsync(NpgsqlConnection connection, string issuer, string subject, Guid tenantId, CancellationToken ct)` trả transaction đã mở khi hàm `lock_active_membership` xác nhận active membership và tenant active; trên cùng connection/transaction gọi `set_config('crm.tenant_id', tenantId.ToString(), true)`. Khi không có quyền rollback/dispose trước khi báo lỗi; caller sở hữu transaction và connection. Không cho nested transaction; không nhận issuer/sub trực tiếp từ HTTP headers.

- [ ] RED: với principal A/B, tenant A/B, kiểm tra A không đọc B qua app role, context thiếu fail closed, header tenant B không tạo context cho A; cùng principal active được vào A; inactive membership và suspended tenant bị từ chối. Chạy `dotnet test tests/Integration/Integration.csproj --filter FullyQualifiedName~TenantIsolationTests`.
- [ ] Implement service, tenant predicate và transaction-local setting. Dùng `FOR SHARE` trên membership và tenant trong cùng transaction; thứ tự khóa tenant rồi membership nhất quán với các mutation tương lai. Không giữ connection/transaction sau failure. Nếu revoke bắt đầu sau lock, nó đợi transaction hoàn tất; request sau revoke commit thất bại. Không hứa ngắt request đang chạy.
- [ ] Test hai connection cho race revoke/authorization; pool size 1 qua commit, rollback và exception/cancellation rồi kiểm tra context rỗng, không đọc dữ liệu tenant trước. Test cross-tenant INSERT/UPDATE/DELETE bị cấm bởi grants và RLS phù hợp; không coi lỗi quyền là bằng chứng duy nhất của RLS SELECT.
- [ ] Chạy focused GREEN cùng `TenantSchemaTests`, `MigrationTests`, `RolePrivilegeTests`; commit `feat(tenancy): bind verified context to transaction`.

## Task 4: contract-compatible tenant list

**Files:** tạo `src/Platform.Api/Identity/TenantEndpoints.cs`, `TenantCursor.cs`, `tests/Integration/TenantEndpointsTests.cs`; sửa `Program.cs` và `OperationalErrors.cs`.

**Interface:** `MapTenantEndpoints(IEndpointRouteBuilder)` map `GET /api/v1/tenants`, yêu cầu bearer principal; `TenantPage` chỉ gồm `items` và `nextCursor` như OpenAPI. Query `limit` mặc định 20, 1..100; lấy `limit+1` để xác định trang tiếp. Cursor base64url UTF-8 canonical UUID của tenant cuối, tối đa 2048 ký tự; decode nghiêm ngặt, rồi xác minh tenant cursor vẫn thuộc active membership của principal trong cùng truy vấn snapshot. Cursor chỉ là vị trí, không cấp quyền; principal khác, cursor giả hoặc bị thu hồi trả 400 `CURSOR_INVALID`. Không chấp nhận duplicate limit/cursor.

- [ ] RED tests: 401 khi không bearer; valid user thấy đúng tenant theo issuer/sub, không thấy membership inactive; status suspended được hiển thị nếu membership active theo contract; pagination 1/20/100, nextCursor null ở cuối; reject limit 0/101/noninteger, duplicate, cursor malformed/khác principal/thu hồi; không có dữ liệu tenant khác và header giả không thay principal. Kiểm tra JSON/Problem đúng schema, reason token và correlation ID. Chạy `dotnet test tests/Integration/Integration.csproj --filter FullyQualifiedName~TenantEndpointsTests`.
- [ ] Triển khai endpoint với SQL parameters, snapshot nhất quán cho kiểm cursor và query; không log token, subject hoặc cursor; DB unavailable trả 503 `IDENTITY_STORE_UNAVAILABLE`, không trả danh sách rỗng. Authorization failure trả 401; malformed query 400 `REQUEST_INVALID`; stale/invalid cursor 400 `CURSOR_INVALID`. Dùng chuẩn `OperationalErrors` và bảo đảm status/title chính xác.
- [ ] Chạy focused GREEN; chạy contract tests hiện có và backend integration suite. Kiểm tra test suite không truy cập production IdP và endpoint health vẫn không cần auth. Commit `feat(tenancy): list verified principal tenants`.

## Task 5: evidence và regression

**Files:** `docs/engineering/03-ACCEPTANCE-MATRIX.md`, `docs/operations/05-IDP-CONFIGURATION.md`, `docs/handoff/2026-11-15-wave-1-handoff.md`.

- [ ] Chạy `dotnet restore CrmPlatform.sln --locked-mode`, `dotnet build CrmPlatform.sln -c Release --no-restore`, `dotnet test tests/Integration/Integration.csproj -c Release --no-restore`; chạy các gate hiện hành theo workflow Wave 0 trong môi trường prepared. Ghi command, tổng pass/fail/skip và nguyên nhân thực tế; không suy đoán khi thiếu runtime.
- [ ] Đối chiếu contract/schema diff; cập nhật IdP configuration (issuer, audience, backchannel, HTTPS, rotation, secret handling), migration/rollback, trust boundary và evidence cụ thể. Chỉ đánh dấu các test đã thực thi; RT-OIDC/RT-TENANT và Wave 1 vẫn mở nếu chưa đủ exit criteria. `git diff --check` và review thay đổi; commit `docs: record identity tenant foundation evidence`.
- [ ] Wave 1 exit và P0 không đóng ở plan này. Chuyển sang plan lifecycle/membership chỉ sau khi các gate của plan này đạt hoặc blocker được báo chính xác.
