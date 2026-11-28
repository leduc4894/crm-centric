# Wave 3 scope inquiry — 2026-11-28

Objective: answer what Wave 3 contains after Wave 2 implementation. Reviewed [roadmap](../roadmap/01-BACKLOG.md), [Wave 2 exit evidence](../engineering/16-WAVE-2-EXIT-VERIFICATION.md), [Wave 2 Plan 9 handoff](2026-11-28-wave-2-plan-9-handoff.md), and [event concurrency design](../architecture/05-EVENTS-SYNC-AUTOMATION.md). Wave 3 is Integration Kernel; its roadmap exit and work-order constraints remain applicable. Wave 2 implementation has a functional checkpoint, but Wave 2 and P0 remain OPEN under acceptance instructions. Security FAIL is deferred per prior user decision; no further waiver or new approval was granted.

Branch `wave-2`, initial HEAD `61a4b91` (ahead of origin by one commit); initial worktree clean. This handoff is the only file changed in this session and is uncommitted. No application code or contract changed. Verification: inspected `git status --short --branch`, `git log -1`, roadmap and relevant handoff/evidence; tests and CI not run for this documentation-only inquiry. No deployment. Next: clarify and address remaining Wave 2/work-order acceptance prerequisites before starting Wave 3 implementation; prepare a contract-first Wave 3 design and incremental plans when requested, preserving existing security deferral and not claiming P0 acceptance.

## Proposed plan split

User requested a Wave 3 plan breakdown following Wave 2. Reviewed the Wave 2 design, roadmap, module and webhook contracts, and DemoModule scenario. Proposed incremental contract/trust, installation, external identity, webhook inbox, event projection, outbox delivery, integration UI/descriptors, independent DemoModule and exit verification slices. This is a discussion proposal, not an approved design or implementation plan; ordering and exact plan boundaries require detailed design against existing code. Wave 2/P0 status and security deferral are unchanged. No tests or CI run; `git diff --check` checks documentation whitespace. No commit or deployment.

## Wave 3 spec draft

User approved the proposed nine-plan direction and requested a Wave 3 spec, focused related tests local and full suite in the last plan on GitHub CI. Wrote [Wave 3 design](../superpowers/specs/2026-11-28-wave-3-integration-kernel-design.md), a draft for review, not implementation approval. It preserves existing contract requirements and distinguishes Wave 3 descriptor resolution from Wave 4 action effects and Wave 5 P0/restore. Independent read-only builder review found no material contradiction; incorporated explicit inbox encryption/tombstone protection, health/status, and Demo action endpoint boundaries. No application or contract changed, no tests, CI, deployment or commit. Verification: checked 12 relative links, 0 missing; `git diff --check` run for documentation. Branch `wave-2` at `61a4b91`, ahead of origin by one; this handoff and the new spec are untracked. Next: user review of the design; after approval develop detailed plans, while resolving Wave 2 work-order prerequisites before implementation. Security remains FAIL/deferred; Wave 2 and P0 OPEN.

## Move spec to main

User interrupted the Plan 1 request and asked to switch to main, bring the Wave 3 spec there, and delete the Wave 2 branch. Fetched origin; `origin/wave-2` was an ancestor of `origin/main`, and the local `wave-2` tip's tree differed from `origin/main` only by 12 additional handoff lines on main, so its documentation was preserved. Switched to `main`, fast-forwarded from `db04aea` to `dd5dca5`, retained this handoff and the Wave 3 spec, and deleted local `wave-2` (tip `61a4b91`). Remote `origin/wave-2` and separate `wave-2-exit-evidence` worktree were not changed. Plan 1 was not written. Verification: Git ancestry/tree comparison, `git status`, and documentation whitespace check; tests/CI not run. Security FAIL/deferred and Wave 2/P0 OPEN remain unchanged. Next: review the spec and write Plan 1 when requested; assess work-order prerequisites before implementation.

## Remote branch cleanup

User requested deleting the remaining Wave 2 branch. `git push origin --delete wave-2` succeeded; the local branch had already been deleted and `main` remains at `ea72135`, ahead of `origin/main` by one. The separate `wave-2-exit-evidence` branch/worktree was not changed. No tests or CI run; no product changes. Next: review the Wave 3 spec and plan further work when requested.

## Create Wave 3 branch

User clarified that Wave 3 must branch from main and the spec commit must belong only to Wave 3. Created `wave-3` from local spec commit `ea72135`, whose parent is `dd5dca5` (`origin/main`), and moved local `main` back to `origin/main` while on `wave-3`. Spec remains on `wave-3`; no main push or remote branch creation. This handoff update records the local and remote Wave 2 cleanup and branch move. No tests or CI run. Next: write Wave 3 Plan 1 when requested, and check work-order prerequisites before implementation.
