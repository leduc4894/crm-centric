# Wave 3 Plan 1 — Manifest, Schema and Publisher Trust Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement task-by-task. Steps use checkbox (`- [ ]`) syntax. One coding agent at a time under AGENTS.md.

**Goal:** Xác minh manifest module v2 và schema từ publisher tin cậy, rồi lưu registry snapshot bất biến có thể dùng làm đầu vào cho installation ở Plan 2.

**Architecture:** Một pipeline nội bộ fetch manifest/sidecar qua egress guard, kiểm tra chữ ký Ed25519 trên exact bytes, validate shape và semantics, fetch schema pin theo hash và lưu snapshot trong PostgreSQL. Registry là catalog các artifact đã verify, không phải installation active hay tenant approval; Plan 2 phải kiểm tra trust/revocation và grants tại thời điểm sử dụng. Không expose endpoint mới nếu contract hiện tại chưa có operation tương ứng.

**Tech Stack:** .NET 10, PostgreSQL/Npgsql/EF migrations, xUnit, Testcontainers, JSON Schema Draft 2020-12; supported stable Ed25519 và JSON Schema libraries được lựa chọn, pin tại thời điểm triển khai sau kiểm tra support/license và lockfile.

**Spec:** [Wave 3 design](../specs/2026-11-28-wave-3-integration-kernel-design.md); [callback contract](../../contracts/MODULE-CALLBACKS.md); [manifest schema](../../contracts/module-manifest.schema.json); [publisher sidecar](../../contracts/publisher-signature.schema.json); [module architecture](../../architecture/04-MODULE-EXTENSIBILITY.md); [ADR-018](../../architecture/08-ADR-018-P0-CONTRACT-BASELINE.md).

## Global Constraints

- Task 0 là gate: Wave 2 và P0 OPEN; security FAIL/deferred. Không bắt đầu code nếu work order/điều kiện áp dụng chưa được giải quyết bằng evidence và authorization phù hợp. Việc duyệt plan không tự waive acceptance hoặc security.
- TDD RED → GREEN → refactor; chỉ test liên quan local trong plan này, full suite ở Plan 9 trên GitHub CI. PostgreSQL thật/Testcontainers cho migration, transaction và concurrency. Không vô hiệu gate CI.
- Contract-first: giữ strict schema v2, `platformApiVersion` `^1.0`, release-only SemVer. Breaking diff cần ADR, migration và consumer tests; không tự rebaseline. Không sửa generated SDK thủ công.
- Manifest URL queryless/fragmentless, detached sidecar chỉ tại `manifestUrl + ".sig.json"`; HTTPS/TLS, timeout 5s, manifest ≤256 KiB, sidecar ≤4 KiB; schema ≤64 KiB, nesting ≤8. Hash và signature trên exact bytes; Ed25519 canonical base64url signature 64 bytes. Không tự implement crypto.
- Không tự cấp egress: DNS/IP/redirect checks cho mọi fetch và mọi lần kết nối; same-origin mặc định, cross-origin cần tenant-admin approval ở installation, private network chỉ operator allowlist. Tenant không thể allow metadata/loopback/link-local. Không coi JSON Schema là authorization hay SSRF protection.
- Không provision credentials, enable installation, thay đổi tenant grants, thực thi module code, webhook hoặc action trong plan này. Mọi lỗi đóng có reason token/correlation ID; không log secrets, tokens, raw payload hoặc dữ liệu nhạy cảm.
- Registry không biến manifest URL hay publisher key thành tenant trust; trust store do operator quản lý. Trong plan này chỉ lưu verified candidate không kích hoạt; policy thay đổi/revocation phải được kiểm tra lại khi Plan 2 cài đặt.

## Review Focus

1. DNS rebinding, IPv4-mapped IPv6 và mixed public/private answers: không kết nối tới địa chỉ bị cấm (Task 2).
2. JSON duplicate keys, signature encoding không canonical hoặc body đổi whitespace sau khi ký: fail closed trước publish (Tasks 1, 3).
3. Redirect, TLS hostname và retry không bypass policy hoặc nối tới IP khác IP đã duyệt (Task 2).
4. Schema `$ref` ngoài snapshot, cycles/depth vượt giới hạn, body vượt size hoặc hash mismatch: không publish snapshot (Task 4).
5. Hai publisher/version registrations cạnh tranh hoặc key bị revoke lúc commit: không ghi đè snapshot, không biến candidate stale thành được phép cài (Tasks 3, 5).

## File map và interface

- `docs/contracts/module-manifest.schema.json`, `publisher-signature.schema.json`, `demo-module.manifest.json`: baseline; thêm fixture công khai test-only vào `tests/Contract/fixtures/` và contract test `tests/Contract/test_wave3_manifest.py` khi cần; không đổi public contract tùy tiện.
- `src/Platform.Infrastructure/Integration/` (mới): `SafeFetchClient.cs` (network policy), `PublisherVerifier.cs` (trust + signature), `ManifestValidator.cs` (shape/semantics), `SchemaSnapshotLoader.cs` (pinned schemas), `ManifestRegistry.cs` (transactional snapshot). Các kiểu DTO/result nội bộ trong `ManifestModels.cs`; không trả raw exception ra API.
- `src/Platform.Infrastructure/Persistence/Migrations/`: migration registry; cập nhật `DatabaseRoleGrants.cs` và snapshot khi convention yêu cầu. Cấu hình publisher trust do operator cung cấp qua `PublisherTrustOptions.cs`; không thêm production private key.
- `tests/Integration/ManifestFetchTests.cs`, `PublisherVerifierTests.cs`, `ManifestValidationTests.cs`, `ManifestRegistryTests.cs`: focused tests; `tests/Integration/Fixtures/` chứa HTTPS/DNS test harness nếu cần. Dùng test key công khai chỉ trong fixture, không dùng key thật.
- Thêm package references tại `src/Platform.Infrastructure/Platform.Infrastructure.csproj`, central versions tại `Directory.Packages.props` và lockfiles liên quan theo convention repo; dùng locked restore. Chọn library stable được hỗ trợ khi thực hiện, ghi chính xác version/license/runtime trong handoff; không suy đoán version trong plan.

**Internal interfaces:** `SafeFetchClient.FetchAsync(Uri uri, FetchPolicy policy, int maxBytes, CancellationToken ct) -> Task<FetchResult>` trả exact bytes và final URI sau policy; `PublisherVerifier.Verify(FetchResult manifest, FetchResult sidecar, PublisherTrustSnapshot trust) -> VerifiedPublisher`; `ManifestValidator.Validate(ReadOnlyMemory<byte> manifest, VerifiedPublisher publisher) -> ValidatedManifest`; `SchemaSnapshotLoader.LoadAsync(ValidatedManifest manifest, FetchPolicy policy, CancellationToken ct) -> Task<IReadOnlyList<SchemaSnapshot>>`; `ManifestRegistry.RegisterAsync(ValidatedManifest manifest, VerifiedPublisher publisher, IReadOnlyList<SchemaSnapshot> schemas, string correlationId, CancellationToken ct) -> Task<RegistryResult>`. Types là immutable; failure dùng typed reason, correlation được gắn tại boundary. Thay interface chỉ khi kế hoạch được cập nhật trước implementation.

## Task 0: Preflight và contract inventory

**Files:** handoff đúng ngày; đọc spec, contracts, `docs/engineering/16-WAVE-2-EXIT-VERIFICATION.md`, `docs/handoff/2026-11-28-wave-2-plan-9-handoff.md`, `Directory.Packages.props`, lockfiles và schema hiện có.

- [ ] Ghi branch/SHA, dirty state, điều kiện Wave 2 và từng evidence còn thiếu; xác định quyền bắt đầu Wave 3 theo AGENTS.md. Nếu chưa đủ thì dừng trước Task 1 và báo blocker; không tự coi security defer là miễn work order.
- [ ] Lập inventory manifest/sidecar/schema, existing public operations và registry schema. Xác nhận hiện chưa có publisher runtime; xác định exact public contract impacts. Nếu thiếu contract cho một operation cần public, thiết kế và review contract trước code; không tự tạo public endpoint trong plan này.
- [ ] Chọn thư viện Ed25519 và JSON Schema Draft 2020-12 stable, supported với .NET 10, license phù hợp, support offline/no remote `$ref`; kiểm tra official package metadata và pin exact version/lockfile trong Task 1. Nếu không đáp ứng, dừng và ghi blocker.

## Task 1: Contract fixtures và baseline verifier dependencies

**Files:** Create `tests/Contract/test_wave3_manifest.py`, `tests/Contract/fixtures/wave3-publisher/` chứa public known-answer manifest bytes, sidecar và public key; modify project/central package versions và lockfiles khi cần.

**Consumes:** manifest và sidecar schema hiện tại. **Produces:** fixture exact bytes, public test key, expected hash và signature dùng bởi Task 3; không có private production material.

- [ ] Viết contract tests cho schema v2 demo accepted, legacy v1/unknown fields/prerelease version/`platformApiVersion` khác bị reject, sidecar sai algorithm/keyId/signature length bị reject; test fixture SHA-256/UTF-8 bytes và consumer độc lập xác minh chữ ký. Fixture phải deterministic, chứa duy nhất key test công khai, và ghi cách tái tạo an toàn; không tự implement Ed25519.
- [ ] Chạy `python3 -m unittest discover -s tests/Contract -p 'test_wave3_manifest.py' -v`; ghi RED thực tế (fixture/expectation chưa có), sau đó thêm fixture và tests tối thiểu.
- [ ] Pin libraries được chọn qua repo central versions và tạo lockfiles; `dotnet restore tests/Integration/Integration.csproj --locked-mode` phải pass sau lock update. Nếu cần public schema diff thì dừng cho ADR/review theo mức breaking, không lén mở rộng schema.
- [ ] Chạy lại contract test PASS; chạy focused consumer verifier (Task 3 sau khi có implementation) và contract generation/check chỉ khi contract đổi. Commit fixture, package pins và test liên quan.

## Task 2: Egress guard và exact-byte fetch

**Files:** Create `src/Platform.Infrastructure/Integration/SafeFetchClient.cs`, `ManifestModels.cs`, `tests/Integration/ManifestFetchTests.cs` và test harness liên quan.

**Consumes:** `FetchPolicy` immutable chứa approved origins và operator-managed egress policy. **Produces:** interface `SafeFetchClient.FetchAsync` và `FetchResult` ở trên; không tự nâng quyền policy.

- [ ] Viết tests cho query/fragment manifest URL, HTTP, userinfo, DNS private/loopback/link-local/metadata, IPv4-mapped IPv6, mixed DNS, DNS rebinding, unsafe redirect, redirect loop, oversized stream, timeout, TLS hostname và response không thành công; chứng minh server cấm không nhận request. Test positive HTTPS public/approved private qua harness kiểm soát, origin pin và exact UTF-8 bytes. `FetchPolicy` không chấp nhận tenant-admin allowlist vượt operator policy.
- [ ] `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~ManifestFetchTests'` phải RED vì component chưa có; ghi output.
- [ ] Implement validate URI và DNS theo contract, chặn automatic redirect hoặc validate mỗi hop, ràng buộc actual connected IP với địa chỉ được duyệt và TLS SNI/hostname với origin; kiểm tra lại tại mỗi connection/retry, không chỉ validate hostname ban đầu. Stream bounded trước cấp phát quá mức; limit/timeouts từ contract; không log URL query hay nội dung.
- [ ] Chạy lại focused test PASS; ghi cách harness chứng minh kết nối thật không bypass policy. Commit component/tests.

## Task 3: Publisher signature và trust

**Files:** Create `PublisherTrustOptions.cs`, `PublisherVerifier.cs`, `tests/Integration/PublisherVerifierTests.cs`; extend models từ Task 2.

**Consumes:** exact bytes Task 2, fixture Task 1 và immutable operator trust snapshot `(moduleId,keyId,publicKey,allowedOrigins,status,revision)`. **Produces:** `VerifiedPublisher` chứa moduleId, keyId, trust revision và hash, không chứa private key; không cấp quyền tenant.

- [ ] Viết tests dùng known-answer thật: đúng bytes/key pass; thay một byte/JSON whitespace, wrong module/key, revoked/unknown key, public key sai size, duplicate JSON keys, malformed/duplicate sidecar fields, signature không canonical hoặc sai 64 bytes, algorithm sai đều fail; verify trước bất kỳ registry write. Test trust rotation: key mới có hiệu lực theo operator snapshot, key cũ revoked không pass.
- [ ] `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~PublisherVerifierTests'` RED, sau đó implement bằng pinned Ed25519 library. Parse tối thiểu moduleId như untrusted selector, reject duplicate keys; signature verify exact fetched bytes trước semantic validation/registration; whitelist origin theo operator trust, không tin origin trong manifest.
- [ ] Chạy lại focused verifier tests PASS và consumer test Task 1; kiểm tra không ghi signature, raw body hay private material vào log. Commit verifier/tests.

## Task 4: Semantic validation và schema snapshots

**Files:** Create `ManifestValidator.cs`, `SchemaSnapshotLoader.cs`, `tests/Integration/ManifestValidationTests.cs`; extend models và fixture cần thiết.

**Consumes:** verified publisher Task 3 và fetch Task 2. **Produces:** `ValidatedManifest` gồm descriptors và schema references; immutable `SchemaSnapshot` chứa URI, version, expected hash và exact verified bytes.

- [ ] Viết tests: valid demo; duplicate descriptor IDs trong từng loại, scope không khai báo, thiếu `entityType`, invalid namespace/version, forbidden origin, schema missing/hash sai, size 65536/65537 bytes, JSON schema invalid, `$ref` external/cyclic và nesting >8 fail; chứng minh không fetch arbitrary `$ref`. Test base-origin; cross-origin chưa có tenant approval thì không fetch và không sử dụng như granted. Candidate có schema cross-origin chờ Plan 2 approval; không đánh dấu snapshot hoàn chỉnh khi schema chưa verify.
- [ ] `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~ManifestValidationTests'` RED. Implement strict schema v2, reject duplicate properties, enforce cross-field constraints ở callback contract và approved trust origins. Xác định namespace event bằng quy tắc contract/fixture hiện hành; nếu contract không định nghĩa mapping unambiguous, ghi gap và dừng để review thay vì tự bịa mapping.
- [ ] Fetch từng schema qua Task 2 với giới hạn/timeout và exact SHA-256, validate offline bounded resolution; cùng URI/hash/version dùng cùng bytes, không resolve remote trên request. Một schema fail thì không trả candidate hoàn chỉnh; không lưu snapshot một phần.
- [ ] Chạy lại focused tests PASS, cả fixture demo và consumer độc lập đọc pinned bytes; commit validator/loader/tests.

## Task 5: Registry immutable và concurrency

**Files:** Create migration `src/Platform.Infrastructure/Persistence/Migrations/20261128000000_ManifestRegistry.cs` (xác nhận timestamp chưa dùng), `src/Platform.Infrastructure/Integration/ManifestRegistry.cs`, `tests/Integration/ManifestRegistryTests.cs`; modify `DatabaseRoleGrants.cs` theo migration convention.

**Consumes:** validated manifest, verified publisher và verified schemas từ Tasks 3–4. **Produces:** immutable catalog snapshot với module ID, release version, manifest exact bytes/hash, publisher key ID/trust revision, descriptor/schema bytes/hash và proposed origins/scopes; status chỉ verified candidate, không installation/grant.

- [ ] Viết PostgreSQL tests từ migration cũ: atomic insert manifest+all schemas; rollback không có partial rows khi insert fail; unique `(moduleId,version)`, same bytes/hash idempotent, same version/different hash conflict; hai writers song song không ghi đè, downgrade rejected theo contract, semver so sánh numeric. Test app role least privilege, immutable update/delete denial, migration upgrade và guarded rollback khi có data.
- [ ] Viết race test revoke/rotate giữa verify và commit: candidate cũ không được đánh dấu hiện đang trusted; trust revision lưu cùng snapshot và phải kiểm tra lại với operator trust hiện hành trước installation/use. Nếu registry dùng shared trust revision transacted, test atomic check; nếu operator config ngoài DB, candidate luôn cần revalidation ở Plan 2, không hứa atomic revoke. Không expose endpoint cho phép bypass kiểm tra này.
- [ ] `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~ManifestRegistryTests'` RED. Implement transaction và constraints; không network trong transaction; validate bytes/hash và identity từ typed pipeline, không tin caller-provided tenant/scopes. Tất cả lỗi fail closed, typed reason/correlation ở boundary.
- [ ] Chạy focused PostgreSQL tests PASS, ghi số test/skips và DB role thực; commit migration, registry và tests. Ghi giới hạn: candidate được verify không đồng nghĩa approved/active.

## Task 6: Focused verification, docs và handoff

**Files:** Update `docs/engineering/03-ACCEPTANCE-MATRIX.md`, `docs/contracts/README.md` khi cần để phản ánh runtime thật, tài liệu operator trust/migration mới trong `docs/operations/`, handoff đúng ngày.

- [ ] Chạy `python3 -m unittest discover -s tests/Contract -p 'test_wave3_manifest.py' -v` và `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~ManifestFetchTests|FullyQualifiedName~PublisherVerifierTests|FullyQualifiedName~ManifestValidationTests|FullyQualifiedName~ManifestRegistryTests'`. Nếu contract/SDK thay đổi chạy generator/check và consumer tests liên quan; ghi candidate SHA, commands, pass/fail/skip, RED/GREEN và limitations. Không chạy full suite local.
- [ ] Review các invariant TLS/DNS actual connection, verifier thật, atomic persistence, immutable hashes và trust revocation. Ghi migration/rollback, trust provisioning/rotation/revoke, reason tokens/correlation và remediation; tuyệt đối không ghi secrets.
- [ ] Cập nhật acceptance matrix chỉ cho evidence đã có: US-06/RT-MOD-01/RT-SSRF-01 vẫn OPEN cho installation approval/challenge, outbound usages và Wave 3 exit. Security FAIL/deferred, Wave 2/P0 OPEN; không claim installation, DemoModule hay P0 pass. Ghi handoff và commit docs. Plan 2 chỉ bắt đầu theo work order và authorization áp dụng.
