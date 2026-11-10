# Repo layout, CI/CD và chất lượng

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

## Hiện trạng kiểm chứng (2026-11-10)

Backend scaffold/locked restore/build, real PostgreSQL integration, SDK-free Compose
clean-source lifecycle, safe JSON logs/Activity và test-only fake issuer đã có local
runtime evidence: [Plan 1](05-WAVE-0-PLAN-1-VERIFICATION.md), [Plan 2](06-WAVE-0-PLAN-2-VERIFICATION.md).
Keycloak realm/metadata đã kiểm chứng local; CRM authentication và production IdP
chưa triển khai/verified. Layout trên vẫn có các thư mục dự kiến.
Frontend build, generated SDKs/breaking-diff detector, full application/security/container
CI và remote CI execution còn OPEN; final Plan 2 whole-branch review pending.

## Frontend
React + TypeScript strict, Vite, TanStack Router, TanStack Query, Tailwind CSS, shadcn/ui; forms with React Hook Form + Zod if adopted consistently; ESLint, formatter, typecheck, Vitest, Playwright; accessibility and localization-ready.

## Backend
.NET 10 LTS, C# 14 when compatible, ASP.NET Core, EF Core 10, FluentValidation or explicit validators, OpenTelemetry, structured logging, problem details error contract; xUnit + Testcontainers. Native AOT only after compatibility/profiling assessment.

## CI pipeline
Hiện có contract-only CI tại `.github/workflows/contracts.yml` và tooling lock `requirements-contracts.lock`; các bước application/security/SDK/diff bên dưới là Wave 0+ chưa triển khai. Không xem contract CI xanh là MVP CI hoàn chỉnh.

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
