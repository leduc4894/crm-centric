# TanStack research handoff — 2026-11-27

Objective: research which TanStack libraries suit the CRM. Reviewed package manifest, [frontend convention](../engineering/02-REPO-CI-CD.md), [Wave 2 design](../superpowers/specs/2026-11-27-wave-2-crm-contacts-design.md), and [Plan 7 handoff](2026-11-27-wave-2-plan-7-handoff.md), plus official TanStack documentation. Query and Table are installed; Router is installed with Plan 7 still undergoing execution/review at lookup time. Form is planned for Plan 8. Suggested prioritizing these and assessing Virtual/Pacer only when needed; TanStack DB was marked RC in official docs at lookup and should not enter MVP without required ADR and stable release. No scope changes or package installations authorized or performed.

Branch `wave-2` at lookup `4e09b56`; pre-existing in-progress changes to `apps/crm-web/tests/e2e/contacts.spec.ts`, `apps/crm-web/tests/e2e/realOidcFixture.ts`, and `docs/handoff/2026-11-27-wave-2-plan-7-handoff.md` were preserved. This handoff is uncommitted. No tests or CI run in this research session; no application code changed. Plan 7 review and hosted Wave 2 gates remain open as recorded in its handoff; security FAIL/deferred and P0 OPEN. Next: continue Plan 7; evaluate Plan 8 Form preserving ETag/drafts, tenant isolation and lossless numbers; full suite and benchmark at Plan 9 on GitHub CI.

## Charts follow-up

User asked about charts. Consulted official TanStack Charts overview and release installation docs: site states web Charts 1.0 stable API, while historical alpha policy applies to pre-1.0; repository docs may describe unreleased main, so confirm exact release source, supported peers and security before choosing/pinning. CRM roadmap puts advanced reporting after P0; recommend evaluate Charts alongside alternatives when a chart use case and real data exist, no dependency installed or plan/scope changed. No tests run. Preserve concurrent Plan 7 work.

## Chart-library comparison

User asked whether to choose TanStack Charts or Apache ECharts. Reviewed official chart type, renderer, accessibility and stability documentation; recommended ECharts provisionally for varied future CRM analytics, with a small tenant-safe proof of concept before adopting and an explicit stable-version/dependency/accessibility check. No chart dependency, implementation or roadmap decision was made. No tests or CI run; this research handoff remains uncommitted.

## Approved ECharts and library roadmap

User selected Apache ECharts and requested recording the library evaluations and appropriate timing. Updated [technology radar](../roadmap/02-TECH-RADAR.md) with Query/Table/Router/Form, Virtual, Pacer, DB, Start, Store/Hotkeys/AI and ECharts evaluations; updated [backlog](../roadmap/01-BACKLOG.md) to use ECharts if Wave 5 P0 dashboards need charts or for later reporting without broadening P0. Reviewed [Plan 7 final handoff](2026-11-27-wave-2-plan-7-handoff.md): Plan 7 local review complete, Plan 8 next; no hosted or deployment acceptance inferred. This handoff was pre-existing untracked research work and was preserved and extended. No dependencies installed, code/contract changed, tests or CI run. Branch `wave-2` started at `d0cc624`; radar, backlog and this handoff are uncommitted. Next: review documentation and continue Plan 8 when requested; before ECharts integration validate stable release, lockfile, data source/tenant boundaries, accessibility and bundle. Security FAIL/deferred, Wave 2/P0 OPEN.

User requested committing the outstanding documentation on `wave-2`: roadmap backlog, technology radar and this handoff. Staged whitespace verification and final status are checked with the commit. No application implementation, tests or CI ran in this documentation-only session.
