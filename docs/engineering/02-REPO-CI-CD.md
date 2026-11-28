# Repo layout, CI/CD và chất lượng

> **Phạm vi CI MVP 2026-11-12:** xem [quyết định Plan 4](09-WAVE-0-CI-MVP-SCOPE.md). Workflow hiện tại chưa được điều chỉnh theo scope mới; các blocker và trạng thái thực tế vẫn phải được báo đúng.

## Gợi ý monorepo
```text
crm-platform/
  apps/crm-web/                      # React + Vite + TS
  src/Platform.Api/                  # .NET 10
  src/Platform.Worker/
  src/Modules/{Tenancy,CRM,Integration,Automation,Audit}/
  src/BuildingBlocks/                # Ports, primitives, telemetry only
  contracts/openapi/
  contracts/events/
  contracts/manifests/
  sdks/typescript/                    # generated / separately versioned
  sdks/dotnet/                        # generated / separately versioned
  examples/demo-module/              # independent process, its own DB
  tests/{Unit,Integration,Contract,E2E,Security}/
  infra/{compose,terraform,monitoring}/
  docs/
```

## Hiện trạng kiểm chứng (2026-11-11)

Backend scaffold/locked restore/build, real PostgreSQL integration, SDK-free Compose
clean-source lifecycle, safe JSON logs/Activity và test-only fake issuer đã có local
runtime evidence: [Plan 1](05-WAVE-0-PLAN-1-VERIFICATION.md), [Plan 2](06-WAVE-0-PLAN-2-VERIFICATION.md).
Keycloak realm/metadata đã kiểm chứng local; CRM authentication và production IdP
chưa triển khai/verified. Layout trên vẫn có các thư mục dự kiến.
Frontend build, generated SDKs/breaking-diff detector, full application/security/container
CI và remote CI execution còn OPEN. Local Plan 2 đã final review Approved,
0 Critical/Important; [review và controller rulings](../superpowers/reviews/2026-11-10-wave-0-plan-2-final-review.md).

## Frontend
React + TypeScript strict và Vite; ESLint, typecheck, Vitest, Playwright; chú ý accessibility và localization.

### Convention TanStack cho feature mới

Ưu tiên dùng các thư viện TanStack **đã được tích hợp và ổn định trong ứng dụng** khi chúng phù hợp với nhu cầu. Không bắt buộc thêm abstraction vào bảng tĩnh hay form nhỏ; nếu chọn cách khác cho feature có cùng nhu cầu, ghi lý do trong plan hoặc PR và giữ các invariants bên dưới. Convention này không yêu cầu viết lại code đang hoạt động chỉ để đồng nhất thư viện.

- **Query:** ưu tiên cho dữ liệu server. Query key/lifecycle phải cô lập theo identity, tenant và session; khi đổi tenant/logout, hủy request và xóa hoặc cô lập cache, kể cả response cũ về muộn. Không đưa token vào key/storage/log. Giữ strong ETag cùng snapshot đã review, giữ draft khi `412`, không tự retry mutation xung đột hoặc có kết quả chưa xác định. Tiếp tục dùng client và contract được duyệt.
- **Table:** ưu tiên cho bảng dữ liệu tương tác cần quản lý cột, selection hoặc trạng thái bảng. Khi dữ liệu được phân trang/filter trên server, không sort/filter cục bộ chỉ tập hàng đã tải hoặc giả định offset/tổng số trang; giữ cursor, thứ tự server, accessibility và thiết kế ứng dụng.
- **Router và Form:** áp dụng cùng nguyên tắc sau khi các plan tương ứng đã triển khai và ổn định. Router phải giữ auth, tenant isolation, deep link và back/forward. Form phải giữ validation, draft khi conflict và độ chính xác số custom field; không mặc định chuyển số lossless qua JavaScript `Number`.
- **Virtual:** chỉ đưa vào khi có nhu cầu render nhiều dòng và evidence phù hợp; không mặc định cài.

Query và Table đã có trong frontend theo [Plan 5](../handoff/2026-11-27-wave-2-plan-5-handoff.md) và [Plan 6](../handoff/2026-11-27-wave-2-plan-6-handoff.md); Router và Form thuộc Plan 7–8, không khẳng định đã triển khai. Chọn stable release được hỗ trợ và pin exact trong lockfile khi thêm dependency. Các plan chạy test liên quan local; full suite ở Wave 2 exit trên GitHub CI theo [Wave 2 design](../superpowers/specs/2026-11-27-wave-2-crm-contacts-design.md).

## Backend
.NET 10 LTS, C# 14 when compatible, ASP.NET Core, EF Core 10, FluentValidation or explicit validators, OpenTelemetry, structured logging, problem details error contract; xUnit + Testcontainers. Native AOT only after compatibility/profiling assessment.

## CI pipeline
Local Plan 4 replaces `contracts.yml` with `.github/workflows/wave0.yml`: backend, Compose, contracts/SDK, web/browser, security and required aggregate. Implementation is reviewable, but backend/Compose, actual security/image findings, dependency coverage, independent trust and remote evidence remain OPEN/BLOCKED. See [Plan 4 verification](08-WAVE-0-PLAN-4-VERIFICATION.md). Proposed required check names and migration/rollback are recorded there; no rules/settings changes or remote runs were performed. The sequence below includes future runtime/deployment work and is not a claim of implemented acceptance.

1. Format/lint/TS typecheck, .NET build, dependency license + vulnerability scan.
2. Unit tests + static analysis/security scan, secret scan.
3. Start ephemeral PostgreSQL/IdP via Testcontainers/compose; integration and RLS tests.
4. API/JSON Schema lint + breaking diff + generated SDK compilation.
5. E2E smoke with DemoModule remote, failure injection nightly.
6. Build immutable OCI images with SBOM, image scan, provenance/signing where feasible.
7. Stage deployment → migration checks → smoke → manual prod approval → rollback.

## Versioning
- CRM API paths `/api/v1`, module manifest and events explicitly versioned.
- SDK semantic versioning independent of server version; compatibility matrix in release notes.
- Prefer additive schema change; deprecate fields before removal, supported sunset window.
- Pin dependencies with lockfiles and renovate/dependabot PRs only after CI.

## Development workflow with one coding agent
- One slice at a time; create tests first; inspect diff, run full relevant suite; merge only after acceptance.
- No broad refactor in same PR as new domain feature unless explicitly approved.
- Each feature records invariants, contract changes and reason tokens in docs.
