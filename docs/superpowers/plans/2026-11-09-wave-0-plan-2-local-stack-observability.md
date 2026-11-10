# Wave 0 Plan 2 — Local stack và observability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Một coding agent tại một thời điểm; reviewers chỉ đọc.

**Goal:** Từ checkout sạch, một lệnh dựng local API/PostgreSQL/Keycloak có bootstrap/migration, secret lifecycle và diagnostics kiểm chứng được.

**Architecture:** Compose có hai database/container/volume/network độc lập cho CRM và IdP; API chỉ chờ bootstrap/migration CRM. Containers dùng file secrets theo service; API giữ contract Plan 1, bổ sung JSON logging/scopes và Activity correlation. Fake OIDC chỉ là test utility; chưa triển khai CRM authentication.

**Tech Stack:** .NET 10/EF Core 10/Npgsql hiện có, PostgreSQL, Keycloak, Docker Compose (tested 5.7.0), Bash/Python 3, xUnit/Testcontainers và Python unittest contract tooling hiện có.

**Spec:** `docs/superpowers/specs/2026-11-09-wave-0-plan-2-local-stack-observability-design.md`. User đã phê duyệt triển khai trên feature branch/no-worktree; Tasks 1–7 đã implement/review.
Task 8 evidence tại [verification](../../engineering/06-WAVE-0-PLAN-2-VERIFICATION.md).
Checkbox ghi steps đã thực hiện, gồm RED failures lịch sử; không có nghĩa mọi baseline
đã pass. Task 8 và final whole-branch review đã hoàn tất: Approved, 0 Critical/Important;
[full review, controller rulings và optional follow-ups](../reviews/2026-11-10-wave-0-plan-2-final-review.md).
Local Plan 2 complete; Wave 0/P0 OPEN. Giữ branch, không merge/push/deploy.

## Global Constraints

- Không service nào truy cập trực tiếp domain DB bên kia. Không dùng chung database/schema/login; API không có admin hoặc migrator credential.
- Không cần host .NET SDK để chạy stack. Mặc định publish API `127.0.0.1:5080`, Keycloak `127.0.0.1:8080`; DB và management/health port của Keycloak không publish host.
- Không dùng `container_name`, host networking, Docker socket mount hoặc privileged container.
- Secret directory mode 0700, files mode 0600; cryptographically random. Không tạo connection string có secret trong Compose interpolation, Docker build ARG, image layer, healthcheck command hoặc argv.
- Mọi upstream image, kể cả build/runtime/probe, pin exact stable tag + digest. Giữ SDK/package pins Plan 1 trừ khi có lý do tương thích được ghi và lockfiles cập nhật.
- API/migrator runtime chạy non-root; không SDK/source/secrets trong image. Không AOT/trimming, không startup migration trong API.
- Budget mặc định startup 180 giây sau build; readiness API giữ budget 3 giây. Probes, build/pull và shutdown đều có deadline hữu hạn.
- Không có tự động `down -v`, drop DB hoặc reset realm. Volume có sẵn nhưng thiếu/mismatched secret hoặc sai ownership/ACL thì fail an toàn.
- Realm `crm-local`; clients `crm-web-local`, `demo-module-local`; Authorization Code + PKCE S256, implicit/direct password grants tắt.
- Không thêm CRM login/session/JWT authorization, tenant/membership/RLS, business schema, Worker, frontend, CI mới hoặc full telemetry stack.
- PostgreSQL integration tests dùng database thật; Compose gate chạy services thật. Không skip thiếu Docker/Compose rồi báo pass; không tạo RED giả cho hành vi đã đúng.
- Cập nhật `docs/handoff/` theo AGENTS; không merge/push/deploy từ việc plan tests pass. Wave 0/P0 vẫn OPEN.

## Review Focus

1. Volume cũ nhưng secret bị mất/đổi: không generate đè hoặc đổi password ngầm (Task 4/7).
2. Container one-shot exit 0 cũ có thể khiến migration image mới bị bỏ qua: mỗi up validate/rerun serially (Task 6).
3. Secret bind file 0600 không đọc được bởi non-root UID: kiểm tra identity/permission thật, không chmod public để sửa (Task 3).
4. Warm Keycloak startup import bỏ qua realm tồn tại: kiểm tra metadata/drift qua IdP thật, không chỉ so JSON local (Task 5/7).
5. Foreign listener, lifecycle race hoặc cleanup nhầm project: không dùng HTTP 200/PID existence làm ownership proof (Task 6/7).

## File map và ranh giới

| Files | Trách nhiệm |
|---|---|
| `src/Platform.Infrastructure/Observability/PlatformLogging.cs` | built-in JSON console registration, safe scopes/events cho hosts |
| `src/Platform.Infrastructure/Observability/ProcessDiagnostic.cs` | một JSON result/diagnostic cho CLI, giữ root fields cũ |
| `src/Platform.Api/Program.cs`, `Operations/OperationalErrors.cs` | logging composition, request scope/Activity, giữ health/Problem contract |
| `src/Platform.Migrator/Program.cs`, `Local/LocalDatabaseBootstrap.cs` | giữ default Up; thêm explicit local-bootstrap mode, không API DDL |
| `tests/Integration/{StructuredLoggingTests,FakeOidcIssuerTests,ContainerRuntimeTests,LocalBootstrapTests,KeycloakContainerTests}.cs` | test theo ranh giới runtime |
| `tests/Integration/Fixtures/{FakeOidcIssuer,ContainerImages,LocalSecretFixture,ComposeFixture}.cs` | fixtures tái sử dụng, safe captured process output |
| `tests/Integration/Compose/{LocalStackTests,LocalStackFailureTests,LocalStackSecretTests}.cs` | `[Trait("Category", "Compose")]`, suite riêng bắt buộc |
| `infra/docker/{Platform,Keycloak}.Dockerfile`, `.dockerignore` | multi-stage app targets, image Keycloak nếu cần build health/UID config |
| `infra/compose/images.lock.env`, `compose.yaml` | image refs công khai, services/networks/volumes/health/secrets |
| `infra/compose/entrypoints/{platform,postgres,keycloak,healthcheck}.sh` | file-secret adapters/probe, không general-purpose script loader |
| `infra/keycloak/crm-local-realm.json` | client/realm metadata không credentials/users |
| `scripts/{dev-up,dev-down,dev-smoke}.sh`, `scripts/local_stack.py` | wrappers và orchestration có timeout/project lock |
| `scripts/local/{secrets,compose,realm}.py` | secret state, scoped Docker calls, realm inspection; không framework plugin |
| `docs/operations/{04-LOCAL-DEVELOPMENT,05-IDP-CONFIGURATION}.md` | vận hành local, production IdP configuration boundary |
| `docs/engineering/06-WAVE-0-PLAN-2-VERIFICATION.md` | W0P2 evidence và versions/commands/results |

Thêm helper/file nhỏ khi cần giữ một trách nhiệm/file; không split domain projects hoặc xây telemetry framework. Namespace theo project. Package mới chỉ khi thật cần, version kiểm chứng và central pin/lock. `images.lock.env` không bắt đầu bằng `.env` nên không bị rule ignore hiện có che mất.

## Command và state contract dùng chung

- Public commands: `bash scripts/dev-up.sh`, `dev-down.sh`, `dev-smoke.sh`; wrappers gọi `python3 scripts/local_stack.py up|down|smoke`, không shell eval.
- Cấu hình công khai: `LOCAL_STACK_PROJECT`, `LOCAL_STACK_STATE_DIR`, `LOCAL_API_PORT`=5080, `LOCAL_IDP_PORT`=8080; `LOCAL_START_TIMEOUT_SECONDS`=180, `LOCAL_BUILD_TIMEOUT_SECONDS`=900. Validate project name/ports/paths trước mutate; deadline build có thể override hữu hạn, được ghi trong evidence.
- Default project: tên `crm-local-<hash-of-canonical-checkout-path>`; default state `.local/stack/<project>/`, gitignored và ngoài Docker context. Custom state directory phải absolute/canonical, rỗng hoặc có đúng manifest ownership; không symlink secret files/directories và không ghi đè foreign directory. Một OS advisory file lock theo Docker context + project giữ suốt up/down; lock nằm trong per-user shared directory `/tmp/crm-centric-<uid>/locks/` mode0700, không theo checkout/state path để hai checkout trỏ cùng project vẫn serialize. Validate owner/no symlink trước dùng; lock release bởi kernel khi process chết, không dựa vào marker PID cũ. Smoke lấy lock nonblocking trong lúc inspect/probe để không race với up/down. Up gọi shared inspection function dưới lock đang giữ, không spawn dev-smoke CLI để tự reacquire lock và deadlock. Concurrent lifecycle fail `LOCAL_LIFECYCLE_BUSY`, không chờ vô hạn; chỉ hỗ trợ local Docker daemon, không claim multi-host locking.
- State manifest không chứa credentials: schema version, project identity, ports, secret file names và trạng thái provisioning. Không coi manifest host đơn độc là bằng chứng volume trống; inspect Docker project volumes và marker trong DB trước khi quyết định initialize/retry.
- Nếu cả state lẫn volumes không tồn tại: generate secrets. Nếu volume đã tồn tại nhưng files/manifest thiếu hoặc identity không khớp: fail `LOCAL_SECRET_STATE_MISMATCH`. Nếu state mới tạo và bootstrap bị ngắt: reuse đúng secrets, validate phần đã tồn tại rồi tiếp tục phần thiếu; không rotate/reset. Unknown existing DB/schema/role ownership fail, không chiếm dụng.
- Owned log `service` values: `crm-api`, `crm-migrate`, `crm-bootstrap`; lifecycle helpers `local-stack`. Không dùng client-controlled service/event labels.
- App CLI root fields `status` hoặc `reasonToken`/`correlationId` giữ tương thích; script success JSON có `status`, `eventName`, `service`, `correlationId`, non-secret project/endpoints. Fail dùng đúng token list trong spec, exit nonzero; captured raw errors không print.
- Tests có .NET SDK để chạy harness; dev commands không được gọi host `dotnet`. Integration command non-Compose: `dotnet test CrmPlatform.sln -c Release --filter 'Category!=Compose'`; mandatory Compose command: cùng command với `--filter 'Category=Compose'`. Ghi count riêng, không để filter chọn zero tests rồi báo pass.

---

## Task 1: JSON logs, safe process diagnostics và Activity correlation

**Files:** Tạo observability helpers và `StructuredLoggingTests.cs`; sửa API/migrator composition và operational error scopes; bổ sung safe process capture vào fixture hiện có nếu cần.

**Interfaces:** `PlatformLogging.Configure(ILoggingBuilder logging, string service): void`; `PlatformLogging.BeginOperation(ILogger logger, string service, string eventName, string correlationId): IDisposable?`; `ProcessDiagnostic.Write(TextWriter writer, string service, string eventName, string correlationId, string? reasonToken = null, string? status = null): void`. Activity IDs lấy từ `Activity.Current`, không giả từ GUID. CLI vẫn đúng một root JSON result trên stream tương ứng, không phá tests đang parse process result.

- [x] Viết `StructuredLoggingTests.Error_json_has_http_correlation_and_real_activity`: HTTP wrong-password/controlled unexpected error qua host thật hoặc WebApplicationFactory; capture console/scopes, parse mỗi owned JSON entry; assert UTC timestamp/severity, service/eventName, reasonToken và correlation khớp Problem/header; traceId 32 hex/spanId 16 hex khớp Activity thực.
- [x] Viết `Forged_correlation_and_malformed_trace_are_safe`, `Query_headers_and_secret_sentinels_are_absent`, `Cli_result_shape_stays_compatible`. Assert server correlation khác input, valid traceparent được xử lý; query/Authorization/Cookie/sentinel không vào logs; vẫn có safe app và framework events; không chỉ assert output rỗng.
- [x] Chốt assertions cho owned error entry và HTTP result (field placement trong JSON formatter/scopes được adapter test đọc, không tự giả Activity):

```csharp
Assert.Equal(problemCorrelationId, loggedCorrelationId);
Assert.Equal(observedActivity.TraceId.ToHexString(), loggedTraceId);
Assert.Equal(observedActivity.SpanId.ToHexString(), loggedSpanId);
```

- [x] Chạy `dotnet test tests/Integration/Integration.csproj -c Release --filter FullyQualifiedName~StructuredLoggingTests`: RED vì plain console/thiếu state/trace; giữ raw capture riêng và chỉ in tên category vi phạm. Không coi missing prerequisite/compile là RED.
- [x] Implement helpers bằng .NET JSON formatter + scopes, IncludeScopes, UTC timestamps. Filter framework request-start/finish có URL query thô; giữ safe host/routing observability và EF/Npgsql filters hiện có. Không emit health-success log mỗi probe. Tạo/dispose Activity chỉ khi host chưa cung cấp; không có exporter/collector mới.
- [x] Chạy tests trên và `dotnet test ... --filter 'Category!=Compose'`: GREEN; verify build, contract suite nếu response shape chạm tới; commit `feat(observability): add structured local diagnostics and trace correlation`.

## Task 2: Fake OIDC issuer độc lập cho tests

**Files:** Tạo `Fixtures/FakeOidcIssuer.cs`, `FakeOidcIssuerTests.cs`; chỉ test package refs/central pins và locks nếu thư viện JWT chưa có.

**Interfaces:** `FakeOidcIssuer.StartAsync(CancellationToken): Task<FakeOidcIssuer>`; properties `Issuer: Uri`, `JwksUri: Uri`; `IssueToken(FakeTokenOptions options): string`; `RotateSigningKey(bool retainPreviousKey = true): void`; `IAsyncDisposable`. Options gồm issuer override, audience, subject, issued/not-before/expires UTC; default subject `fixture-user`, audience `crm-api`. Keys RSA runtime, RS256, kid ngẫu nhiên; không token/authorize/password-grant endpoints.

- [x] Viết `Discovery_and_jwks_match_bound_issuer`, `Token_signature_verifies_using_public_jwks`, `Rotation_keeps_or_retires_previous_kid`, `Fixture_publishes_no_private_key_or_credentials`. Assert `issuer`, `jwks_uri`, public RSA n/e, không d/p/q; hai tokens giữ distinct kid; retired kid không còn JWKS; options cho expired/wrong issuer/audience được biểu đạt bằng standard token library.
- [x] Chốt assertions của discovery/key test:

```csharp
Assert.Equal(issuer.Issuer.AbsoluteUri, discovery.GetProperty("issuer").GetString());
Assert.Equal(issuer.JwksUri.AbsoluteUri, discovery.GetProperty("jwks_uri").GetString());
Assert.False(publicJwk.TryGetProperty("d", out _));
```

- [x] Chạy filter `FullyQualifiedName~FakeOidcIssuerTests`: RED với compileable fixture stub chưa có hành vi. Không implement OAuth flows để pass.
- [x] Implement loopback ephemeral Kestrel/discovery/JWKS và token generation bằng standard JWT library; vô hiệu request/token body logging; thread-safe snapshot keys cho tests; dispose keys/server. Private fixture assembly không được reference từ src.
- [x] Chạy focused tests: GREEN; inspect production publish output trong Task 3 không có fixture/test assembly. Commit `test(identity): add isolated fake OIDC issuer fixtures`; không claim CRM auth pass.

## Task 3: Pinned container builds và file-secret runtime adapters

**Files:** Dockerfiles, image lock, `.dockerignore`, entrypoints/probes, `ContainerImages.cs`, `ContainerRuntimeTests.cs`; cập nhật evidence toolchain section.

**Interfaces:** `Platform.Dockerfile` targets `api`, `migrator`; Keycloak image target runtime bật health trên management interface. Entrypoints cấp `ConnectionStrings__Platform` từ một mounted connection file cho đúng service, không nhận CS qua argv/Compose interpolation. Host env configuration Plan 1 vẫn hoạt động khi chạy không qua adapter. API probe gọi live/ready trong chính container, timeout <=5s, không lấy health của host khác.

- [x] Xác minh official image/tool metadata, pin tag+digest cho SDK/runtime/PostgreSQL/Keycloak và probe tools; SDK phải khớp global.json. Ghi exact UID/GID/secret-read strategy của từng image trước khi implement adapter. Không copy native executables từ image khác không kiểm chứng libc/architecture.
- [x] Viết `ContainerRuntimeTests.Api_uses_only_application_secret_as_nonroot`, `Unreadable_or_missing_secret_fails_closed`, `Published_images_contain_no_sdk_source_or_fixture`, `Image_config_history_and_build_context_exclude_sentinels`. Assert mounted secret có 0600, host directory 0700, process runtime nonzero UID và connection hoạt động; foreign/admin/migrator secret không được mount; publish output không chứa tests/FakeOidcIssuer. Run actual built images.
- [x] Chốt non-root/secret-boundary assertions từ inspect và actual runtime:

```csharp
Assert.NotEqual("0", runtimeUid);
Assert.DoesNotContain(adminSecretFile, apiMountedSecretFiles);
Assert.DoesNotContain(migratorSecretFile, apiMountedSecretFiles);
```

- [x] Chạy focused tests: RED với minimal buildable targets/harness trước adapters; không gọi Dockerfile chưa tồn tại là behavioral RED. Build prerequisites/pins failure ghi riêng.
- [x] Implement multi-stage locked restore/publish và small adapters. COPY allowlist; non-secret UID build args được phép. Dùng image-supported ownership/ACL hoặc short root handoff rồi drop privileges theo spec; không chmod secrets 0644. PostgreSQL adapter đọc file lúc bootstrap và giữ canonical image entrypoint/privilege drop; Keycloak data directory phải writable bởi runtime UID đã test.
- [x] Chạy filter `FullyQualifiedName~ContainerRuntimeTests`, inspect image/config/layers với captured sentinel checks: GREEN. Secret không build ARG/ENV/layer/health args; tests không dump raw history. Commit `build(platform): add pinned runtime containers and secret adapters`.

## Task 4: Secret state và idempotent local database bootstrap

**Files:** Tạo `scripts/local/secrets.py`, shared diagnostic helper nếu cần, `Local/LocalDatabaseBootstrap.cs`, `LocalBootstrapTests.cs`, `LocalSecretFixture.cs`; sửa migrator Program thêm local mode, `Fixtures/MigratorProcess.cs` cho explicit local-mode args/env, `.gitignore` thêm `.local/`, entrypoint mapping bootstrap secrets.

**Interfaces:** Python `ensure_secret_state(state_dir: Path, project: str, existing_volumes: bool) -> SecretState`; actual randomness dùng standard crypto API, manifest version 1. CLI `Platform.Migrator.dll --local-bootstrap` chỉ mode mới, default Up không đổi. Typed `LocalDatabaseBootstrap.RunAsync(LocalBootstrapOptions options, CancellationToken): Task`; options admin connection + fixed DB/role identifiers + secret-file references + project marker, không passwords trong command arguments. Bootstrap chỉ dùng admin secret trong `crm-bootstrap` service; existing default migrator không nhận nó. `LocalBootstrapOptions` map runtime settings `LocalBootstrap__AdminConnectionString`, `LocalBootstrap__DatabaseName` (crm), `LocalBootstrap__ProjectId`, `LocalBootstrap__MigratorSecretFile`, `LocalBootstrap__ApplicationSecretFile`; role names fixed `crm_migrator`/`crm_app`. Adapters đọc mounted files trước khi populate private process env. Tests gọi CLI qua helper, không reference migrator executable project vào tests để tránh trùng global Program với API. Extend `MigratorProcess.RunAsync(string? connectionString, string? applicationRole, bool api = false, IReadOnlyList<string>? arguments = null, IReadOnlyDictionary<string, string?>? environment = null): Task<MigratorProcess.Result>` tương thích call sites cũ; args chỉ có mode/flags công khai, không secret.

- [x] Viết tests secret generation/reuse/0700+0600/symlink/foreign-directory rejection; persisted-volume+missing-file và changed credentials fail mismatch; không print value. Viết PostgreSQL tests `Bootstrap_is_idempotent_and_restricted`, `Partial_bootstrap_retries_without_reset`, `Existing_unowned_database_is_rejected`; tạo real non-owner migrator/app với đúng ACL Plan 1, no PUBLIC CREATE/TEMPORARY, không elevated memberships.
- [x] Viết `Existing_volume_with_wrong_secret_or_acl_does_not_repair_silently`: kiểm chứng DB credentials thực, không chỉ checksum manifest. Sentinel table/data dùng fixture admin, không domain migration; warm bootstrap giữ nguyên. Interrupted fixture có đúng ownership marker/roles nhưng thiếu grants phải tiếp tục có kiểm soát; unknown pre-existing ownership phải fail.
- [x] Chốt filesystem/permission assertions trên files thật; warm test kiểm tra bytes giữ nguyên mà không in bytes:

```csharp
Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(secretFile));
Assert.True(originalSecretBytes.AsSpan().SequenceEqual(currentSecretBytes), "Secret changed during warm start.");
Assert.False(applicationCanCreateSchema);
```

- [x] Chạy filter `FullyQualifiedName~LocalBootstrapTests`: RED; implement generation/validation và typed bootstrap. Initial state được ghi vào private temporary directory rồi publish atomically; không overwrite files của state cũ, không để manifest complete khi files còn thiếu. Quote SQL identifiers/literals bằng safe library logic; không passwords trong SQL logs/server error statements. Local DB startup config phải ngăn statement/error logging lộ CREATE ROLE password, giữ safe operational failure. DB/project marker kỹ thuật không phải tenant/domain entity; namespace riêng `_local` và không thêm vào application migrations.
- [x] Chạy focused tests: GREEN; default MigrationTests/RolePrivilegeTests và security tests vẫn pass. Mode mới emit safe diagnostic một JSON; không destructive reset hoặc arbitrary DB repair. Commit `feat(local): provision isolated database identities and persistent secret state`.

## Task 5: Keycloak realm, canonical issuer và warm-state validation

**Files:** Realm JSON, Keycloak entrypoint/image adjustments, `scripts/local/realm.py`, `KeycloakContainerTests.cs`, `docs/operations/05-IDP-CONFIGURATION.md`.

**Interfaces:** `inspect_realm(public_base_url: str, admin_secret_files: AdminSecretFiles, timeout_seconds: float) -> RealmStatus`; statuses ready/drift/unavailable mapped exact LOCAL tokens. Canonical issuer `http://localhost:<LOCAL_IDP_PORT>/realms/crm-local`. Clients exact redirect placeholders: web `http://localhost:5173/auth/callback` / origin `http://localhost:5173`; demo `http://localhost:5180/auth/callback` / origin `http://localhost:5180`. Không wildcard, không chạy callback giả.

- [x] Viết tests real Keycloak + PostgreSQL riêng: `Cold_import_has_two_pkce_clients_and_canonical_discovery`, `Warm_start_preserves_realm_and_detects_drift`, `Host_port_override_changes_discovery_consistently`. Assert public clients, standard flow, S256, implicit/direct grant off, metadata/JWKS public, không seeded users/client secret/private key.
- [x] Chốt observable realm assertions từ Keycloak Admin API:

```csharp
Assert.Equal("S256", client.GetProperty("attributes").GetProperty("pkce.code.challenge.method").GetString());
Assert.False(client.GetProperty("directAccessGrantsEnabled").GetBoolean());
Assert.False(client.GetProperty("implicitFlowEnabled").GetBoolean());
```

- [x] Chạy focused filter: RED với baseline realm/config chưa đúng. Implement pinned health-ready probe thực (không giả định curl trong vendor image), fixed hostname/port và import metadata. Không dùng import file làm evidence warm migration; inspect actual persisted realm/client metadata.
- [x] Warm inspection dùng bootstrap admin secret chỉ trong trusted local helper, obtain admin-management token qua body trong memory/file input, không argv/log; scope master/admin-cli riêng, không bật password grant cho CRM clients hoặc lấy end-user token để chứng minh login. Không persist admin token vào manifest. Không mount CRM secrets vào IdP. idp-db có admin secret riêng; init tạo Keycloak DB LOGIN non-superuser, sở hữu DB/schema dành riêng nó, Keycloak không mount PostgreSQL admin secret. HTTP chỉ local profile.
- [x] Chạy focused tests: GREEN. Document production TLS/issuer/audience/PKCE/JWKS rotation/MFA/session/logout/proxy trust/DB-secret management và managed IdP alternative; no actual production deployment. Commit `feat(local): configure isolated Keycloak realm and issuer checks`.

## Task 6: Compose dependency gates và lifecycle commands

**Files:** `compose.yaml`, shell wrappers, `local_stack.py`, `scripts/local/compose.py`, `ComposeFixture.cs`, `Compose/LocalStackTests.cs`, local operations guide.

**Interfaces:** Services exactly crm-db, crm-bootstrap, crm-migrate, crm-api, idp-db, keycloak; API only CRM network, Keycloak only IdP network. `run_local_stack(action: str, config: LocalStackConfig) -> int`; config contract above. Compose fixture creates unique project/state/ports per test, runs real CLI with output captured, cleans only test-owned labeled resources; developer commands never remove volumes.

- [x] Viết `[Trait("Category", "Compose")]` happy-path tests: `Cold_up_needs_no_host_sdk`, `Warm_up_revalidates_oneshots_and_preserves_data`, `Down_up_keeps_secrets_volumes_and_realm`, `Sql_and_secret_mount_boundaries_hold`. Assert expected startup health/exits, history exactly one migration, app permissions thực và two DB volume/network sets distinct; fixture admin-only sentinel survives restart.
- [x] Chốt cold/warm acceptance assertions:

```csharp
Assert.Equal(0, upExitCode);
Assert.Equal(HttpStatusCode.OK, liveStatus);
Assert.Equal(HttpStatusCode.OK, readyStatus);
Assert.Equal(originalSentinelValue, persistedSentinelValue);
```

- [x] Chạy `dotnet test tests/Integration/Integration.csproj -c Release --filter 'Category=Compose&FullyQualifiedName~LocalStackTests'`: RED khi orchestration chưa đáp ứng; Docker/Keycloak/download prerequisites không tính RED. Host SDK unavailable trong environment của command-under-test nhưng tests vẫn chạy bằng SDK harness.
- [x] Implement dependency conditions DB healthy → bootstrap exit0 → migrate exit0 → API; IdP separate chain. Mỗi up serialize, explicitly rerun bootstrap/migrate bằng current image, stop/recreate owned API trước migration update, không để previous ready API che failure. Giữ volumes/secrets; không dùng exited0 cũ để skip. Up validate readiness toàn stack sau build, startup timeout180; build timeout900, probes finite, log timings riêng.
- [x] Implement down giữ volumes/state; finite shutdown grace20s rồi force-stop đúng owned project resources. Smoke derives container ID/project labels/loopback port mapping rồi probes endpoint và container-local health, không spawn host API; foreign/unowned listener/config mismatch fail. Không dùng docker socket bên trong service để tự kiểm chứng.
- [x] Chạy focused tests: GREEN; document up/down/smoke and SDK-free dev path. Commit `feat(local): orchestrate reproducible Compose startup and shutdown`.

## Task 7: Failure, concurrency, drift và secret-leak gates

**Files:** `Compose/LocalStackFailureTests.cs`, `Compose/LocalStackSecretTests.cs`; sửa lifecycle/bootstrap/realm/logging khi regression chỉ ra lỗi, không thêm product scope.

**Interfaces:** Reuse ComposeFixture/captured output; test-owned overrides/injections không vào default compose. Các lý do lỗi dùng đúng spec token và correlation GUID; không service reset hoặc process kill ngoài owned project.

- [x] Viết tests `Crm_db_outage_recovers_same_api_instance`, `Idp_outage_does_not_change_crm_readiness`: stop/start actual dependencies, API instance ID giữ nguyên; CRM live200/ready503 rồi200 trong DB case; live/ready200 trong IdP case, full smoke exit!=0 `LOCAL_IDP_NOT_READY`, recovery succeed.
- [x] Viết `Bootstrap_and_migration_failure_block_new_api`, `Partial_init_retry_and_mismatched_secrets_are_safe`, `Realm_drift_is_not_overwritten`: fail by actual bad role/privilege/config trong disposable stack; restore prerequisite/retry thành công, data/history/secrets intact; drift no automatic reset. Bao gồm secret file deletion khi volume vẫn tồn tại và changed app credential; không tạo image lỗi giả để claim Compose acceptance.
- [x] Viết `Foreign_listener_cannot_supply_smoke_success`, `Concurrent_lifecycle_is_rejected_including_distinct_state_paths`, `Parallel_projects_do_not_share_resources`, `Shutdown_is_bounded`: already healthy foreign listener survives failure, project lockbusy token kể cả hai checkout/state paths khác nhau cùng project/context, unique container/volumes, no hang; script bị kill không để stale lock gây bế tắc. Không dùng preflight free-port check một mình làm ownership proof.
- [x] Viết secret sentinel audit success + bad auth + bootstrap/migration/IdP-startup failure: scan captured all-service logs, CLI output, rendered Compose config, Docker Config/History và published layer files. Assert expected safe diagnostic có mặt và credentials không có; diagnostics của test chỉ nêu category, không raw capture. Check request query/header/token sentinel cũng không log.
- [x] Chốt isolation/recovery assertions, giữ diagnostic output trong memory:

```csharp
Assert.Equal(originalApiContainerId, recoveredApiContainerId);
Assert.NotEqual(0, foreignListenerSmokeExitCode);
Assert.Equal("LOCAL_LIFECYCLE_BUSY", concurrentFailureReason);
Assert.True(allCapturedOutputsExcludeSentinels, "Sensitive diagnostic category detected.");
```

- [x] Chạy filters FailureTests/SecretTests để ghi RED đúng lỗi mới; baseline GREEN regression được ghi trung thực nếu prior implementation đã đúng. Fix minimum, rerun focused then all Compose tests: GREEN. Commit `test(local): verify failure recovery isolation and secret-safe lifecycle`.

## Task 8: Clean-source evidence, docs và final review

**Files:** `docs/engineering/06-WAVE-0-PLAN-2-VERIFICATION.md`, local/IdP guide, README, backlog/engineering status/acceptance matrix, spec status, plan checkboxes và `docs/handoff/2026-11-09-wave-0-plan-2-implementation-handoff.md` (date theo session thực tế).

- [x] Task8: restore/build/contracts/local/syntax/diff chạy mới; full Compose14/non-Compose70 reuse final gates2026-11-10 sau khi xác minh source đúng `34342cb`, không rerun (controller ruling; chi tiết verification). Yêu cầu: chạy `dotnet restore CrmPlatform.sln --locked-mode`; `dotnet build CrmPlatform.sln -c Release --no-restore`; non-Compose tests và Compose tests bằng hai filter đã định nghĩa với `--no-build --no-restore`. Ghi counts riêng, zero unexpected skips/failures; phải có Compose tests được chọn. `.venv/bin/python -m unittest discover -s tests/Contract -v` PASS. `bash -n` từng new/changed shell entrypoint; Python syntax check; `git diff --check` PASS.
- [x] Commit source candidate, export chính xác SHA qua `git archive` sang thư mục mới (không git worktree). Không copy secret/volume/bin/obj/caller env. Chạy `bash scripts/dev-up.sh` với fresh project/state/volumes và không host dotnet, rồi `dev-smoke` và `dev-down`. Evidence API200/Keycloak/discovery/JWKS/ownership, retained volumes/secrets, pinned image digests và start timings. Image cache được reuse phải ghi rõ; không claim fresh image pull nếu chỉ dùng cache. Cleanup test-owned volumes ngoài dev-down chỉ trong disposable fixture, ghi rõ không phải hành vi người dùng.
- [x] Map W0P2-01→12 tới named tests/commands/artifacts: 01 Tasks3/6/8; 02 Tasks3/4/6; 03 Tasks4/6/7; 04 Tasks4/5/6/7; 05–06 Task7; 07 Task1; 08 Tasks1/3/4/5/7; 09 Task2/3; 10 Task5/7; 11 Task6/7; 12 Task8. Ghi SHA, versions/digests, Docker/Compose minimum đã test, RAM/resources, build/pull/start duration và actual pass/fail. Không thay runtime bằng config/schema validation.
- [x] Cập nhật README và backlog stale scaffold/cold-start theo evidence; remaining frontend/SDK/diff/CI Wave0 OPEN, OIDC/tenant/business/remote DemoModule/DR OPEN. Viết production IdP config rõ là hướng dẫn, chưa deployed/auth-verified.
- [x] Read-only review whole branch theo AGENTS/spec; findings có regression/verification tương ứng. Nếu source/script thay đổi, cập nhật exact source/export evidence cho bản cuối, không dùng hash/script cũ làm proof. Handoff ghi user approvals, branch/commits/uncommitted work, rulings, tests và bước tiếp theo. Commit `docs(local): record Plan 2 verification and handoff`; không merge/push/deploy.

## Self-review và coverage

- Spec topology/secrets/state/startup/IdP/fake/logging/rollback/evidence đều có task và concrete runtime tests; không thêm auth hoặc full telemetry.
- Năm Review Focus được kiểm chứng tại Tasks3/4/5/6/7; W0P2 mapping đầy đủ trong Task8.
- Interfaces project/state/CLI/fixture/service IDs nhất quán; default migration Up và host env connection path Plan1 tiếp tục tương thích.
- Secret/UID/image patch choices theo spec được builder kiểm chứng trước runtime adapter; không đoán digest/Keycloak release trong tài liệu plan.
- Secrets/Compose gate không dùng Testcontainers-only/config-only thay proof; real Compose suite là mandatory dù có command riêng.
- Lượt viết plan chỉ sửa tài liệu, không cài package, build Docker hoặc chạy Plan2 tests; mọi checkbox còn mở.

## Execution handoff

Đề xuất giữ cách thực thi SDD tuần tự như Plan1: fresh implementer/task, read-only task reviews và whole-branch review. User trước đây đã chọn branch mới/no-worktree cho Plan1; không coi đó là đã cấp implementation authorization cho Plan2. Chỉ bắt đầu sau khi người dùng review plan và yêu cầu triển khai; nếu tiếp tục chọn SDD/không worktree thì giữ đúng lựa chọn đó, không hỏi lại giữa các task.
