# Deployment, Observability, Backup, DR

## Environments
- Local: Docker Compose with CRM API, Web, Worker, PostgreSQL, IdP, DemoModule on separate network/container; optional Redis.
- Stage: production-like domain/TLS/idp, synthetic test tenants, contract/recovery drills.
- Prod: ingress TLS, isolated secrets manager, per-service runtime identity, separate DB backup, monitoring, deployment pipeline.

## Identity and hosts
- CRM deployment `crm.example.com`, IdP `auth.example.com`, module `trading.example.net` can be different origins, network providers and regions.
- TLS for all cross-host; secure cookies and CORS explicitly scoped; no blanket wildcard.
- Browser login via OIDC with PKCE, API validates access token. Avoid long-lived tokens in localStorage.

## Observability
- OpenTelemetry traces/metrics/log correlation → Grafana stack (Prometheus, Loki, Tempo) or managed equivalent.
- Key dashboards: p50/p95 API latency, 5xx, DB saturation, outbox lag, duplicate rate, DLQ depth, module health, per-tenant quota, workflow run duration, identity failures.
- Every closed error path emits reason token and correlation ID; redact contact personal data.
- Alerts: pending outbox age, DLQ > 0, failed reconciliation, backup fail, elevated authorization failure, webhook signature anomaly.

### Wave 0 local diagnostics (Plan 2, Task 1)

The API uses the built-in .NET JSON console formatter, one object per line.
Its envelope contains `Timestamp` (UTC with `Z`), `LogLevel`, `EventId`,
`Category`, `Message`, `State`, and `Scopes`. Application operation scopes
contain `service`, `eventName`, `correlationId`, and, when an Activity exists,
`traceId`/`spanId`. Closed HTTP errors add `reasonToken` in structured `State`.
Service names are `crm-api` and `crm-migrate`; current events are
`startup`, `configuration_failed`, `dependency_failed`, `migration_completed`,
and `migration_failed`. No trace exporter or collector is configured yet.

Every HTTP request receives a fresh server correlation ID. The error log's ID
matches the existing Problem body and `X-Correlation-Id`; a caller's header
cannot select it. W3C trace context is handled separately by ASP.NET Core.
Trace/span IDs come from the active Activity, with a locally owned W3C Activity
created and disposed only when no Activity exists.

Process results keep exactly one JSON root on the result stream: migration
success writes `status: migrated` to stdout; configuration/migration failure
writes `reasonToken` and `correlationId` to stderr and exits nonzero. Added
metadata uses the same owned field names, with `Timestamp` and `LogLevel` at
the root. Process output is a result document, so it does not add separate
console log entries to either result stream.

Request-start/finish logs from `Microsoft.AspNetCore.Hosting.Diagnostics` are
filtered because they include raw query strings. Safe host/routing events
remain enabled. EF Core and Npgsql diagnostic filters remain in place; app
errors record reason tokens instead of exceptions or connection metadata.
Application logging emits no health-success event per probe. Request bodies,
Authorization/Cookie headers, query values and raw exceptions are excluded.

Build the full solution before process tests, since the test fixture starts
the separately built migrator with `--no-build`:

```bash
dotnet build CrmPlatform.sln -c Release --no-restore
dotnet test tests/Integration/Integration.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~StructuredLoggingTests
```

## Reliability
- Health endpoints: liveness separate from readiness; CRM ready must not depend on external module being online.
- Outbox lease and replay, exponential retry, DLQ, operator replay with audit.
- Backup: daily full + WAL-based PITR where supported; restore monthly on isolated environment.
- Secrets rotation: webhook keys / client credentials, support overlap windows, audit revocation.
- Deploy: expand-contract DB migrations, staged rollouts, confirm read/write compatibility, rollback image and forward-fix schema where needed.

## Initial SLO proposals
- CRM API 99.9% monthly availability (after first production stabilization).
- 99% accepted events processed within 60s when dependencies healthy; benchmark and refine.
- RPO ≤15min/RTO ≤4h targets, validate restore drills; not guaranteed by architecture alone.
- Durability invariant áp dụng process/worker crash và network outage khi committed storage còn nguyên. Total storage loss nằm trong DR envelope có RPO riêng; không dùng availability percentage làm allowance mất accepted events. DR runbook: `03-RECOVERY-AND-INCIDENT-RUNBOOK.md`.

## Capacity
- Start single-tenant/small load benchmark; resource budgets per host. Avoid bundling Temporal/RabbitMQ/ClickHouse into MVP on a 16GB dev machine unless needed.
