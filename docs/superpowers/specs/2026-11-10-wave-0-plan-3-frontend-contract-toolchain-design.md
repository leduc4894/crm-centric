# Wave 0 — Plan 3: Frontend và contract toolchain

Ngày: 2026-11-10. Trạng thái: đề xuất để review; chưa phê duyệt implementation. Khảo sát trên `main`, HEAD `3af03d0`, working tree sạch.

## Mục tiêu và hiện trạng

Thiết lập frontend React/TypeScript chạy được, generated TypeScript/C# SDK compile và tiêu thụ độc lập, cùng local gate phát hiện API breaking changes. Đây là phần tiếp theo của cách chia Wave 0 thành bốn plan đã thống nhất; full CI/security scans và remote CI thuộc Plan 4.

Plan 2 đã merge/push. Handoff tích hợp ghi fresh Compose 14, non-Compose .NET 70, contract 32 và Local Python 20 tests pass; đây là bằng chứng lịch sử Plan 2, không phải kết quả Plan 3. Repo hiện có local Compose ở **`compose.yaml` tại root**, scripts lifecycle, API/migrator, Keycloak riêng và safe JSON/Activity logs; chưa có `apps/` hoặc `sdks/`. Không xây lại topology hay nới secret/project ownership để thêm frontend.

Hai nguồn HTTP contract độc lập:
- `docs/contracts/crm-api.openapi.yaml`: OpenAPI 3.1, revision 0.2.0; business API vẫn là đặc tả cho các wave sau.
- `docs/contracts/platform-operations.openapi.yaml`: live/ready đã có runtime, anonymous; giữ nguyên Problem/correlation/no-store.

## Nguồn và invariants

Theo `AGENTS.md`, ADR-003/004/006/010/012/018, `docs/engineering/02-REPO-CI-CD.md`, acceptance matrix, roadmap và UX journeys. Đọc [Plan 2 integration handoff](../../handoff/2026-11-10-wave-0-plan-2-integration-handoff.md) và [review](../reviews/2026-11-10-wave-0-plan-2-final-review.md) khi triển khai.

External consumers chỉ biết versioned HTTP contracts/SDK packages; không reference CRM API/infrastructure project, database hoặc domain types. Contact không thành authenticated user. Tenant context server-side không bị thay bằng body/client state. Không đổi public contract chỉ để generator compile; breaking contract cần ADR, migration plan và consumer evidence.

## Phạm vi và điều kiện thành công

Trong phạm vi: pinned Node/npm frontend workspace; React/TypeScript strict + Vite; routing/query/UI primitives tối thiểu theo engineering conventions; SDK generation/check/pack/consumer tests cho hai OpenAPI; local breaking/security/surface gate và fixtures; browser smoke với operational API thật; hướng dẫn và evidence.

Ngoài phạm vi: login/OIDC callback thực, token storage/session/logout, tenant/RBAC/RLS, contacts CRUD/tags/timeline, business mock screens, workflow editor, demo navigation/SSO, frontend production hosting, npm/NuGet publication, remote CI/branch protection/scanners, marketing/AI. Không thêm business endpoints hoặc seed dữ liệu để tạo cảm giác CRM đã hoạt động.

Plan 3 đạt khi clean checkout có thể locked install, generate/check, build/typecheck/lint/test frontend và cả hai SDK languages; packaged consumers hoạt động qua HTTP boundary; breaking fixtures làm gate fail thật; browser lấy health từ API thật. Không đóng Wave 0/P0 bằng build xanh hoặc fixture business API.

## Các cách tiếp cận

1. Viết frontend với interfaces/client tự viết: nhanh ban đầu, không đóng generated SDK hoặc contract gate.
2. Frontend tối thiểu + generated clients + packaged consumers + pinned local diff tool: chứng minh nền tảng hiện cần, giữ business implementation cho đúng wave.
3. Xây UI nghiệp vụ, login, frontend container và full CI cùng lúc: vượt Plan 3, thêm auth/runtime assumptions chưa có.

Đề xuất của người viết: cách 2. Phạm vi Plan 3 đã có trong thỏa thuận chia Wave 0; lựa chọn package manager, generator, trang nền tảng và policy gate dưới đây là đề xuất kỹ thuật, không được gán thành lựa chọn người dùng đã duyệt.

## Frontend và toolchain

Đề xuất một npm workspace tại repo root, `apps/crm-web`, `sdks/typescript/*` và client consumer tests. Pin exact supported Node patch, npm và dependencies; root `package-lock.json`, `packageManager`, runtime pin file và exact dependency policy phải nhất quán. Fresh install dùng `npm ci`, không dựa vào global packages/cache hoặc tự update lock khi check. Builder xác minh stable versions/compatibility lúc triển khai, ghi nguồn; không đoán số version trong spec.

React + TypeScript strict + Vite; TanStack Router/Query, Tailwind/shadcn primitives chỉ cho phần thực sự dùng. Không cài form library, chart/editor hoặc component catalog chưa có consumer. Không experimental Vite Environment API/Temporal hoặc SSR. Tách generated SDK runtime khỏi web framework.

Trang nền tảng có app heading, navigation tối thiểu, trạng thái kết nối API và thao tác kiểm tra lại. Trạng thái loading/ready/unavailable/network-timeout phân biệt được; không fake contacts hoặc giả đăng nhập. React render text an toàn, keyboard/focus/aria-live hợp lý; không raw HTML từ Problem payload. Spec này không chốt visual design hoặc xây đầy đủ sitemap P0.

Frontend dùng **generated operational TS client** qua một adapter mỏng để quản lý cancellation, timeout và safe error rendering. `/health/live` và `/health/ready` giữ nguyên semantics backend: readiness là DB/schema hiện tại, không phải bằng chứng user/tenant/business ready. Chưa gọi business endpoints chưa triển khai.

Mỗi lần kiểm tra có deadline tổng tối đa 5 giây, bao gồm body read; unmount/refresh hủy request cũ, stale result không được ghi đè trạng thái mới. Không retry/poll vô hạn; Query không dùng kết quả ready cũ để che dependency failure. Cache-Control/no-store phải được tôn trọng; không persist health/token/contact vào browser storage/service worker.

Hiển thị failure bằng safe message và correlation của server khi có hợp lệ; không render raw response/HTML/stack/URL/query/token. Timeout/network/invalid response dùng local classification `WEB_REQUEST_TIMEOUT`, `WEB_NETWORK_ERROR`, `WEB_CONTRACT_INVALID`, phân biệt client correlation với server correlation nếu tạo ID local. Không bịa reason/correlation của server khi chỉ có network error. HTTP Problem boundary kiểm tra shape/length trước khi hiển thị; không coi TypeScript type là runtime validation.

## Browser origin và tích hợp local

Frontend dev server chạy host `localhost:5173`, strict port (occupied thì fail, không tự nhảy port), khớp redirect metadata Keycloak đã có. Auth callback vẫn chưa triển khai; không tạo callback giả. Không đổi realm hoặc issuer từ `localhost` thành Docker service DNS.

Plan 2 `dev-up/down/smoke` và default Compose services/secret grants giữ nguyên. Frontend chạy bằng command npm riêng sau khi backend local stack sẵn sàng; yêu cầu host Node cho frontend không làm mất SDK-free/backend startup của Plan 2. Không tự khởi động/shutdown developer stack trong frontend scripts.

Dev browser gọi cùng origin; Vite proxy allowlist chỉ `/health/live` và `/health/ready` tới loopback API port đã cấu hình (default 5080, hỗ trợ `LOCAL_API_PORT` override). Không wildcard proxy tùy URL/path/host, không thêm backend CORS `*`, không proxy IdP/admin/business API. Browser config chỉ public paths/ports; không `loadEnv` toàn environment hoặc đưa server config vào `define`/VITE variables.

Static build artifact và dev proxy là hai thứ khác nhau. `vite preview` không được mô tả là production reverse proxy; static hosting tương lai phải cấu hình same-origin upstream riêng. Browser runtime smoke của plan dùng dev server/proxy với backend thật; static build có kiểm tra load asset/SPA fallback phù hợp nhưng không claim deployed production connectivity.

UI diagnostic không nhập password/API token hoặc chọn arbitrary upstream URL. Health proxy không được biến thành SSRF endpoint; target/path validate tại startup. Build bundle/sourcemaps/logs không chứa secrets, `.local/` metadata, credentials hoặc raw captured responses.

## Generated SDK architecture

Đề xuất output tách API families và languages:

```text
apps/crm-web/
sdks/typescript/crm/
sdks/typescript/operations/
sdks/dotnet/Crm.Client/
sdks/dotnet/Platform.Operations.Client/
tools/contracts/                       # configs, tool lock, input manifest/policy
scripts/contracts/                     # generate/check/compatibility entrypoints
tests/ClientConsumers/                 # TS/.NET independent package consumers
tests/Contract/fixtures/compatibility/
docs/engineering/07-WAVE-0-PLAN-3-VERIFICATION.md
```

Canonical contracts vẫn ở `docs/contracts`; không copy sang nguồn mới rồi bỏ quên đồng bộ. Generated output được commit cùng config/provenance để code review và reproducibility checks; không sửa tay generated files. Custom transport/adapters nằm ngoài generated tree, tối thiểu và có tests.

Generator đầu tiên cần đánh giá là OpenAPI Generator stable `typescript-fetch` và C# stable template, vì dùng chung toolchain. Đó là candidate, chưa là compatibility proof: builder phải generate/compile **toàn bộ hai OpenAPI thật** và chạy consumer cases trước khi chốt. Tránh C# templates hoặc date options được vendor đánh dấu experimental. Nếu stable generator khác phù hợp hơn, ghi lựa chọn/compatibility matrix; không downgrade OpenAPI, bỏ operation/schema/security hoặc silently đổi nullable/required/additionalProperties để né lỗi.

Pin generator exact release + artifact checksum/image digest, configs và runtime dependencies/lockfiles. Generated C# compile bằng pinned SDK .NET hiện có; target/framework/library được chọn phải supported, không reference CRM server projects. TS clients dùng fetch-compatible runtime, không kéo React/Node-only modules vào browser bundle. Business client có injectable token/HTTP transport cho future consumers; không tự lấy token từ localStorage hoặc auto-refresh auth chưa triển khai. Operational client anonymous không tự gắn bearer, cookies hoặc tenant header.

Generation có provenance: input contract hashes, tool version/digest, config hash. Output deterministic: không timestamps, random IDs hoặc absolute checkout paths gây churn. `generate` chỉ ghi vùng output đã khai báo; `check` generate vào temp và so byte/content chuẩn hóa đã document, **không sửa repo/lockfiles**. Missing/stale/edited/unexpected generated files phải làm check fail. Ref resolution chỉ dùng local committed inputs; không fetch remote `$ref`, custom templates hoặc scripts từ manifest/URL bên ngoài.

Phân biệt **wire fidelity** và **schema validation completeness**. SDK phải biểu đạt/serialize được mọi valid payload của contract, giữ đúng fields, literal discriminator/const values, nullable/omission, unions và headers. Không bắt SDK thực thi toàn bộ JSON Schema validation hoặc biểu diễn `if`/`then`/`else`/`not` thành compile-time types. Ví dụ AutomationRule có conditional action fields: DTO có optional properties tương ứng được phép nếu mọi valid built-in/remote action round-trip đúng và SDK không tự drop/ép dữ liệu. Server vẫn kiểm tra combinations/constraints; SDK không quảng cáo reject được toàn bộ invalid rule.

Compatibility matrix phải ghi rõ từng nhóm constraints nào có typed/wire coverage và constraint nào là server-only; consumers test valid variants + fixture validator độc lập test invalid variants theo canonical schema. Dictionary/JsonElement được phép cho vị trí contract thật sự dynamic; không dùng `any` cho toàn bộ models hoặc opaque JSON để thay mọi DTO. `SDK_GENERATION_UNSUPPORTED` bắt buộc khi mất valid variant/field, không giữ được wire representation/headers, hoặc không có stable supported template compile được; không bắt buộc chỉ vì SDK không enforce conditional validation. Không disable strict hoặc downgrade contract để pass. Hạn chế phải được quyết định trước khi coi gate đạt, không ghi sau khi báo pass.

## Consumer contract và error semantics

Độc lập deploy được chứng minh bằng **local packaging**, không public release: tạo `.tgz`/`.nupkg`, cài vào temporary consumer project chỉ biết package và public HTTP contract. Generated C# không chứa reference/assembly Platform.Api/Infrastructure; TS package không import web app/workspace domain source. Consumer compile từ package output, không chỉ project reference tới generated source. SDK SemVer riêng với contract revision/API `/v1`; release package public/registries để sau.

Consumer tests cả TS và C# qua captured fetch/HttpMessageHandler hoặc HTTP stub cô lập phải kiểm chứng:
- Health success và safe 500/503 Problem, HTTP status/media type, `X-Correlation-Id` và cancellation.
- Request required fields/nullable fields, additionalProperties policy đúng schema; query/cursor encoding, enum/UUID/date-time/numeric representations không bị silently coercion. Date-time mapping phải được document; ưu tiên ISO strings cho TS, không experimental runtime date API.
- Strong ETag đọc từ response không mất; mutation truyền đúng `If-Match`; 412/428 giữ Problem/reason/correlation và không retry/overwrite âm thầm.
- Tenant header là lựa chọn transport tương lai được test bằng fixture; không inject tenant_id vào body hoặc coi header là authorization. Business bearer không rò sang operational requests.
- AutomationRule valid built-in/remote conditional variants biểu đạt/round-trip được; canonical schema validator riêng reject invalid variants, không claim generated client đã enforce toàn bộ DSL.
- OperationId/type surface ổn định, closed errors có thể đọc mà không parse exception message hoặc mất response headers. Thin facade chỉ bổ sung metadata/cancellation nếu generator thiếu ergonomic API, không viết lại DTOs.

Runtime validation/tenant authorization vẫn ở server; SDK compile/serialization không chứng minh business runtime. Business contract fixtures dùng synthetic data và HTTP stub, không gọi endpoint chưa có rồi biến 404 thành success. SDK không tự xử lý signed-webhook authentication: raw-byte/signature protocol vẫn theo tài liệu; không quảng cáo JSON serialization của CRUD client là helper ký webhook hoặc proof replay/durability. Existing HMAC fixtures giữ nguyên.

## Breaking, security và SDK-surface gate

Gate local là executable command sẽ được Plan 4 gọi lại trong CI. Đề xuất pinned `oasdiff` cho OpenAPI wire-compatibility, kèm policy checks cho security và SDK surface. Không chỉ chạy YAML/schema lint rồi gọi là breaking detector. Không upload contracts hoặc dùng SaaS/cloud reporting; generator/diff chạy local với output đã redacted.

Baseline đến từ **Git object tại trusted base revision**, không từ bản baseline editable trong current worktree. CLI nhận `--base-ref`; default local resolve merge-base với `origin/main`, ghi resolved SHA. Không tự fetch/reset/update baseline khi check; thiếu ref/history hoặc contract tại base thì fail `CONTRACT_BASE_UNAVAILABLE`, có hướng dẫn fetch. Plan 4 phải truyền target/base SHA đáng tin cậy; branch protection/approval governance không được claim trong plan này.

So sánh đầy đủ cả operations và business OpenAPI. Input discovery lấy union paths từ base/current; xóa/rename file không được coi như không có gì để check. Config hiện tại không được bỏ qua file baseline mà vẫn pass. Thêm contract mới phải validate/generate/consumer compile trước khi được nhận, không mặc định mọi file mới additive-safe.

Phân loại output: compatible, breaking, security-review-required, SDK-surface-incompatible, schema-review-required, toolchain-review-required, unsupported hoặc tool/config error. Mọi loại ngoài compatible exit nonzero, kèm reason token/correlation ID, input hashes, resolved base SHA và safe JSON report. Invalid/unsupported spec không được trả compatible. Severity mapping/rule config được pin và versioned; fixtures phải chứng minh các case bắt buộc fail với actual tool.

Các case bắt buộc: remove path/operation/response/property, thêm required request field hoặc thắt constraints, nullable/type/enum incompatibility, ETag/If-Match/header compatibility. Security gate bao phủ effective root/operation `security`, resolved `components.securitySchemes` được operation sử dụng (type/scheme/header name/location/flows khi có), và **`x-required-scopes`** theo operation. Thêm/bớt/đổi required scope, bearer/anonymous drift hoặc đổi webhookSignature khỏi `X-Event-Signature`/header phải fail review-required dù wire diff không xem là breaking. Required tenant/signature parameter changes tiếp tục được kiểm tra qua wire + invariant fixtures; object-key/scope-order-only changes normalize an toàn, không tự ignore security extension. New operations cũng phải có security/scope metadata phù hợp API family.

`SDK-surface-incompatible` chỉ nghĩa **phá vỡ surface đã có**, không phải mọi export/member mới. Rename/remove operationId/type/member hoặc đổi signature/type/nullability không tương thích phải fail; additive operation/model/member không làm hỏng existing signatures/consumer use được phép pass. Compare public TS declarations/.NET package API cùng baseline consumer compile, không chỉ HTTP schemas. Mỗi thay đổi chưa chứng minh compatible phải review-required, không tự gán additive-safe. Golden additive cases tối thiểu gồm description-only và thêm unreferenced model/operation có metadata hợp lệ; không mặc định thêm response enum/field luôn safe.

Baseline SDK surface được derive từ base contracts và base generator/config/lock khi đã tồn tại, candidate từ current contracts/toolchain. Lần bootstrap đầu chưa có baseline SDK/toolchain: dùng cùng pinned selected config cho base/current contracts, ghi rõ bootstrap proof, chưa có released SDK consumer. Sau bootstrap, generator/config/dependency/API-shaping changes phải hiện trong report; không dùng current toolchain regenerate cả hai phía để che generator-only breaking change. Missing/unverifiable base toolchain hoặc policy change chưa chứng minh được khiến gate fail `SDK_TOOLCHAIN_REVIEW_REQUIRED`/`CONTRACT_POLICY_REVIEW_REQUIRED`; không bỏ qua chỉ vì OpenAPI không đổi.

Standalone JSON Schemas/events/manifests không được coi đã có full semantic diff chỉ vì OpenAPI tool pass. Plan 3 bổ sung conservative gate: existing schema semantic content thay đổi → `SCHEMA_COMPATIBILITY_REVIEW_REQUIRED`; docs-only `title`/`description`/`$comment` có thể normalize, `$id`/`$ref`/default/validation keywords không tự ignore. Added schemas validate + fixtures; removed/renamed schema không pass. Existing sample/pinned-hash/negative contract tests tiếp tục chạy. Đây là review-required detector, không tự tuyên bố schema thay đổi chắc chắn breaking hoặc additive.

Không automatic rebaseline, ignore-all, `--force` hoặc cho version bump/waiver file trong PR tự làm gate pass. Breaking có chủ đích cần ADR + migration/rollback + consumer tests và baseline advancement qua review riêng trước khi được đánh giá compatible theo base mới. Plan 3 cung cấp command/evidence; approval/trusted-ref enforcement của CI được thiết kế ở Plan 4. Text/protocol business semantics, HMAC correctness và tenant authorization không được chứng minh bởi structural diff.

## Tooling commands và kiểm tra bằng chứng

Implementation plan sẽ chốt exact script names, nhưng public entrypoints cần phân biệt rõ: locked install, generate, generated-check, frontend typecheck/lint/build/test, TS/.NET SDK build/pack/consumer tests, compatibility check với base ref. Một command aggregate cho Plan 3 được phép gọi các bước này tuần tự, fail closed, không tự sửa source hoặc fetch/reset Git.

Golden fixtures gồm unchanged/additive-safe và các mutations bắt buộc fail. Run actual diff/generator/compile; unit test chỉ mock exit code không chứng minh gate. Mỗi negative case bắt đầu từ known-valid base, validate candidate khi thích hợp, assert đúng classification/nonzero để phân biệt breaking với parse failure. Test missing base, removed input và tampered generated output/rebaseline attempts.

Frontend tests dùng Vitest/component tests cho behavior/cancellation/errors và Playwright browser smoke cho app/assets + API path thật. Không tạo snapshot boilerplate thay behavior proof. Tests UI với intercepted failures được ghi rõ fixture; tối thiểu một browser smoke dùng existing real CRM PostgreSQL/API local stack và generated operational client. Mock business contract tests không tính vào backend acceptance.

| ID | Bằng chứng bắt buộc |
|---|---|
| W0P3-01 | Clean checkout locked Node/npm install; strict typecheck/lint/test/build pass, pins/lock không đổi trong check, không global tooling ngầm |
| W0P3-02 | Generate toàn bộ hai OpenAPI sang TS/C#, build cả bốn outputs; local refs/provenance đúng, không làm yếu nguồn contract hoặc models để pass |
| W0P3-03 | Generate hai lần ở thư mục khác ra kết quả deterministic; stale/manual edits/missing hoặc extra generated file khiến check fail; check không mutate workspace |
| W0P3-04 | `.tgz`/`.nupkg` consumers compile độc lập, không CRM project/DB/web dependency; package versions/contract/tool versions phân biệt rõ |
| W0P3-05 | Cả hai languages chứng minh serialization/query/nullable/header/ETag/If-Match/412/428/Problem/correlation và no implicit auth/tenant leakage bằng HTTP boundary fixtures |
| W0P3-06 | Browser thật qua frontend dev proxy gọi API live/ready thật; loading/ready/503/timeout/error và manual retry thể hiện đúng, không giả business/auth ready |
| W0P3-07 | Cancellation/deadline5s gồm body read, stale response không overwrite, error text không XSS/raw response; keyboard/accessibility checks cơ bản pass |
| W0P3-08 | Foreign frontend listener/invalid API target/port override fail rõ; no wildcard CORS/proxy; root Compose lifecycle/secret grants và issuer config giữ nguyên |
| W0P3-09 | Actual diff gate: unchanged/additive-safe pass; specified wire/security/scope/scheme/operationId/schema-review mutations fail đúng classification; malformed/unsupported/missing baseline không pass |
| W0P3-10 | Baseline Git SHA đúng, deleted/renamed inputs vẫn bị kiểm tra, current-worktree baseline/config/waiver hoặc generator-only edits không tự bypass; no cloud upload hoặc remote ref resolution |
| W0P3-11 | Bundle/sourcemaps/generation artifacts/process/browser logs không chứa sentinel secrets/token/PII; không browser storage auth, no package publish/deploy |
| W0P3-12 | Exact-SHA clean export chạy aggregate checks + packaged consumers + browser smoke; existing contract/Local/backend relevant suites pass; docs/evidence phân biệt local fixtures/runtime và Plan4/P0 OPEN |

Evidence ghi tested SHA, Node/npm/.NET/package/generator/diff/browser versions + digests, commands/exit codes/counts, base SHA/input hashes, fresh/cached installs/builds, package artifacts và safe report paths. Không lấy compiler pass thay wire/header proof; không lấy browser screenshot thay request/response assertions. Docker/Node/browser prerequisite thiếu thì báo blocker, không skip rồi tuyên bố complete.

## Regression, migration và rollback

Không thêm DB migration/domain schema; revert frontend/tooling/generated package changes không chạy Down/reset volumes/realm. Không tự bump contract revision để vượt gate. Tài liệu contract README phải phân biệt implemented operational routes với pre-runtime business spec, sửa mô tả stale khi có evidence.

Plan 2 full Compose suite hiện mất hàng chục phút; khi source/container/lifecycle không đổi, có thể tham chiếu exact-SHA prior evidence cho phần không bị chạm và chạy relevant new browser/backend path. Nếu đổi Plan 2 runtime/config/entrypoint/secret/lifecycle, chạy affected tests và required broader gates theo tác động; không nói đã rerun nếu chỉ reuse evidence. Wave-exit full-suite/remote CI ở Plan 4 vẫn bắt buộc. Optional fake-issuer same-kid/disposal follow-ups không tự thành scope Plan 3.

## Builder decisions và bước tiếp theo

Builder chọn exact supported versions, generator/config sau compatibility proof, minimal UI styling/primitives, package metadata/target frameworks và thin HTTP adapters. Đây là quyết định implementation cần ghi evidence; không được đổi ADR, nguồn contract hoặc đưa auth/business UI vào plan để né giới hạn.

Builder review ngày 2026-11-10 đã nêu ba câu hỏi về authorization extensions, additive SDK surface và conditional-schema fidelity. Các đoạn trên đã chốt policy/acceptance tương ứng; reviewer xác nhận cả ba đã được giải quyết, không còn blocking ambiguity. Không thay đổi phạm vi hoặc nguồn contracts.

Sau review/duyệt spec, viết implementation plan bằng `superpowers:writing-plans`. Lượt này chỉ viết tài liệu: không scaffold app/SDK, install packages, generate code, build containers hoặc publish. Local checks của Plan 3 chưa là remote CI hoặc Wave 0/P0 acceptance.

## Nguồn kỹ thuật tham khảo

- [OpenAPI Generator TypeScript Fetch](https://openapi-generator.tech/docs/generators/typescript-fetch/) và [C# generator](https://openapi-generator.tech/docs/generators/csharp/): options/stability khác nhau theo template; phải test source contracts, không dựa vào tên generator.
- [oasdiff breaking-change CLI](https://github.com/oasdiff/oasdiff/blob/main/docs/BREAKING-CHANGES.md): local comparison và severity exit behavior; không thay security/schema semantic review.
- [Vite env/modes](https://vite.dev/guide/env-and-mode.html): browser-visible config cần được xem là public; không đưa secret vào client bundle.

Nguồn tham khảo hỗ trợ lựa chọn, không là compatibility/runtime evidence cho repo. Exact versions và behavior phải kiểm chứng khi implementation.
