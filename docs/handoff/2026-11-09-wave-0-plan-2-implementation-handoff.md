# Wave 0 Plan 2 — implementation handoff

## Objective and authorization

Implement the approved [Plan 2](../superpowers/plans/2026-11-09-wave-0-plan-2-local-stack-observability.md)
and [design](../superpowers/specs/2026-11-09-wave-0-plan-2-local-stack-observability-design.md)
in sequential SDD tasks. The user requested full Plan 2 SDD execution on a new
branch, without a worktree. Current branch is
`feat/wave-0-plan-2-local-stack-observability`; no worktree, push, merge or deploy
was performed. This record covers Tasks 1 and 2; Task 2 awaits independent review.

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
`feat(observability): add structured local diagnostics and trace correlation`.
Task 1 passed independent review at commit `df91367`. No product blocker is
known; its worker sandbox was handled through controller execution. Task 2's
worker has direct MSBuild and loopback network access.

Plan 2 Tasks 3–8 remain planned. Compose, CRM authentication, clean-source final Plan 2 gates,
remote DemoModule E2E, concurrent duplicates, tenant isolation and restore drills
have not been run by Task 1. Wave 0/P0 acceptance remains OPEN. Rollback is to
revert this code/dependency/documentation commit; no database migration or data
rollback is required.

## Task 2 — isolated fake OIDC issuer

Implemented a test-only HTTP issuer on IPv4 loopback with an ephemeral Kestrel
port. Discovery identifies the bound issuer and JWKS URI. RS256 tokens use
runtime RSA-2048 keys and random key IDs; JWKS exposes only public n/e plus
key metadata. Options permit issuer/audience/subject and UTC issued/not-before/
expiry overrides. Rotation can retain public verification keys or immediately
retire them. Signing, rotation and public snapshots share a lock; retiring and
disposing releases RSA keys, and async disposal releases the server. Logging
providers are cleared and no request/body logging middleware or OAuth endpoints
are installed. Startup failure also disposes resources.

Changed files: `tests/Integration/Fixtures/FakeOidcIssuer.cs`,
`tests/Integration/FakeOidcIssuerTests.cs`, the test project and its lockfile,
`tests/Integration/SmokeTests.cs` (expected teardown exception only),
`Directory.Packages.props`, and this handoff. No `src` assembly references the
test assembly. JWT generation/validation uses the supported Microsoft
IdentityModel 8.x line, pinned to 8.23.0 using official
[NuGet version/support metadata](https://www.nuget.org/packages/Microsoft.IdentityModel.JsonWebTokens/8.23.0).
Only the test project gained the dependency; production lockfiles are unchanged.
Public API contracts, database schema and production authentication are unchanged.

Task 2 verification:

- RED: `/tmp/crm-dotnet-10.0.403/dotnet test tests/Integration/Integration.csproj -c Release --no-restore --filter FullyQualifiedName~FakeOidcIssuerTests`:
  **9 failed, 0 passed/skipped** against a compilable HTTP scaffold returning
  empty discovery/JWKS/tokens. Failures were missing discovery fields, empty
  keys and empty tokens; compilation succeeded without warnings.
- GREEN: same command after implementation: **9 passed, 0 failed/skipped**,
  6 seconds. Required discovery/public-key/signature/rotation/privacy tests,
  four standard invalid-token validation cases and concurrent rotation/signing/
  JWKS reads all passed.
- `/tmp/crm-dotnet-10.0.403/dotnet restore CrmPlatform.sln --locked-mode`:
  **PASS**.
- `/tmp/crm-dotnet-10.0.403/dotnet build CrmPlatform.sln -c Release --no-restore`:
  **PASS**, zero warnings/errors.
- `.venv/bin/python -m unittest discover -s tests/Contract -v`:
  **32 passed**, 1.583 seconds.
- First full non-Compose regression, invoked by absolute SDK path without
  exporting its directory on PATH: **37 passed, 1 failed, 0 skipped**, 53 seconds.
  Failure: `SmokeTests.Script_checks_real_api_and_cleans_up_on_success_or_readiness_failure(invalidApplication: False)`.
  The smoke script runs `dotnet` in a child shell and requires it on PATH;
  `command -v dotnet` confirmed it was absent in the worker shell. No source
  change was made for this environment issue; the corrected run exports the
  SDK PATH and DOTNET_ROOT for child processes. PostgreSQL tests ran directly.
- Corrected-environment full suite: **37 passed, 1 failed, 0 skipped**,
  1 minute 13 seconds. The original real-API smoke case passed. A separate
  pre-existing smoke-fixture teardown race failed
  `SmokeTests.Script_rejects_foreign_listener_and_bounds_term_ignoring_cleanup(foreignListener: True)`:
  cancellation/Stop can cause AcceptTcpClientAsync to throw InvalidOperationException
  rather than OperationCanceledException. The controller authorized a minimal
  cancellation-filtered fixture fix; no production smoke behavior is changed.
- Isolated smoke-method characterization before that fix: **2 passed, 0
  failed/skipped**, 25 seconds. The race is intermittent; the captured full-suite
  failure is its behavioral RED. The fixture now catches InvalidOperationException
  only when its stop token is canceled; other unexpected exceptions propagate.
- Final focused check after that guard:
  `PATH=/tmp/crm-dotnet-10.0.403:$PATH DOTNET_ROOT=/tmp/crm-dotnet-10.0.403 /tmp/crm-dotnet-10.0.403/dotnet test tests/Integration/Integration.csproj -c Release --no-restore --filter 'FullyQualifiedName~FakeOidcIssuerTests|FullyQualifiedName~Script_rejects_foreign_listener_and_bounds_term_ignoring_cleanup'`:
  **11 passed, 0 failed/skipped**, 26 seconds, no warnings.
- Final full non-Compose regression with the SDK PATH/DOTNET_ROOT environment:
  `/tmp/crm-dotnet-10.0.403/dotnet test CrmPlatform.sln -c Release --no-build --no-restore --filter 'Category!=Compose'`:
  **38 passed, 0 failed/skipped**, 1 minute 9 seconds; real PostgreSQL tests
  included. No full-suite failure remains unresolved.
- `git diff --check`: **PASS**.

Task report: `.superpowers/sdd/2026-11-09-wave-0-plan-2-local-stack-observability/task-2-report.md`
(ignored local artifact). Private command captures are under `/tmp/crm-task2-*`;
no raw captured logs, tokens or key material are reproduced here. Commit subject:
`test(identity): add isolated fake OIDC issuer fixtures`; resolve its SHA in
branch history after commit (parent `df91367`). The task commit includes all
listed changes; no additional intended code changes remain. No migrations
require rollback; revert the test,
dependency and documentation commit if needed. No known product blocker.

Next: independent Task 2 review, then Task 3 must inspect production publish
output to prove no fixture/test assembly is present. CRM OIDC authentication and
Wave 0/P0 acceptance are not established by this fixture.
