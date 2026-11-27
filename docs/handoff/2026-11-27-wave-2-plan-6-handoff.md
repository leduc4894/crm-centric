# Wave 2 Plan 6 handoff — 2026-11-27

Objective: write Plan 6 for TanStack Table after Plan 5. Reviewed current branch `wave-2` at `3017559`, [Plan 5 implementation and review](2026-11-27-wave-2-plan-5-handoff.md), ContactList, Query data flow, existing UI tests and Wave 2 design. Plan 5 focused local verification and independent review are recorded in that handoff; actual-base compatibility remains `CONTRACT_POLICY_REVIEW_REQUIRED`, security FAIL/deferred, and Wave 2/P0 OPEN. Do not transfer prior verdicts to a new candidate.

Status: [Plan 6](../superpowers/plans/2026-11-27-wave-2-plan-6-tanstack-table.md) is drafted, not implemented. It preserves server filtering/order/cursor pagination, scoped Query state, ETag/reconciliation, existing prototype design and real-provider browser coverage. Select and pin a supported stable Table release during execution. User requested the plan only; no package or application code changed. New plan and this handoff are uncommitted; no tests or CI run in this documentation-only session. Verification: whitespace checks and `git status` at handoff.

Next: user review; implement only when requested, with focused local tests. Plan 7 Router and Plan 8 Form follow; full suite runs on GitHub CI and benchmark at Plan 9. Preserve any pre-existing work and report actual evidence.

User requested committing the Plan 6 document and this handoff on `wave-2`. Staged whitespace verification and final status are checked with the commit. No implementation, test suite or CI ran in this documentation-only session; Plan 6 remains unexecuted.
