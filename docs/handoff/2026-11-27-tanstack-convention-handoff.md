# TanStack frontend convention handoff — 2026-11-27

Objective: document the user's approved convention to prefer established TanStack libraries in future frontend features, and commit the documentation. Reviewed [frontend engineering guide](../engineering/02-REPO-CI-CD.md), [Wave 2 design](../superpowers/specs/2026-11-27-wave-2-crm-contacts-design.md), [Plan 6 handoff](2026-11-27-wave-2-plan-6-handoff.md), current package manifest and branch state.

Status: added the convention to the engineering guide for Query/Table and, once implemented and stable, Router/Form; documented appropriate exceptions and tenant/cache/ETag, server pagination, lossless number, accessibility and dependency constraints. No application code, contract or dependencies changed. Plan 7/8 remain future work; full suite is at Plan 9 on GitHub CI. Security remains FAIL/deferred and Wave 2/P0 OPEN.

Branch `wave-2`, starting commit `a0a7a6f`, clean at session start. Changed files: engineering guide and this handoff; user requested their commit. Verification: no test suite or CI run for this documentation-only change; staged whitespace and final Git status checked at commit. Next: apply convention to future feature plans; continue Wave 2 in order and report actual gate results.
