# Wave 1 handoff — 2026-11-15

Objective: continue after Wave 0 by identifying and preparing the first Wave 1 slice. Current status: design proposal only; no implementation or Wave 1 acceptance claimed. User requested continuation; no separate design approval recorded.

Read [Wave 0 closure handoff](2026-11-14-wave-0-remote-gates-handoff.md), [Wave 0 MVP scope](../engineering/09-WAVE-0-CI-MVP-SCOPE.md), [roadmap](../roadmap/01-BACKLOG.md), identity/access architecture, IdP configuration and acceptance matrix. Wave 0 CI MVP is closed by documented PR and main-push evidence; P0 and later runtime acceptance remain open. Some older README/roadmap status text predates closure; scope decision is the controlling update.

Created [Wave 1 draft design](../superpowers/specs/2026-11-15-wave-1-identity-tenancy-design.md), recommending verified API identity and tenant context/RLS first, then membership/invite, browser login and complete Wave 1 acceptance. No public contract, dependency, runtime or database changes. Branch `main`, starting HEAD `40bc290`; draft and this handoff are uncommitted. No remote operations.

Verification: inspected repository status and current source/docs; no tests run (documentation-only). Next: review/approve the proposed design, prepare an implementation plan with explicit bootstrap/RLS privileges and contract mapping, then execute tests first. Do not claim Wave 1 or P0 complete without required evidence.

## Continuation after user approval

User approved proceeding with the split implementation. Created [delivery map](../superpowers/plans/2026-11-15-wave-1-delivery.md) with five sequential slices, security review focus and evidence gates. The design is approved in principle by the user's instruction to proceed; detailed technical choices remain to be made within the existing invariants. Inspected API entry point, DbContext, grants, test fixtures and tenant OpenAPI: runtime currently only maps operational endpoints. `dotnet` is not on host PATH; Docker server responds, but no test or build has been attempted in this continuation. No runtime implementation yet. Next: prepare an executable first-slice plan, including a safe membership bootstrap privilege boundary and actual pinned SDK/test runtime, then implement test-first. Do not treat the delivery map as a completed first-slice plan or Wave 1 as implemented.

## First implementation plan

User requested the first plan. Created [Plan 1: identity and tenant foundation](../superpowers/plans/2026-11-15-wave-1-plan-1-identity-tenant-foundation.md), covering JWT, PostgreSQL bootstrap/RLS and privilege boundary, transaction-local tenant context, contract-compatible tenant list, security regressions and evidence. It explicitly states the app credential/SQL principal trust limitation and separates this slice from Wave 1 exit. No implementation, dependency, schema or runtime changes; plan is uncommitted alongside prior Wave 1 documents. Verification for this documentation session: `git diff --check` PASS; plan relative Markdown links PASS; runtime tests not run. Next: execute Plan 1 test-first using pinned SDK and real PostgreSQL, after reviewing the plan; do not claim implementation or acceptance.
