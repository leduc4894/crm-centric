# Wave 1 branch split — 2026-11-26

Objective: restore local main to final Wave 0 commit and retain all Wave 1 commits on a separate branch. User subsequently instructed that the currently uncommitted handoffs and Plan 4 should not be added to the Wave 1 branch.

Verified clean tracked starting tree, main `1f87d2f` with 53 commits after `40bc290`, and local `origin/main` at `40bc290`. Created local branch `wave-1` at `1f87d2f`, then reset local `main` to `40bc290`. Verified `main` matches local `origin/main` and `git rev-list --count main..wave-1` returns 53. Existing feature branches and linked worktree were preserved; no push, remote change or branch deletion. Existing handoffs already committed in Wave 1 history remain part of those existing commits. Previously untracked handoffs and Plan 4 were left untracked and not committed to either branch; this handoff is also untracked. Untracked files are present in this checkout independently of the selected branch.

Verification: Git status, branch tips, 53 commit count and worktree list inspected; no tests run because no application code was changed. Plan 4 remains at `docs/superpowers/plans/2026-11-26-wave-1-plan-4-browser-oidc.md` as an untracked file; previous plan status and security-gate limitations are recorded in `docs/handoff/2026-11-26-wave-1-plan-4-handoff.md`. Next: user can decide how to handle the untracked documents and whether to publish branches; Wave 1/P0 remain open.

## Move documentation to Wave 1

User subsequently requested committing the untracked handoffs and Plan 4 to `wave-1` and removing those untracked copies from the `main` checkout. Branch `wave-1` is checked out in `.local/worktrees/wave-1-invites-audit`; the eight documents were copied to that worktree and committed there. The corresponding main-checkout copies are removed only after verifying their committed bytes, except this handoff, whose new section is the sole deliberate difference. No product code, remote state or tests were changed/run. Check Git status and log for the resulting commit and clean main state.
