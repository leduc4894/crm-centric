# Wave 0 Plan 2 — implementation handoff

## Objective and authorization

Implement the approved [Plan 2](../superpowers/plans/2026-11-09-wave-0-plan-2-local-stack-observability.md)
and [design](../superpowers/specs/2026-11-09-wave-0-plan-2-local-stack-observability-design.md)
in sequential SDD tasks. The user requested full Plan 2 SDD execution on a new
branch, without a worktree. Current branch is
`feat/wave-0-plan-2-local-stack-observability`; no worktree, push, merge or deploy
was performed. This record covers Task 1 only, pending independent review.

## Task 1 — structured diagnostics

Implemented built-in .NET JSON console logs with UTC timestamps and scopes;
application errors contain service/event/reason/correlation and real Activity
trace/span IDs. HTTP Problem and header contracts are unchanged. Each request
gets a fresh correlation ID; .NET handles inbound W3C trace context separately.
Fallback Activities are created/disposed only when Activity.Current is absent.
Raw request-start/finish diagnostics are filtered, safe host/routing logs remain,
and existing EF/Npgsql filters are preserved. No owned health-success log is
emitted per probe and no exporter/collector was added.

Process diagnostics retain one root JSON result on the original stream, with
root `status` or `reasonToken`/`correlationId`, plus safe metadata. Migration
logic, schema, privileges and connection settings are unchanged. The logging
console package is pinned to the existing runtime version 10.0.14; lockfiles
were regenerated. Public OpenAPI/JSON schemas did not change.

Changed files: `Directory.Packages.props`; Infrastructure project/lockfile and
new `Observability/PlatformLogging.cs`, `Observability/ProcessDiagnostic.cs`;
API `Program.cs`, `Operations/OperationalErrors.cs`; migrator `Program.cs` and
lockfile; integration lockfile, `OperationalSecurityTests.cs` and new
`StructuredLoggingTests.cs`; [operations documentation](../operations/01-DEPLOYMENT-OBSERVABILITY.md)
and this handoff. Task report is under the ignored local SDD artifact directory:
`.superpowers/sdd/2026-11-09-wave-0-plan-2-local-stack-observability/task-1-report.md`.

## Actual verification

SDK `/tmp/crm-dotnet-10.0.403` and existing `.venv` were verified available.
The coding worker's retained sandbox blocks Docker and MSBuild IPC; controller
commands run in the same checkout with full access. This is an execution
boundary, not a failing product test, and those prerequisite failures are not RED.

- RED: `dotnet test tests/Integration/Integration.csproj -c Release --filter FullyQualifiedName~StructuredLoggingTests`:
  **5 failed, 0 passed/skipped**. Four HTTP cases report non-JSON logs; CLI reports
  missing metadata. Two xUnit2031 warnings were subsequently corrected.
- `dotnet restore CrmPlatform.sln`: **PASS**, lockfiles regenerated for the pinned
  console dependency.
- Intermediate GREEN attempt: **compile failure** from misplaced using clauses,
  corrected; following focused run **4 passed, 1 failed** because the separately
  built migrator still had its old binary. No failed run is treated as GREEN.
- `dotnet build CrmPlatform.sln -c Release --no-restore`: **PASS**.
- GREEN: `dotnet test tests/Integration/Integration.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~StructuredLoggingTests`:
  **5 passed, 0 failed/skipped**, 13 seconds, including real PostgreSQL migration.
- `git diff --check`: **PASS**.
- `dotnet restore CrmPlatform.sln --locked-mode`: **PASS**.
- Final `dotnet build CrmPlatform.sln -c Release --no-restore`: **PASS**, zero
  warnings/errors.
- `dotnet test tests/Integration/Integration.csproj -c Release --no-build --no-restore --filter 'Category!=Compose'`:
  **29 passed, 0 failed/skipped**, 1 minute 14 seconds. Private capture:
  `/tmp/crm-task1-regression.txt`.
- `.venv/bin/python -m unittest discover -s tests/Contract -v`: **32 passed**,
  2.201 seconds.

Raw test captures remain private under `/tmp/crm-task1-*-controller.txt`; only
safe diagnostic categories/results were shared. No captured raw log or credential
is included here.

## Branch, commit, blockers and next steps

Task 1 is verified on the feature branch above, based on `98710c3`.
The implementation commit subject is
`feat(observability): add structured local diagnostics and trace correlation`,
Independent spec/code review follows before Task 2. Resolve the final Task 1
SHA from that subject in branch history. No product blocker is known; the worker
sandbox is handled through controller execution.

Plan 2 Tasks 2–8 remain planned. Compose, OIDC, clean-source final Plan 2 gates,
remote DemoModule E2E, concurrent duplicates, tenant isolation and restore drills
have not been run by Task 1. Wave 0/P0 acceptance remains OPEN. Rollback is to
revert this code/dependency/documentation commit; no database migration or data
rollback is required.
