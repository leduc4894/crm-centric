# Phased Delivery Plan / Sprint Backlog

## Wave 0 — Repo / architecture runway
- ADR and module boundary baselines, repo scaffolding, compose local stack.
- Install .NET 10/EF Core 10 and front-end toolchain versions; CI build/unit tests.
- Fake OIDC provider for tests, production-ready IdP config documented.
**Exit:** cold-start and CI reproducible, structured logs + trace IDs.

## Wave 1 — Identity + Tenancy
- OIDC login, organizations/tenants, membership, RBAC, RLS and audit.
- Cross tenant negative tests and token security suite.
**Exit:** independent tenant A/B sessions prove no leak.

## Wave 2 — CRM Contacts
- Contacts CRUD, filters/pagination, tags, custom field definitions and values, activity timeline.
- Optimistic concurrency, audit, contacts API contract, TS client generation.
**Exit:** 50k contact fixture and permission tests.

## Wave 3 — Integration Kernel
- Module manifest schema+registry, install/uninstall, connector credentials, verified webhook ingestion/inbox, outbox worker, delivery retries/DLQ.
- Contact external identity links, unmatched queue, schema registry.
**Exit:** cross-host DemoModule install without CRM Core changes.

## Wave 4 — Automation P0
- Trigger registry, safe condition DSL, add-tag and remote action, versioned rule definitions, history, pause/resume.
- Idempotency and crash/replay tests.
**Exit:** event from DemoModule triggers exactly one logical action.

## Wave 5 — Hardening / MVP release
- End-to-end UX, Playwright, failure injection, security testing, backup/restore, dashboards, docs, operator runbook and release checklist.
**Exit:** all P0 acceptance passed and no security blocker.

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
