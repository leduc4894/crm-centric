# Phased Delivery Plan / Sprint Backlog

## Wave 0 — Repo / architecture runway
- ADR and module boundary baselines, repo scaffolding, compose local stack.
- Install .NET 10/EF Core 10 and front-end toolchain versions; CI build/unit tests.
- Fake OIDC provider for tests, production-ready IdP config documented.
**Đã kiểm chứng local:** .NET scaffold/locked build, PostgreSQL migration/least privilege,
SDK-free Compose clean-source startup, structured JSON logs + Activity trace IDs,
fake OIDC test issuer và Keycloak riêng. Xem [Plan 2 evidence](../engineering/06-WAVE-0-PLAN-2-VERIFICATION.md).
Production IdP configuration đã document, chưa deployed/auth-verified. Local Plan 2
final review Approved (0 Critical/Important); [review và optional follow-ups](../superpowers/reviews/2026-11-10-wave-0-plan-2-final-review.md).
**Đã kiểm chứng local (Plan 3):** frontend toolchain/build (React/Vite workspace, typecheck/lint/test/build),
generated TS/C# SDK compile + đóng gói + consumer độc lập, và breaking/security/schema gate tại base Git
với aggregate fail-closed. Xem [Plan 3 evidence](../engineering/07-WAVE-0-PLAN-3-VERIFICATION.md).
**Exit còn OPEN:** application/security/container CI, remote CI run và toàn bộ Wave 0/P0 runtime.
Local Plan 4 replaces the old contract-only workflow with `.github/workflows/wave0.yml`.
[Plan 4 evidence](../engineering/08-WAVE-0-PLAN-4-VERIFICATION.md) records failed original backend/Compose jobs, real security/image findings, coverage gaps and unprovisioned independent trust. Final-SHA full gates and remote PR/main/cancellation evidence are missing; no Wave 0/P0 closure or governance enforcement claim.

## Wave 1 — Identity + Tenancy
- OIDC login, organizations/tenants, membership, RBAC, RLS and audit.
- Cross tenant negative tests and token security suite.
**Exit:** independent tenant A/B sessions prove no leak.

## Wave 2 — CRM Contacts
- Contacts CRUD, filters/pagination, tags, custom field definitions and values, activity timeline.
- Optimistic concurrency, audit, contacts API contract, TS client generation.
**Exit:** 50k contacts/tenant, 50 concurrent users; p95 <400ms trong benchmark environment ghi rõ CPU/RAM/DB/network; permission/concurrency/audit tests pass. Chỉ seed fixture không đủ đóng wave.

## Wave 3 — Integration Kernel
- Module manifest schema+registry, install/uninstall, connector credentials, verified webhook ingestion/inbox, outbox worker, delivery retries/DLQ.
- Contact external identity links, unmatched queue, schema registry.
**Exit:** cross-host DemoModule verified publisher + approved schema v2 install; panel/SSO/event descriptors được resolve không sửa Core; webhook binding/rotation/SSRF/concurrent dedup pass. Remote action effects được chứng minh ở Wave 4.

## Wave 4 — Automation P0
- Trigger registry, safe condition DSL, add-tag and remote action, versioned rule definitions, history, pause/resume.
- Idempotency and crash/replay tests.
**Exit:** event from DemoModule triggers exactly one logical action.

## Wave 5 — Hardening / MVP release
- End-to-end UX, Playwright, failure injection, security testing, backup/restore, dashboards, docs, operator runbook and release checklist.
**Exit:** all P0 acceptance passed; backup/WAL restore đo đạt RPO/RTO, outage/recovery và remote DemoModule E2E có evidence; no security blocker. Theo dõi từng US tại `../engineering/03-ACCEPTANCE-MATRIX.md`.

## P1 — Product and Marketing
- Consent management, provider abstraction (SES/Postmark/etc.), transactional vs marketing mail, unsubscribe, suppressions, bounces/complaints.
- Contact-centric Temporal long-running sequences (delay/branch/cancel), segmentation, deals/pipelines, company model, basic campaigns.
- Broker when fan-out demands it; SDK NuGet/npm published, backwards-compatible webhooks.

## P2 — Rich Ecosystem
- Visual workflow builder using React Flow, contact panel widgets, reporting, module developer portal, enhanced reconciliation, billing/license per tenant, advanced workflow versions.

## P3 — Scale / Platform commercial
- Marketplace/signing/trust tiers, AI tools with human approval and auditing, ClickHouse/warehouse where event scale demands, deployment isolation, HA/multi-region, on-prem integration agent.

## P4 — Integration of real products
- LMS: enrollment/progress summary, entitlement actions, not LMS DB sharing.
- Trading: consent-scoped activity snapshots and account linkage, not copying trading journal wholesale.
- Backcom: rebate settlement state and notifications only; ledger in Backcom domain.
- Third-party connectors for CRM contact export/import.

## Non-goals of MVP
Do not build a generalized plugin code loader, Kubernetes operator, fully dynamic UI marketplace, proprietary auth server, full email delivery infrastructure or distributed transaction framework.
