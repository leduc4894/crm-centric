# Technology Radar — hiện đại nhưng production-oriented (2026-11)

## Adopt in P0
| Domain | Choice | Reason |
|---|---|---|
| Backend | .NET 10 LTS / ASP.NET Core / C# | LTS production |
| Persistence | PostgreSQL, EF Core 10 | ACID, migrations; tenant isolation |
| Frontend | React, TypeScript strict, Vite | ecosystem, SPA dashboards |
| UI | TanStack Router/Query, Tailwind, shadcn/ui | routing/data composability |
| Contract | OpenAPI 3.1.x if toolchain passes; track 3.2.1 | 3.2.1 exists but tool support must be verified |
| Event schema | JSON Schema + CloudEvents-aligned envelope | interoperability |
| Auth | OIDC/PKCE, Keycloak or managed IdP | do not hand-roll login/security |
| Testing | xUnit, Testcontainers, Vitest, Playwright | full-stack confidence |
| Observability | OpenTelemetry + Grafana stack | logs/traces/metrics |
| Delivery | Docker Compose, GitHub Actions | simple repeatability |

## Trial / P1
- Temporal .NET SDK for long-lived contact-centric automation; test SDK, task queues, versions and sizing.
- RabbitMQ vs NATS JetStream after throughput/operational analysis; do not run both.
- Redis cache/ratelimit when useful; must not become source of truth.
- OpenAPI 3.2.1 after generated SDK, lint and diff toolchain can parse/round-trip and test.
- OpenFeature for feature flags if release/tenant entitlement complexity warrants.
- .NET Aspire for local orchestration/observability if simpler than existing Compose.

## P2+/Hold
- ClickHouse for behavioral analytics only when PostgreSQL projections insufficient.
- OpenFGA/Cedar when permissions outgrow scoped RBAC.
- React Flow for visual workflow builder after runtime stable.
- Kubernetes + GitOps after proven operational need, not in MVP.
- AI assistants/RAG only when explicit use cases + privacy/security reviews.

## Confirmed sources
- .NET 10 LTS: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- OpenAPI 3.2.1: https://spec.openapis.org/oas/v3.2.1.html
- Temporal .NET SDK: https://docs.temporal.io/develop/dotnet
- Keycloak releases: https://www.keycloak.org/docs/latest/release_notes/

**Version policy:** pin tested release versions on project initialization, record exact patch in lockfiles/images. 'Mới nhất' không luôn là lựa chọn an toàn nhất.
