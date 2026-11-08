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

## Capacity
- Start single-tenant/small load benchmark; resource budgets per host. Avoid bundling Temporal/RabbitMQ/ClickHouse into MVP on a 16GB dev machine unless needed.
