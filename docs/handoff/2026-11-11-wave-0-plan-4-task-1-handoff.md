# Wave 0 Plan 4 Task 1 handoff

## Objective, authorization and status
Implement revision provenance validation under the parent agent's explicit Task 1 delegation, on `feat/wave-0-plan-4-ci`, without worktree, remote actions or unrelated changes. Task implementation is complete locally; Wave 0/P0 acceptance remains OPEN.

Relevant documents: [plan](../superpowers/plans/2026-11-11-wave-0-plan-4-ci-wave-exit.md), [spec](../superpowers/specs/2026-11-11-wave-0-plan-4-ci-wave-exit-design.md), and the local `.superpowers/sdd/2026-11-11-wave-0-plan-4-ci-wave-exit/task-1-brief.md`. Read the existing Plan 4 spec handoff and checked current branch/status before implementation; its documentation-only authorization is superseded for this scoped task by the parent delegation.

## Completed behavior and decisions
- `scripts/ci/revision.py`: immutable `RevisionContext`; PR event merge SHA/checkout equality, exact ordered base/head parents, commit-object availability and merge-base proof; push before/after and nonforced ancestor proof. SHA inputs must be lowercase full nonzero SHA-1 identifiers.
- GitReader uses local read-only Git queries with 15-second subprocess timeouts, replacement objects disabled, lazy fetch disabled, and captured diagnostics. No fetch/reset/rebaseline.
- Unsupported manual/merge-group events fail closed. Event name may be supplied explicitly; direct calls can infer PR/push from payload shape. CLI uses actual HEAD and treats optional checkout SHA as an assertion, not authority.
- CLI emits allowlisted SHA JSON on success and reasonToken/correlationId on failure, without raw event/process diagnostics. Event files have a 4 MiB size limit.
- `tests/CI/test_revision.py`: 11 synthetic real-Git tests, including immutable context, invalid SHAs, malformed event safety, stale candidate, wrong parent order/nonmerge, advanced event base, missing objects, push failures and unsupported events.
- No public API/schema contract, database, runtime application or security policy changes. Rollback is revert of this task commit; no migration.

## Branch, changes and verification
Commit subject: `feat(ci): validate event revision provenance` (resolve SHA from local history). Changed files are the resolver, its tests and this handoff. Local task report is ignored under `.superpowers/`; no unrelated tracked work changed.

- RED: `python3 -m unittest discover -s tests/CI -p test_revision.py -v` against explicit behavioral stub: exit 1, 11 tests, 1 failure + 10 NotImplementedError errors, showing missing resolver/CLI behavior.
- GREEN: same command: exit 0, 11 tests passed.
- `python3 -m unittest discover -s tests/CI -v`: exit 0, 11 tests passed.
- `python3 -m unittest discover -s tests/Local -v`: exit 0, 20 tests passed.
- `python3 -m unittest discover -s tests/Contract -v`: exit 1, 59 discovered, 15 failures, 2 import errors, 7 skips. System Python lacks openapi_spec_validator; node toolchain is unusable; compatibility failures report SDK_TOOLCHAIN_REVIEW_REQUIRED; generation fixtures fail setup. Exact failing identities are in the local task report. This is not a passing full-suite result.
- `python3 -m py_compile scripts/ci/revision.py tests/CI/test_revision.py`: exit 0.
- `git diff --check`: exit 0 before commit.
- Backend, browser, Compose, remote CI, remote DemoModule, duplicate/tenant/outage/recovery/restore drills: NOT RUN for Task 1.

## Limitations and next steps
Parent should review and integrate Task 1 before Task 2. Provision pinned full toolchain before full-suite evidence. The resolver proves the immutable event and locally checked-out object relationship, not freshness against a live remote branch; a newer actual base outside the event requires a new GitHub event/candidate/run. Merge queue enforcement remains UNKNOWN and unsupported until event-specific proof and remote evidence are supplied. No push/merge or repository settings changes performed.

## Review fix round 1
Reviewer identified argparse errors outside the safe error boundary. Confirmed by regression: unknown option with synthetic sentinel, unexpected positional input, missing option value and missing required option all exited 2 using argparse diagnostics. Added a parser override that raises the existing safe RevisionError without printing parser input, and moved parsing into the safe boundary. Help remains normal explicit help output.

`python3 -m unittest discover -s tests/CI -p test_revision.py -v` RED: 12 tests, 4 failing subcases for incorrect exit status. After fix, `python3 -m unittest discover -s tests/CI -v`: PASS, 12 tests, including empty stderr and safe JSON assertions. `python3 -m py_compile scripts/ci/revision.py tests/CI/test_revision.py` and `git diff --check`: PASS. Broader prerequisites/failures listed above unchanged; broader suites not rerun for this CLI-only correction. Commit subject `fix(ci): sanitize revision CLI argument errors`; resolve hash from history. No remote actions.
