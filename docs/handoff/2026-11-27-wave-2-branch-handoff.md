# Wave 2 branch handoff — 2026-11-27

Objective: create the `wave-2` branch at the user's request.

Status: created and switched to local branch `wave-2` from `main` at `db04aea`. No Wave 2 implementation was started. See [Wave 1 CI handoff](2026-11-27-wave-1-pr3-ci-handoff.md) for the deferred security failure and outstanding acceptance conditions; creating this branch does not establish acceptance or authorize bypassing those conditions.

Changes: this handoff is uncommitted. No application files changed. No remote branch was created.

Verification: `git switch -c wave-2` PASS. Tests not run (branch creation and documentation only).

Next: assess the outstanding Wave 1 and Wave 2 entry conditions before implementation.

## Wave 2 scope discussion

Reviewed the backlog, acceptance matrix, contact state and data model to answer the user's question. Recommended five implementation/verification plans: contact contract and CRUD; search/filter/pagination; tags and custom fields; timeline and contact UI; exit verification and benchmark. These are a proposed breakdown, not approved or implemented plans. First assess remaining applicable Wave 1 non-security requirements on the appropriate candidate; security remains FAIL/deferred under the user's existing decision. Remote DemoModule and recovery/restore are later P0 acceptance requirements, not automatically Wave 1 exit criteria. No tests run for this documentation-only review.

## Wave 2 design spec

User requested a spec and clarified that plan-specific related tests run locally; the full suite runs only at Wave 2 exit on GitHub CI, not locally. Drafted [Wave 2 design](../superpowers/specs/2026-11-27-wave-2-crm-contacts-design.md), including the prototype as UI source, five proposed plans, contract/security boundaries, testing and exit evidence. Spec awaits user approval; no implementation or plan execution started. Reviewed backlog, architecture, acceptance matrix, CRM OpenAPI, prototype and frontend package. No tests run in this documentation-only session. Next: user review/approval of spec, then write detailed plans; first assess applicable non-security Wave 1 entry conditions. Security remains FAIL/deferred and P0 OPEN.

Documentation verification: initial `git diff --cached --check` reported trailing whitespace in the spec; corrected before rerunning. Test suite not run.

## Plan 1 authoring

User requested Plan 1 after reviewing the design direction. Created [Contact CRUD plan](../superpowers/plans/2026-11-27-wave-2-plan-1-contact-crud.md), covering entry-evidence assessment, contract/event intent, migration and transactional journal, tenant-scoped CRUD, concurrency and focused verification. The plan preserves the user's instruction: related tests local; full suite at Wave 2 exit on GitHub CI. Plan is proposed, not executed; no source code or contract changed. The prior spec commit is `d02f869`; this handoff update and plan are uncommitted. No tests run for this documentation-only session. Next: review the plan; when implementation is authorized, evaluate Wave 1 entry conditions before code. Security remains FAIL/deferred, P0 OPEN.

## Commit request

User requested committing all current changes on `wave-2`: this handoff update and the Plan 1 document. No implementation or tests were run. Verification: `git diff --check` passed before staging; staged whitespace and final status checked as part of commit. The plan remains unexecuted and the Wave 1 entry assessment remains the next prerequisite.
