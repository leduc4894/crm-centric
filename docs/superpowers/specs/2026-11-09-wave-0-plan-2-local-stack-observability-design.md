# Wave 0 — Plan 2: Local stack, IdP và observability nền tảng

Ngày spec: 2026-11-09. Cập nhật trạng thái: 2026-11-10. User đã phê duyệt implementation
trên feature branch/no-worktree. Tasks 1–7 đã implement và review; Task 8 ghi clean-source
runtime/evidence. Local Plan 2 final review Approved (0 Critical/Important), controller đã
chấp nhận mọi scope exclusion; [review/rulings](../reviews/2026-11-10-wave-0-plan-2-final-review.md). Xem [verification](../../engineering/06-WAVE-0-PLAN-2-VERIFICATION.md).
Không đóng Wave 0/P0, không push/merge/deploy. Baseline khảo sát: Plan 1 đã merge/push,
`main` tại `e624514`.

## Mục tiêu và kết quả

Người phát triển checkout sạch có thể chạy một lệnh để dựng API, PostgreSQL, migration và Keycloak local; nhận kết quả readiness có kiểm chứng, đọc được structured logs có correlation/trace mà không tự provision database hoặc nhập credentials vào command line.

Plan 1 đã có migrator riêng, least-privilege database identity, health endpoints, safe errors và process smoke. Plan 2 đóng gói và chuẩn hóa các phần này, không viết lại persistence hoặc thay đổi nghĩa health. Thành công của plan này không đóng cả Wave 0 hoặc P0.

## Nguồn và hiện trạng

- `AGENTS.md`; ADR-003/007/008/017 trong `docs/00-DECISIONS.md`.
- `docs/roadmap/01-BACKLOG.md`, `docs/engineering/02-REPO-CI-CD.md`.
- `docs/architecture/03-IDENTITY-TENANCY-SECURITY.md`; `docs/operations/01-DEPLOYMENT-OBSERVABILITY.md`.
- Plan 1 spec, implementation và evidence; `docs/handoff/2026-11-09-wave-0-plan-1-integration-handoff.md`.

Hiện trạng tại thời điểm khảo sát trước Plan 2: runtime chỉ có `/health/live`, `/health/ready`; API đọc connection string từ environment, không tự migrate; migrator preflight role boundary rồi Up/grant. Error responses có reasonToken/correlationId và lỗi đã được kiểm tra chống lộ secrets. Chưa có Dockerfile, Compose, Keycloak, fake issuer hay chuẩn JSON logs/trace dùng chung cho mọi process do repo sở hữu. Backlog còn một số mô tả scaffold chưa triển khai cần cập nhật khi có evidence, không coi mô tả cũ là hiện trạng mã.

## Phạm vi và giới hạn

Trong phạm vi:
- Build container API/migrator từ source và lockfiles; Compose local có health/startup gates.
- Provision tự động dedicated CRM database và roles đúng Plan 1; Keycloak có PostgreSQL riêng.
- Secret generation/injection local, startup/stop/smoke commands và xử lý restart/persistence.
- Realm/client metadata Keycloak local; cấu hình IdP production được tài liệu hóa; fake OIDC issuer trong test assembly.
- JSON console logs và trace/correlation cho host, runner, lifecycle scripts; kiểm tra không lộ secrets.
- Integration/Compose acceptance và tài liệu evidence, không thêm CI pipeline mới.

Ngoài phạm vi: CRM login/session/JWT authorization, tenant/membership/RLS, contacts, Worker, business events, DemoModule, frontend, SDK generation, breaking-diff gate, remote CI, metrics backend/Grafana/Loki/Tempo/OTLP collector, production deployment và DR. Không thêm endpoint bảo vệ giả chỉ để chứng minh OIDC; Wave 1 mới thực thi đăng nhập và tenant authorization. Không bật experimental Keycloak features hoặc tự viết OAuth server.

## Các cách tiếp cận

1. Compose chỉ DB, API chạy trực tiếp trên host: debug thuận tiện nhưng vẫn phụ thuộc SDK/provisioning host và chưa chứng minh container cold start.
2. Compose chứa mọi service hiện cần, logs JSON ra stdout/stderr, IdP độc lập: đáp ứng local cold start và giữ phạm vi nhỏ.
3. Dựng thêm full observability stack, frontend và Worker: tăng RAM/điểm lỗi trước khi có hành vi nghiệp vụ để quan sát.

Đề xuất của người viết: cách 2. Người dùng đã thống nhất phạm vi Plan 2 là local stack + observability và yêu cầu viết spec; lựa chọn chi tiết như hai PostgreSQL, ports và lifecycle dưới đây vẫn là đề xuất để duyệt, không được ghi thành quyết định người dùng đã chọn.

## Topology và ownership

| Service | Vai trò và quyền | Dependency startup |
|---|---|---|
| `crm-db` | PostgreSQL dành riêng CRM; admin sở hữu DB; volume CRM riêng | không |
| `crm-bootstrap` | one-shot provision/validate DB và roles; chỉ service này cùng crm-db được cấp admin secret | crm-db healthy |
| `crm-migrate` | one-shot chạy migrator hiện có bằng non-owner/non-superuser identity | bootstrap exit 0 |
| `crm-api` | app role không DDL, chỉ đọc migration history cho readiness | migrate exit 0 |
| `idp-db` | PostgreSQL riêng cho Keycloak, credentials/volume riêng | không |
| `keycloak` | IdP riêng, chỉ có quyền với idp-db; không đọc CRM DB hoặc CRM secrets | idp-db healthy |

Keycloak không nằm trong dependency chain của API. Khi IdP offline, CRM live/ready vẫn phản ánh host/CRM DB theo Plan 1; lệnh xác nhận toàn stack có thể fail vì IdP chưa sẵn sàng nhưng không đánh đồng thành API unhealthy.

Mặc định publish API `127.0.0.1:5080`, Keycloak `127.0.0.1:8080`. DB và management/health port của Keycloak không publish host. Cho phép chọn ports khác qua cấu hình không chứa secret; không dùng `container_name`, host networking, Docker socket mount hoặc privileged container. Mỗi test dùng Compose project name và volumes riêng; dev project name ổn định theo checkout, không tác động stack khác.

CRM và IdP có network riêng; không service nào truy cập trực tiếp domain DB bên kia. Không dùng chung database/schema/login; API không có admin hoặc migrator credential. Không thêm runtime reference sang Keycloak hay domain module ngoài CRM.

## Build và đường dẫn dự kiến

```text
infra/compose/compose.yaml
infra/compose/images.lock.env             # chỉ exact image refs, không secrets
infra/compose/entrypoints/                # file-secret adapters, bootstrap, health probes
infra/keycloak/crm-local-realm.json       # metadata không users/passwords/client secrets
infra/docker/Platform.Dockerfile          # build targets API và migrator
.dockerignore
scripts/dev-up.sh
scripts/dev-down.sh
scripts/dev-smoke.sh
tests/Integration/Fixtures/FakeOidcIssuer.cs
tests/Integration/*                     # logging/OIDC-fixture/Compose tests theo trách nhiệm
docs/operations/04-LOCAL-DEVELOPMENT.md
docs/operations/05-IDP-CONFIGURATION.md
docs/engineering/06-WAVE-0-PLAN-2-VERIFICATION.md
```

Đường dẫn là đề xuất, builder được tách helper nhỏ theo trách nhiệm. Không tạo domain project rỗng. Cấu hình logging dùng lại `Platform.Infrastructure` hoặc host composition root theo dependency hiện có; không tạo một framework telemetry mới.

Docker multi-stage dùng SDK patch khớp `global.json`, restore locked trước publish; API và migrator runtime chạy non-root, không chứa SDK/source/secrets, không chạy restore/migrate ngầm trong API entrypoint. Không bật AOT/trimming. `.dockerignore` và COPY allowlist loại `.git`, `.venv`, `.superpowers`, local config và secret directory khỏi context/image.

Mọi upstream image, kể cả build/runtime/probe, pin exact stable tag + digest. Builder xác minh compatibility/support/health probe trên phiên bản thực tế khi triển khai, ghi nguồn/digest/minimum Compose version đã test. Giữ SDK/package pins Plan 1 trừ khi có lý do tương thích được ghi và lockfiles cập nhật; không đoán phiên bản Keycloak chưa kiểm chứng.

## Local secrets và database bootstrap

`dev-up` tạo secret directory đã gitignore, mode 0700; files mode 0600, cryptographically random, không giá trị mặc định hard-coded. Lần chạy sau giữ nguyên secrets; không tự đổi password khi named volumes còn dữ liệu. `.env.example`/image lock chỉ có cấu hình công khai hoặc tên file. Không tạo connection string có secret trong Compose interpolation, Docker build ARG, image layer, healthcheck command hoặc argv.

Compose secrets mount read-only theo từng service. PostgreSQL dùng file-based secret interface của image đã pin; entrypoint adapter của API/migrator/Keycloak chỉ đọc file được cấp rồi truyền setting trong process environment hoặc cấu hình runtime riêng, không echo/export toàn environment. Cấu hình Plan 1 trên host tiếp tục hoạt động. File-permission/UID thực tế phải kiểm tra với Compose non-root; không giả định khai báo secret `mode` sẽ sửa được bind-file ownership. Không chmod 0644 để chữa lỗi permission; dùng ownership/ACL hoặc runtime bootstrap root rất ngắn với quyền hạ xuống trước khi chạy app.

Host có thể mở secret file của chính mình để cấu hình admin local; script chỉ in vị trí file, không in giá trị. Startup không phụ thuộc việc in/copy password vào terminal. Compose local file mounts không được mô tả là secret manager mã hóa production.

Bootstrap giữ boundary Plan 1: admin-owned dedicated DB; migrator LOGIN không superuser/BYPASSRLS và không owner DB, có CONNECT/CREATE; app LOGIN chỉ CONNECT, không CREATE/TEMPORARY/role membership nâng quyền hoặc ownership. Admin thu hồi PUBLIC database/schema privileges trong đúng DB local do stack sở hữu; migrator tạo `platform`/history và cấp app USAGE/SELECT. Không dùng `EnsureCreated` hoặc app identity để provision.

Bootstrap lần đầu tạo role/database cần thiết. Chạy lại với volume và secrets cũ thì validate/idempotent, không drop/recreate/reset data. Volume có sẵn nhưng thiếu/mismatched secret hoặc sai ownership/ACL thì fail an toàn, không tự sửa bằng credential mới. Phân biệt role/database tồn tại với trạng thái đã provision đầy đủ; init bị ngắt phải retry không phá dữ liệu, không đánh dấu success khi quyền chưa đúng. Không coi script chỉ chạy một lần trong initdb là đủ cho kiểm tra warm start.

Mọi service chỉ nhận secrets cần dùng; không log SQL chứa password hoặc raw provider error. Cả stdout/stderr, database/bootstrap/Keycloak startup logs, `docker inspect` cấu hình do repo tạo và Compose config phải không chứa sentinel secret ở trường được công bố. Quyền host admin đọc file/proc là boundary máy local, không claim chống host administrator.

## Lifecycle và điều kiện ready

Entrypoint được người dùng gọi: `bash scripts/dev-up.sh` từ checkout sạch, prerequisites Docker Engine + Compose v2, Bash/Python theo toolchain đã kiểm chứng. Không cần host .NET SDK để chạy stack. Lệnh generate/reuse secrets, build, start services, chạy serialized bootstrap/migration và xác nhận readiness thực tế. Không yêu cầu login, admin seeding hay chỉnh source trước khi chạy.

Compose dùng health gate cho DB và `service_completed_successfully` cho bootstrap/migration. Không chỉ dựa vào container running hoặc thứ tự `depends_on`. Up helper phải chạy lại/validate one-shot services trong mỗi lần up (kể cả container exit 0 cũ), không vô tình skip migration của image mới. Serialize các lệnh lifecycle cùng project, lock có cleanup; không dùng nhiều runner song song. Không quảng cáo distributed migration locking.

Healthcheck CRM dùng anonymous routes hiện có; readiness giữ budget 3 giây, no-store, safe errors và server correlation. Health probes phải có executable thật trong image: không giả định runtime .NET hoặc Keycloak có curl/wget. Probe lỗi không dump response/log nhạy cảm. Keycloak kiểm tra health management nội bộ và realm discovery đúng issuer; `pg_isready` không thay readiness của ứng dụng.

Cold start có hai phần evidence: thời gian pull/build riêng, startup readiness riêng. Budget mặc định startup 180 giây sau build; probes có timeout hữu hạn. Hết budget trả exit khác 0 và safe reason/correlation, giữ volumes để điều tra, không báo pass/teardown dữ liệu. Thời gian build/pull phụ thuộc mạng nên ghi riêng, vẫn có deadline hữu hạn do helper/builder công bố. Up pass chỉ khi API live/ready 200, Keycloak ready và realm discovery/JWKS truy cập được.

`dev-down` dừng/xóa containers/network của đúng project, giữ data volumes và secrets. Không có tự động `down -v`, drop DB hoặc reset realm. Hướng dẫn reset tách riêng là thao tác destructive do người dùng chủ động chọn, không nằm trong cold-start/retry tự động. Normal stop/shutdown có grace deadline rồi force-stop owned processes/containers; không hang vô hạn.

`dev-smoke` kiểm tra stack đang chạy qua host-bound endpoints; không tạo một API host khác như process smoke Plan 1. Xác minh container/project ownership cùng port mapping để không chấp nhận service lạ đang chiếm port; curl 200 đơn thuần chưa đủ. Không thay đổi ý nghĩa script `backend-smoke.sh` đang có.

## Keycloak local và tài liệu production

Realm local `crm-local` có metadata của hai OIDC clients tương lai: `crm-web-local` và `demo-module-local`, đều public browser clients Authorization Code + PKCE S256, distinct client IDs, exact loopback redirect/origin allowlists, implicit/direct password grants tắt. Audience CRM tương lai được mô tả nhưng không kiểm chứng API JWT middleware trong plan này. Chưa có frontend nên redirect là placeholder được tài liệu hóa; không tạo callback endpoint giả để báo login pass.

Không import users, admin password, client secret hoặc token signing private key vào Git. Bootstrap admin credentials sinh riêng cho local Keycloak, không tạo CRM identity/contact/membership. Chưa cần service-account client hoặc demo user đăng nhập. Không test token issuance bằng password grant để thay browser login.

Issuer canonical mặc định `http://localhost:8080/realms/crm-local`, thay port đồng bộ khi user override; discovery phải trả đúng issuer và URLs có thể truy cập từ browser host. Ghi rõ hostname public khác service DNS nội bộ, không thay issuer bằng `http://keycloak:8080` và không tắt issuer validation để né networking. Plan 2 chưa có API gọi IdP nên không tự claim server-to-server discovery đã xong; Wave 1 phải chốt backchannel routing/HTTPS trước auth middleware.

HTTP/start-dev, nếu builder chọn cho local, chỉ là local loopback profile, không dùng làm production example. Realm startup import có thể skip realm đã tồn tại: warm start validate metadata cần thiết, không coi sửa JSON là migration tự áp dụng. Metadata drift trả reason rõ và hướng dẫn reconcile/reset có chủ đích; không overwrite realm/delete users đang chạy. Test cold realm import và warm unchanged separately.

Tài liệu production phải chỉ rõ TLS/hostname/proxy trust, issuer/audience, PKCE/redirects, separate browser clients, JWKS/key rotation, admin MFA, session/logout policy, secrets/DB identity và managed IdP alternative. Không chứa credentials thật; không deploy hoặc claim production-ready từ local start-dev evidence.

## Fake OIDC issuer cho tests

Chỉ nằm trong test assembly, chạy HTTP loopback ephemeral; không được đăng ký thành production service hoặc dependency của Compose. Cung cấp discovery/JWKS nhất quán và ký fixture JWT bằng thư viện chuẩn, RSA key sinh runtime; tắt request/body/token logging. Không triển khai authorize/login/token endpoint hay custom OAuth flows.

Fixture API cho phép chọn issuer/audience/subject/expiry và đổi active `kid` để Wave 1 dùng cho negative cases/rotation; default subject là test identifier, không suy ra từ email/contact. Tests của fixture chứng minh metadata đúng, JWKS không chứa private material, signature verify bằng public key và key rotation behavior. Đây là test utility readiness, không phải evidence OIDC authorization của CRM. Chỉ tests mới cho phép HTTP discovery; production configuration vẫn yêu cầu HTTPS.

## Structured logs, correlation và trace

API/migrator dùng JSON console một object mỗi dòng cho log entry. Ưu tiên built-in .NET JSON formatter và logging scopes, không viết custom formatter chỉ để đổi casing. Standard console fields theo formatter đã chọn phải được tài liệu hóa; application-owned structured state tối thiểu: `service`, `eventName`, `correlationId`; thêm `reasonToken` cho closed error, `traceId`/`spanId` khi có Activity, UTC timestamp và severity ở formatter envelope. Không giả trace/span bằng correlation GUID.

Giữ response Problem và `X-Correlation-Id` contract Plan 1. Mỗi request sinh correlation ID mới, không tin header caller; log operational error chứa đúng ID response. Inbound W3C trace context hợp lệ được .NET xử lý, tách biệt với correlation/tenant authorization; malformed traceparent không làm host crash. Tests kiểm tra Activity IDs thực với HTTP pipeline, không tự điền string vào log để pass.

Runner và lifecycle scripts dùng safe machine-readable JSON stdout/stderr: success/status, failure reason/correlation, không raw stack/exception/SQL/connection settings. Hình dạng stdout/stderr hiện có của runner (`status` hoặc reasonToken/correlationId) được giữ tương thích khi thêm metadata. Không bắt buộc định dạng JSON của vendor PostgreSQL/Keycloak giống .NET; bằng chứng logs của toàn stack vẫn kiểm tra secrets không lộ.

Thêm events startup/configuration failure, bootstrap/migration outcome và dependency failures cần thiết để vận hành; không ghi request body, Authorization/Cookie headers, token, query string hoặc health payload tùy tiện. Giữ bộ lọc EF/Npgsql chống lộ metadata của Plan 1. Khả năng xem logs không dựa vào tắt hết logger: tests capture cả app/framework và assert có safe events/correlation. Không ghi log health success mỗi lần probe để tránh ngập console.

Lệnh vận hành có reason tokens ổn định: `LOCAL_PREREQUISITE_MISSING`, `LOCAL_CONFIGURATION_INVALID`, `LOCAL_SECRET_STATE_MISMATCH`, `LOCAL_BOOTSTRAP_FAILED`, `LOCAL_MIGRATION_FAILED`, `LOCAL_PORT_CONFLICT`, `LOCAL_START_TIMEOUT`, `LOCAL_IDP_NOT_READY`, `LOCAL_REALM_DRIFT`, `LOCAL_SMOKE_FAILED`, `LOCAL_LIFECYCLE_BUSY`; HTTP reason tokens hiện có không đổi. Mỗi closed failure có correlation ID, exit nonzero và không nuốt exception.

## Kiểm thử và bằng chứng nghiệm thu

Viết test cho hành vi thiếu trước code theo RED → GREEN → refactor. PostgreSQL thật/Testcontainers cho integration tests; Compose tests chạy services thật, không thay image giả để chứng minh cold start. Phân biệt những gì là schema/config validation với runtime evidence. Không skip vì Docker/Compose thiếu rồi báo pass. Existing Plan 1 suite phải giữ xanh.

| ID | Điều kiện và bằng chứng cần có |
|---|---|
| W0P2-01 | Clean tracked-source export, không host SDK/secret/data cũ: một lệnh dev-up thành công; image pins/locks đúng; API live/ready 200, Keycloak healthy + discovery/JWKS đúng issuer |
| W0P2-02 | Inspect SQL bằng identities thực: migrator non-owner DB, app không DDL/DML history; API không có admin/migrator secrets; Keycloak DB/container/volume tách biệt |
| W0P2-03 | Bootstrap/migration lỗi thì API mới không start/ready và up exit nonzero; sửa nguyên nhân/retry thành công; không thêm history trùng hoặc reset data |
| W0P2-04 | Up lại và down/up trên volumes cũ: giữ role/secret/history/realm; sentinel fixture data giữ nguyên; thiếu/mismatched secret hoặc drift fail rõ, không reset ngầm |
| W0P2-05 | Stop crm-db trong API instance đang chạy: live 200, ready 503 trong budget, safe log/correlation; start DB thì ready 200 không restart API |
| W0P2-06 | Stop Keycloak/idp-db: API live/ready vẫn 200 khi CRM DB tốt; full-stack smoke báo IdP lỗi; phục hồi IdP không ảnh hưởng CRM dữ liệu |
| W0P2-07 | JSON logs parse được, app reason/correlation khớp HTTP, traceId/spanId là Activity thật; forged correlation/malformed traceparent không phá isolation hoặc diagnostics |
| W0P2-08 | Sentinel secrets: config artifacts/image history/Compose settings và toàn bộ logs/captured diagnostics không lộ giá trị ở success, bad auth, failed bootstrap/migration/IdP startup; test capture không in raw payload khi fail |
| W0P2-09 | Fake issuer discovery/JWKS/signature/rotation pass, không private JWK hoặc token log; xác nhận fixture không vào production publish output |
| W0P2-10 | Realm đúng client IDs/PKCE/redirects, direct/implicit grants off; warm realm drift phát hiện; tài liệu production có checklist cấu hình, không claim login pass |
| W0P2-11 | Port đã bị foreign listener chiếm hoặc 2 lifecycle commands cùng project: fail an toàn, không success nhầm, không kill stack/process khác; stop có deadline; parallel test projects không dùng chung volumes |
| W0P2-12 | Locked restore/build, existing + new integration/contract suites, shell validation, clean export Compose smoke pass; tài liệu và acceptance matrix đúng evidence/local-only |

Compose tests được chạy bằng command riêng nếu runtime lâu, nhưng là gate bắt buộc đóng Plan 2. Không thay gate Compose bằng Testcontainers-only hoặc `docker compose config` pass. Fixture sentinel không tạo business schema production. Evidence gồm tested SHA, SDK/package/image digests, Compose/Docker versions, resource budget/host RAM, pull/build/start timing, command/exit status/test counts, safe correlation examples và artifact path đã redacted.

## Migration, rollback và vận hành

Không thêm migration domain trong plan này. Compose gọi migrator hiện có; fixture data/provisioning không đưa vào domain migration. API image/config rollback về Plan 1 không tự Down/drop schema. Local stack config có thể revert và chạy host path Plan 1 với credentials đã provision; giữ named volumes/secrets. Image PostgreSQL/Keycloak upgrades phải review compatibility, không tự bump rồi reuse volume mù quáng.

Realm import không phải backup; down/up không phải restore drill. Volume/secret rotation hoặc destructive reset ngoài lifecycle thường phải có quy trình riêng được người dùng chủ động chọn. Không dùng admin credential từ Keycloak cho CRM.

## Quyết định giao builder và bước sau review

Builder chọn exact supported versions/digests, safe bootstrap implementation (SQL stdin/typed helper), health probe executable, secret UID mapping, JSON formatter/scopes và chi tiết fake issuer library theo ràng buộc trên; ghi lựa chọn và nguồn vào evidence. Không mở rộng sang Wave 1 hoặc thêm observability products nếu chưa có yêu cầu.

Builder review chỉ đọc ngày 2026-11-09 đã cho phép lập plan. Runtime evidence hiện có được ghi trong verification; các đoạn đề xuất và baseline bên trên giữ ngữ cảnh thiết kế.

Implementation plan đã thực thi và final review Approved; local Plan 2 complete, optional Minor follow-ups được giữ trong review. Không coi evidence local là production deployment/authentication hoặc P0 acceptance.

## Nguồn kỹ thuật đã tham khảo

- [Docker Compose startup order](https://docs.docker.com/compose/how-tos/startup-order/): health/completed-success gates, không đồng nhất running và ready.
- [Compose secrets](https://docs.docker.com/compose/how-tos/use-secrets/): file mount và per-service grants; không đặt secret trong image/source.
- [Keycloak hostname](https://www.keycloak.org/server/hostname): issuer/public URL cần canonical configuration.
- [Keycloak realm import](https://www.keycloak.org/server/importExport): startup import/warm-state semantics, không thay backup.
- [.NET console log formatting](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/console-log-formatter): built-in JSON console formatter/scopes.

Các nguồn hỗ trợ lựa chọn thiết kế, không là evidence runtime của repo. Implementation phải kiểm chứng lại chi tiết với các image/tool versions đã pin.
