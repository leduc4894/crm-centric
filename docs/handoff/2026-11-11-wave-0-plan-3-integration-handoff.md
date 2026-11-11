# Wave 0 Plan 3 — merge and publication handoff

## Objective, authorization and status

User explicitly requested "merge và push" on 2026-11-11, after the controller
reported the branch ready and the user chose the fast-forward merge into `main`.
This supersedes the previous keep-branch/no-publication scope **for this
integration only**; it authorizes the merge and the push of `main`, nothing else.

Fetched origin and verified no remote divergence: `origin/main` was still
`3af03d0` and is an ancestor of the reviewed feature head, so no rebase was
needed and no conflict was possible. Local `main` was at `5257abf` and was
fast-forwarded with `git merge --ff-only` to the reviewed feature head
`ec781b0`. The merged tree is byte-identical to the reviewed tree
(`git diff --stat ec781b0..HEAD` is empty). No source edit, dependency change,
migration or deployment was performed during the integration.

After the merged-main verification below, `git push origin main` succeeded:
remote `main` advanced from `3af03d0` to `ec781b0`. Local `HEAD` and
`origin/main` are equal and the working tree was clean after that push.

The live CI workflow `.github/workflows/contracts.yml` ran on the pushed `main`
and **succeeded** (run `42987032191`, 22 s). This is the workflow that the
final review of Task 7 found **red** before the fix wave; it is green now.

This documentation-only handoff follows the publication and is committed/pushed
under `docs: record Plan 3 merge and publication`; resolve its final SHA from
history. The final publication check must confirm local/remote equality again.

## Fresh verification on merged main

Run on the merged `main` at `ec781b0`, with the pinned Node 24.22.2 on the
`PATH`, `LD_LIBRARY_PATH=/tmp/plan3-libs:/tmp/plan3-browser-libs/usr/lib/x86_64-linux-gnu`
and the existing `.venv` reused. These are fresh integration executions, not
reused counts.

- `bash scripts/check-plan3.sh --base-ref 40a7c91`: **PASS**, exit 0, 4m31.502s,
  nine steps exit 0 (the browser step recorded as `not run: the browser suite
  needs the local Docker stack; pass --with-browser to run it`), source digest
  `70b89a35023978507589a084fcaef21c0f71e147073ab1a59fb29f3ac01c7066` unchanged
  during the run.
- `npm run test -w apps/crm-web`: **PASS**, 54 of 54 tests in 5 files, 6.37 s.
- `python3 scripts/contracts/check.py`: **PASS**, exit 0,
  `SDK_GENERATED_CURRENT`, correlation identifier present.
- `git diff --check`: **PASS**, exit 0, no output.

The measurements of the independent scoped re-review of the fix rounds 6 and 7,
on the reviewed commit `88f5542`, stand unchanged: the Python contract suite
**87 tests, 0 skipped, exit 0**; the aggregate harness **16 of 16**; the
fail-closed negative cases **3 of 3**; the CI-like checkout **87 tests, 55
skipped with a visible reason, 32 passed**; the scratch-cleanup harness exit 0;
`check.py` `SDK_GENERATED_CURRENT`.

The browser suite was measured separately against the real local stack: `dev-up`
exit 0 (project `crm-local-91859983f1e0`, API `http://localhost:5080`),
`dev-smoke` exit 0, `npm run test:e2e -w apps/crm-web` **4 passed, 9.4 s**,
`dev-down` exit 0 with the stack stopped and no container left.

### The CI rehearsal on the runner's own interpreter

The scoped re-review could not verify the workflow on **Python 3.15.2**, the
version `actions/setup-python` installs, because only Python 3.12.3 was
available on the host; it recorded this as not verified. The controller closed
that gap by running the workflow's own command in a `python:3.15.2-slim`
container against a clean checkout of the merged tree, with the locked
dependencies:

- `python -m pip install -r requirements-contracts.lock` — exit 0
- `python -m pip check` — "No broken requirements found"
- `python -m unittest discover -s tests/Contract -v` — **Ran 87 tests, OK
  (skipped=55)**: the 55 Plan 3 tests skipped with a visible reason, the 32
  pre-existing tests passed.

The `import prerequisites` discovery mechanism and the `unittest.skipUnless`
guards therefore behave identically on the interpreter the runner uses. The
scratch checkout and the container image were removed afterwards.

## The base revision of the aggregate

Fix round 6 changed `tools/contracts/surface/compare-typescript.cjs`, which the
compatibility gate treats as a **policy-like** file, so the gate refuses to
compare a candidate against a base whose policy-like files differ and reports
`CONTRACT_POLICY_REVIEW_REQUIRED`. The change was reviewed — it is the F-3 fix
of the final review of Task 7 — but the consequence is that a base at or after
`40a7c91` is required for a run that is expected to pass. With `--base-ref
408ded6` the gate stops at the compatibility step, by design. The gate code was
not touched, no threshold was relaxed and no waiver was introduced. The merge
base of the branch remains the revision a reviewer compares the candidate
against; only the **self-test** base of the aggregate moved.

## The scratch cleanup that this branch carries

The Plan 3 harnesses used to delete their own scratch trees with a bare
`rm -rf`. Under an agent runtime whose `rm` is a recoverable-delete shim, that
copies a multi-gigabyte scratch tree into the user's trash instead of freeing
the disk — 207 runs had deposited about 55 GB in `~/.local/share/Trash`. Fix
round 7 added `scripts/lib/scratch-cleanup.sh`, which resolves an **absolute**
rm binary and routes all eight scratch deletions of the slice through it, plus
the mutation-sensitive harness `tests/Plan3/scratch-cleanup.sh`, which grew the
aggregate harness from 15 to 16 cases. A developer machine has no such shim and
was never affected.

## References, remaining work and next steps

- [Implementation handoff](2026-11-10-wave-0-plan-3-implementation-handoff.md).
- [Plan 3 evidence](../engineering/07-WAVE-0-PLAN-3-VERIFICATION.md).
- [Plan 3 design](../superpowers/specs/2026-11-10-wave-0-plan-3-frontend-contract-toolchain-design.md).

Active branch is `main`; the feature branch
`feat/wave-0-plan-3-frontend-contract-toolchain` is retained locally, as
`feat/wave-0-plan-1-backend-postgresql` and
`feat/wave-0-plan-2-local-stack-observability` are. No worktree, force push,
pull request, tag or package publication was created. No intended uncommitted
work remains after this handoff commit. There is no current integration blocker.

Next: publish this handoff, confirm `origin/main` equals local `HEAD` and that
the working tree is clean.

**Still OPEN, not claimed by this merge:** Wave 0 / P0 acceptance; remote
DemoModule end-to-end; tenant isolation; outage/recovery and restore drills;
remote CI wiring of the five Plan 3 harnesses and of the prerequisites of the
55 contract tests, both of which Plan 4 owns; production hosting; package
publication; database migration. The three Minor findings of the scoped
re-review are recorded in the implementation handoff: two documentation counts
were corrected, and the module-level granularity of the prerequisite guard is an
accepted design decision that is now disclosed in the CI disclosure.
