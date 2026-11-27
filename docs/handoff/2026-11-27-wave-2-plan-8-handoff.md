# Wave 2 Plan 8 handoff — 2026-11-27

Objective: write and commit the user's requested Plan 8 for TanStack Form. Read [Plan 7 handoff](2026-11-27-wave-2-plan-7-handoff.md), [Wave 2 design](../superpowers/specs/2026-11-27-wave-2-crm-contacts-design.md), frontend convention, current contact/profile/attribute/note/settings forms, and official TanStack Form validation documentation. Branch `wave-2` started clean at `8e533ff`.

Status: [Plan 8](../superpowers/plans/2026-11-27-wave-2-plan-8-tanstack-form.md) drafted; no implementation, dependency installation, application code or contract change. Plan covers explicit ETag review and drafts, scope isolation, raw numeric tokens, tag/field/note forms and focused local verification. Existing empty-string/null ambiguity is explicitly carried forward for review rather than silently changed. Plan 7 focused review complete; actual-base compatibility and hosted gate evidence remain open, security FAIL/deferred and Wave 2/P0 OPEN. Plan 9 owns hosted full suite and benchmark.

Changed files: Plan 8 and this handoff. User requested commit; staged whitespace and final branch status are checked as part of commit. No application tests or CI run for this documentation-only session. Next: review/implement Plan 8 when requested, choose and pin supported stable Form version, run only related local tests and report actual results.
