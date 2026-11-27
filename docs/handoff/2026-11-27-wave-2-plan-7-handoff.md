# Wave 2 Plan 7 handoff — 2026-11-27

Objective: write Plan 7 for TanStack Router after the user's report that Plan 6 is complete. Reviewed `wave-2` at `36e933c`, [Plan 6 handoff](2026-11-27-wave-2-plan-6-handoff.md), [Wave 2 design](../superpowers/specs/2026-11-27-wave-2-crm-contacts-design.md), current ContactShell/App, auth callback prelude, tenant/query scope and tests. Plan 6 focused verification/review is recorded in its handoff; actual-base compatibility remains review-required on last recorded candidate and security FAIL/deferred. Wave 2/P0 remain OPEN.

Status: [Plan 7](../superpowers/plans/2026-11-27-wave-2-plan-7-tanstack-router.md) drafted, not implemented. It covers code-based routes, safe OIDC callback, existing list/detail, a tenant-scoped custom-field settings route from the spec, session/cache isolation and ETag reconciliation. No application code, package, lockfile or contract changed. Plan and this handoff are uncommitted. No tests or CI run in this documentation-only session; whitespace and status checked before handoff.

Next: review plan and implement when requested, selecting/pinning supported stable router release at execution. Run related tests local; full suite on GitHub CI and benchmark remain Plan 9. Plan 8 Form follows Plan 7. Do not bypass gates or infer current-candidate CI from past evidence.

User requested committing the Plan 7 document and this handoff on `wave-2`. Staged whitespace verification and final status are checked with the commit. No implementation, test suite or CI ran in this documentation-only session; Plan 7 remains unexecuted.
