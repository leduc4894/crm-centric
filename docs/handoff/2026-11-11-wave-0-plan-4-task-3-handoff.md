# Wave 0 Plan 4 Task 3 handoff

## Objective and authority
Implement fail-closed CI report parsing, complete five-job aggregation and reviewed
coverage inventories under the parent's explicit Task 3 delegation. Work stayed
on `feat/wave-0-plan-4-ci`; no worktree, subagent or remote repository/CI/settings action. Read AGENTS.md,
the Task 3 brief, Plan 4 spec reporting/coverage sections and Task 1 handoff before
implementation. Wave 0/P0 acceptance remains OPEN.

References: [spec](../superpowers/specs/2026-11-11-wave-0-plan-4-ci-wave-exit-design.md),
[plan](../superpowers/plans/2026-11-11-wave-0-plan-4-ci-wave-exit.md),
[report format and trust requirements](../../tools/ci/README.md).

## Completed changes
- Immutable RunIdentity and CiSummary; bounded JSON with duplicate-key rejection,
  exact run/attempt/revision binding, strict suite inventory, reviewed minimum and
  required identity checks, no real-suite skips, no zero discovery. Native TRX,
  Vitest and Playwright reports can be embedded in the run-bound envelope; raw
  errors are discarded and global native failures cannot hide behind pass counts.
- Aggregator requires exactly backend, compose, contracts-sdk, web-browser and
  security. Every platform result must independently be success; missing,
  duplicate, failed, skipped, cancelled or stale evidence fails. CLI emits only
  safe aggregate JSON and nonzero on failure. No assertion that an unscheduled
  aggregator or a cancelled workflow produced evidence.
- Historical minima retained: Contract87, Local20, non-Compose70, Compose14,
  Vitest54 and browser4. Named Python identities inventoried from source; actual
  pinned Node and .NET runtime discovery inventories recorded. Candidate output
  never updates the policy. Independent baseline review remains pending.
- Plan 3 simulation now checks named required identities and named prerequisite
  skip reasons; additional passing tests are allowed. The simulation exception
  never applies to accepted real-suite evidence.
- Focused tests include real subprocess discovery of a deliberately failing
  `tests/Local` fixture and non-success aggregation, native malformed/partial/
  skipped/global-error cases, stale/missing/duplicate evidence, and CLI safety.

## Trust and remaining work
The workflow must execute this parser and load coverage.json from an immutable
trusted checkout. Running candidate-modifiable parser/policy cannot prevent a
candidate lowering its baseline. Task 2 independent trust provisioning remains
blocked and is not solved or claimed by this task. Workflow wiring with explicit
needs and always(), normalized unittest production, remote cancellation cases,
security scanner identity inventory and independent policy review remain later
tasks. Security minimum1 is explicitly a placeholder, not proof of scanner
coverage. .NET discovery used existing Release binaries on the pinned SDK image;
fresh-build discovery and application/backend/browser execution remain workflow
verification. No API contract or database migration; rollback is task revert.

## Verification and changed files
Detailed commands and RED/GREEN results are in the ignored local task report
`.superpowers/sdd/2026-11-11-wave-0-plan-4-ci-wave-exit/task-3-report.md`.
Changed: scripts/ci/{summary,aggregate}.py, tests/CI/{test_summary,test_aggregate}.py,
tools/ci/{coverage.json,README.md}, both Plan 3 reporting harnesses, this handoff.
Commit subject: `feat(ci): aggregate complete test evidence fail closed`; resolve
its SHA from local history. No unrelated tracked work was changed.

- Focused summary tests: PASS, 8 tests; aggregate tests: PASS, 5 tests.
- Local Python: PASS, 20 tests.
- Actual Vitest run: PASS, 54 tests; native parser verifies all required identities.
- Plan 3 ci-environment simulation: PASS, 87 total/55 named skips/32 passed.
- Full Plan 3 aggregate-reporting harness: PASS, 16/16 cases.
- Pinned Node discovery: PASS, 54 Vitest/4 browser identities. Pinned .NET image
  list-tests: PASS, 70 non-Compose/14 Compose identities (existing Release build).
- Full CI suite: PASS, 33 tests, zero skips (loaded before three additional native
  regressions; final focused runs cover all additions). Full Contract: still running.
- py_compile, bash -n and git diff --check: PASS before commit.
- Remote CI, application integration execution, real browser stack, DemoModule,
  duplicate/tenant/outage/recovery/restore drills: NOT RUN for Task 3.

## Active verification handoff authorized by parent
Parent requested committing for review without blocking later tasks on the long
Contract run. Full pinned Contract suite remains RUNNING, not passed: 32 completed
passes, 0 skips and 0 failure/error markers at handoff; active
`test_compatibility.CompatibilityTests.test_transitive_schema_ref_change_requires_review_and_hashes`.
Elapsed 12m34s. Output `/tmp/task3-contract.out`; exec session `78926` for parent
write_stdin follow-up. Record the eventual final count/exit status before any
full-contract completion claim. Task implementation and focused/harness checks
are complete locally; trusted policy review/provisioning and other acceptance
items remain open as stated above. No unrelated uncommitted work at staging.

## Review fix round 1
Reproduced both Important findings with new regressions: Playwright contradictory
native stats/status accepted, and oversized integer elapsed_seconds escaping as
OverflowError/CLI traceback. RED: summary10 tests had 11 failing Playwright
subcases + one error; aggregate6 tests had one CLI failure. Parser now requires
nonnegative integer native stats, exact total/outcome reconciliation and matching
case statuses; skips/flaky/missing stats fail. Numeric overflow becomes safe
ReportError. GREEN: summary10 and aggregate6 PASS; py_compile and diff check PASS.
README documents stricter native Playwright requirements. Broader suites not
repeated for this parser-only review change. Commit subject:
`fix(ci): reject contradictory browser evidence and numeric overflow`.
Trust provisioning and other previously documented acceptance gaps remain open.
