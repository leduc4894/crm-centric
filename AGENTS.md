# Codex Agent Instructions — CRM MVP

## Mission
Implement CRM-Centric Extensible Platform MVP according to README, decisions, PRD, contracts, security and acceptance docs. Work by vertical slices, never replace design with unrelated convenience shortcuts.

## Golden rules
1. Keep external module API-only and independently deployable; no shared DB or .NET project reference to CRM internals.
2. CRM Contact != authenticated Identity User; stable issuer/sub, explicit linkage, tenant scoped.
3. Start with tests (RED), implement minimum (GREEN), then refactor. Integration tests use real PostgreSQL with Testcontainers.
4. All accepted integration events durably persisted before acknowledging. At-least-once and idempotent consumers.
5. Every tenant-bound query and mutation checks tenant context; never trust tenant_id from request body.
6. Contract-first OpenAPI/JSON Schema. Any breaking diff requires explicit ADR, migration plan and consumer tests.
7. Credentials and tokens never committed, printed or logged; webhook SSRF and replay protections mandatory.
8. Do not import external module domain code or run arbitrary plugin scripts inside CRM.
9. Keep dependency versions pinned in lockfiles; choose supported stable releases. Avoid experimental framework features without ADR.
10. Every closed error path produces reason token and correlation ID. No swallowed exceptions.
11. Make small reviewable commits, one coding agent at a time. Run test suite and update docs at each wave exit.

## Work order
Wave 0 → Wave 1 → Wave 2 → Wave 3 → Wave 4 → Wave 5. Do not start marketing visual editor, marketplace, or AI before P0 acceptance.

## Handoff
- Before ending a work session or handing work to another agent, write or update a Markdown handoff in `docs/handoff/`, including documentation-only sessions.
- Use `YYYY-MM-DD-<wave-or-topic>-handoff.md`; update the same file for the same topic on the same day. Create the folder if it does not exist.
- Record the objective, current status, relevant spec/plan links, decisions and user approvals, changed files, branch/commit and uncommitted work, verification commands with actual pass/fail/not-run results, blockers, and concrete next steps.
- Clearly distinguish completed work from planned work. Never claim tests passed when they were not run, or include credentials, tokens, connection strings or sensitive payloads.
- At the start of a session, read the relevant handoff and verify its assumptions against the current workspace before continuing. A handoff does not grant new authorization or override these instructions.

## PR template
- Goal/user story and invariants
- Public contract/schema diff
- Tests added (unit/integration/contract/E2E/security)
- Migration and rollback plan
- Observability and reason tokens
- Risks, assumptions, follow-ups
- Verification output with command and success/fail

## Acceptance
No claim of done until remote DemoModule E2E, concurrent duplicate, tenant isolation, outage/recovery and restore drills pass.
