# Wave 0 Plan 2 — implementation handoff

## Objective and authorization

Implement the approved [Plan 2](../superpowers/plans/2026-11-09-wave-0-plan-2-local-stack-observability.md)
and [design](../superpowers/specs/2026-11-09-wave-0-plan-2-local-stack-observability-design.md)
in sequential SDD tasks. The user requested full Plan 2 SDD execution on a new
branch, without a worktree. Current branch is
`feat/wave-0-plan-2-local-stack-observability`; no worktree, push, merge or deploy
was performed. This record covers Tasks 1–4; Task 4 awaits independent review.

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

## Task 3 — pinned runtime containers and file-secret adapters

Current branch remains `feat/wave-0-plan-2-local-stack-observability`, based on
approved Tasks 1/2 (`df91367`, `390ae97`). Task 3's commit subject is
`build(platform): add pinned runtime containers and secret adapters`; this
handoff is included in that commit. No worktree, push, merge or deploy was used.
Task 2 review is complete; Task 3 awaits controller review. This section
supersedes earlier next-step statements that still list Task 2 review as pending.

Added a deny-by-default Docker context, locked multi-stage platform targets
`api`/`migrator`, Keycloak/PostgreSQL runtime targets, public image lock and small
file-secret/probe scripts. No package, migration, schema or public API changes.
Existing host configuration bypasses the adapters and remains valid. Fixed the
previously missed owned-log contract labels to exact `crm-api`/`crm-migrate`,
with tests first; bootstrap's `crm-bootstrap` remains Task 4.

Exact official image digests, source links, preflight failures, runtime tools,
UID/ownership strategy, RED/GREEN attempts and limits are documented in
[Plan 2 evidence](../engineering/06-WAVE-0-PLAN-2-VERIFICATION.md). Defaults:
SDK 10.0.403-noble, ASP.NET 10.0.14-noble, PostgreSQL 18.6-bookworm,
Keycloak 26.8.2. SDK matches `global.json`. Bash and timeout are native to each
pinned image; no external native executable was copied.

Task 4/5/6 contracts and decisions:

- Platform and Keycloak `LOCAL_UID`/`LOCAL_GID` are public build args, numeric and
  nonzero, default 1000:1000. Task 6 must pass actual non-root host owner IDs;
  state files must be owned by that UID. Root-host state requires explicit
  non-root provisioning or a safe failure. Runtime checks enforce 0600 and
  owner match, with host state directory 0700. No chmod 0644 permission repair.
- API only mounts the application connection file. Migrator only mounts its
  connection file. `PLATFORM_CONNECTION_FILE` defaults to
  `/run/secrets/platform_connection`; the file is mapped to private process
  `ConnectionStrings__Platform`. Plaintext connection environment plus file is
  rejected, as are arbitrary extra container argv. **Task 4 may explicitly allow
  only its fixed migrator `--local-bootstrap` mode and map bootstrap's own admin/
  credential files; do not restore general .NET configuration argv passthrough.**
  Controller agreed this repair while Task 3 was in progress.
- PostgreSQL's wrapper validates `POSTGRES_PASSWORD_FILE`, then retains native
  official file loading, initdb and root-to-postgres 999:999 handoff. The native
  reader already handled host 0600 correctly in baseline; no duplicated reader
  was added. Use `LOCAL_DATABASE_SERVICE=crm-db|idp-db` for adapter diagnostics.
  A TCP readiness check is needed to avoid accepting initdb's temporary socket
  server as the final published listener.
- Keycloak uses `KC_DB_PASSWORD_FILE` and
  `KC_BOOTSTRAP_ADMIN_PASSWORD_FILE`; defaults are
  `/run/secrets/idp_db_password` and `/run/secrets/idp_admin_password`. It runs
  UID/GID 1000:1000 in actual tests and can write its data directory. Selected
  host IDs own the app tree at build time, including conf/data. Existing named
  volumes with mismatched ownership require safe failure/operator reconciliation.
  `KC_DB=postgres` must be fixed at runtime as well as build time: `start-dev`
  otherwise selected the wrong driver despite the build's `--db=postgres`.
  Health is enabled on management port 9000. Task 5 still owns realm/import/
  canonical hostname configuration; no login or CRM authentication was added.
- Probes only access the same container's loopback ports, total timeout 4s,
  Docker health timeout 5s. API checks live and ready on 8080; Keycloak management
  ready on 9000. No health response payload is printed.

Changed files: `.dockerignore`; three Dockerfiles under `infra/docker/`;
`infra/compose/images.lock.env`; five shell adapters/probes under
`infra/compose/entrypoints/`; `ContainerImages.cs`, `ContainerRuntimeTests.cs`,
`PostgresFixture.cs`; API/migrator owned service labels; corresponding structured/
security assertions; operations logging docs; Plan 2 evidence and this handoff.
No uncommitted work from another task was present at Task 3 start.

Actual verification so far:

- Minimal buildable API/migrator baseline RED: **9 failed, 2 passed**, no skips.
- Keycloak/PostgreSQL baseline: **1 failed, 1 passed**, no skips. Native PostgreSQL
  secret handling was already correct and honestly recorded as baseline GREEN.
- Root/invalid UID + missing PostgreSQL file RED: **3 failed**, none passed/skipped.
- Combined focused GREEN: **17 passed**, no failures/skips, 2m04s.
- Expanded `FullyQualifiedName~ContainerRuntimeTests` image/config/history/layer/
  actual build-context audit: **12 passed**, no failures/skips, 2m23s.
- Additional `Arguments_cannot_override_file_configuration` RED **2 failed**,
  then GREEN **2 passed**, no skips, 1s each; the adapter now rejects extra argv.
- `dotnet restore CrmPlatform.sln --locked-mode`: **PASS**.
- `dotnet build CrmPlatform.sln -c Release --no-restore`: **PASS**, zero warnings/
  errors; final rebuild/regression result is appended below.
- `.venv/bin/python -m unittest discover -s tests/Contract -v`: **32 passed**,
  3.041s.
- `bash -n` on each of the five new shell scripts: **PASS**.
- `git diff --check`: **PASS** before final handoff updates; repeated before commit.

Tests use real built images and PostgreSQL/Testcontainers, verify runtime/PID 1
UID, host0700/file0600/mounted0600, application-only API secrets, dependency
isolation and probe deadline, Keycloak's actual PostgreSQL master realm table,
no published SDK/source/test/fake issuer, and absent sentinel values in image
config/history/decompressed layers, filtered context and captured runtime logs.
Raw captures remain local under `/tmp/crm-task3-*`; failing capture helpers now
create mode0700 directories and mode0600 files. The context sentinel test only
removes files it created and fails safely if a developer's local target exists.

Next: independent Task 3 review, then Task 4 bootstrap/secrets and Task 5 realm
configuration, sequentially. Tasks 4–8 and full Compose/clean-export acceptance
remain planned. No known product blocker. Roll back the Task 3 commit to remove
these build/adapters and label repairs; no data/schema rollback is needed.

Task 3 final verification, after the argv guard and capture/cleanup repairs:

- Final `dotnet build CrmPlatform.sln -c Release --no-restore`: **PASS**, zero
  warnings/errors, 3.20s.
- `dotnet test CrmPlatform.sln -c Release --no-build --no-restore --filter 'Category!=Compose'`:
  **52 passed, 0 failed/skipped**, 3m15s. This includes all 14 final container
  cases, existing smoke tests and Task 1/2 regressions. SDK PATH/DOTNET_ROOT were
  exported for child processes. Capture: `/tmp/crm-task3-regression.txt`.
- Final `git diff --check`: **PASS**. All audit sentinel files were removed;
  no source secret or test capture is staged. The four image tags and test
  containers/networks were cleaned by fixture teardown.

Task 3 is ready for independent review. No remaining Task 3 implementation work
or known blocker; full Plan 2 and P0 acceptance remain open as described above.

## Task 4 — persistent secret state and isolated database bootstrap

Task 3 passed independent review at `23d7952`. This section supersedes the earlier
Task 3 pending-review statement. Task 4 implements the approved plan's secret
state/bootstrap slice on the same feature branch, no worktree/push/merge/deploy.
Commit subject: `feat(local): provision isolated database identities and persistent secret state`.

Completed implementation:

- Linux private state generation/reuse: five independent random passwords and
  three file-based CRM connections, v1 project/fingerprint manifest, atomic
  fsync/no-replace publication, actual 0700/0600/owner/symlink validation. Existing
  volume plus missing state and changed files fail without rotation or repair.
- Fixed `--local-bootstrap` migrator mode and typed Infrastructure bootstrap;
  default migration is unchanged. Private admin-owned maintenance-DB `_local`
  marker controls interrupted retries; role/password/ownership/ACL drift on
  completed state fails. No application migration was added.
- Actual authentication of migrator/app credentials; admin-owned `crm`, non-owner
  `crm_migrator` CONNECT/CREATE, app CONNECT, no PUBLIC DB/schema privileges,
  no TEMPORARY or elevated memberships. Warm validation allows authorized
  migrator-owned application schema/history, rejects app CREATE/DML/ownership.
- Explicit entrypoint file mapping, safe one-root JSON `crm-bootstrap`
  diagnostics, disabled SQL statement/error/duration/sample logging around
  password SQL and database adapter startup; no credentials in argv or logs.

Changed files: `.gitignore`; `scripts/local/secrets.py`; `tests/Local/test_secrets.py`;
`src/Platform.Infrastructure/Local/LocalDatabaseBootstrap.cs`; migrator `Program.cs`;
platform/PostgreSQL entrypoints; `Fixtures/MigratorProcess.cs`, new
`Fixtures/LocalSecretFixture.cs`, `LocalBootstrapTests.cs`; Plan 2 verification
evidence and this handoff. No dependency lock or public contract/schema changes.

Actual evidence: filesystem initial RED **2 failures/1 missing-behavior error**;
initial GREEN **3 passed**, interruption/owner characterizations **5 passed**;
no-replace publication RED **1 failure/5 passes**, then **6 passed**. A real
lifecycle-directory import regression RED **1 failure/6 passes** caught stdlib
name shadowing; standard cryptographic `os.urandom` fixed it, final Python
**7 passed**.
Real PostgreSQL bootstrap RED **6 failed**, first GREEN **6 passed**; actual
adapter RED **1 failed**; initial expanded GREEN **19 passed**. Added schema-ACL
negative case RED **1 failed/3 passed**, final expanded local/migration/role/
security GREEN **20 passed**, 58s. Locked restore/build **PASS** (final zero
warnings/errors); contracts **32 passed**; Bash syntax/Python compilation/diff
checks **PASS**. Full non-Compose regression
`dotnet test CrmPlatform.sln -c Release --no-build --no-restore --filter 'Category!=Compose'`:
**60 passed, 0 failed/skipped**, 4m04s (actual built-image audits included). Complete
commands and actual intermediate outcomes are in
[verification evidence](../engineering/06-WAVE-0-PLAN-2-VERIFICATION.md).

Task 6 integration contract: `SecretState.files` exposes eight exact filenames
listed in the evidence. Set crm-db initial database to maintenance **postgres**,
not `crm`; use `POSTGRES_PASSWORD_FILE` for `crm_admin_password`. Only bootstrap
mounts `crm_admin_connection`, `crm_migrator_password`, `crm_app_password`;
selectors/default mount paths are documented in evidence. Set public
`LocalBootstrap__DatabaseName=crm` and project ID. Bootstrap mode's maintenance
marker is `_local.bootstrap`; roles are fixed `crm_migrator`/`crm_app`.
Migration/API retain `PLATFORM_CONNECTION_FILE` and app-only API secret mounts.
Use host non-root UID/GID build args, retain 0700/0600. Secret generation requires
Linux renameat2 and lifecycle serialization; unsupported/root-host state fails.

No known product blocker. Task 4 awaits independent review; Tasks 5–8 remain
planned, and full Compose/P0 acceptance is open. Do not treat the PostgreSQL-only
or built-image regression as Compose acceptance. Rollback: revert the task code;
retain volumes and secret state. Existing managed roles/DB/technical marker are
preserved; no automatic drop/password rotation or destructive reset is provided.
Report: `.superpowers/sdd/2026-11-09-wave-0-plan-2-local-stack-observability/task-4-report.md`.

### Task 4 review fix round 1 — ownership boundary

Review of `19cb472` found an ownership validation gap: relation ownership was
checked only within `platform`, and existing managed schema/history ownership was
not required to match `crm_migrator`. The finding was reproduced with five real
PostgreSQL warm-state cases: app-owned public/custom-schema tables, an unrelated
platform schema owner, unrelated history owner and an admin-owned unexpected
platform relation. All five were incorrectly accepted before the fix.

Completed fix: reject app-owned relations throughout user schemas; require
existing `platform` schema and relations (including history) to be migrator-owned.
The pre-migration no-schema state and valid migrator-owned schema/history remain
accepted. Validation does not transfer ownership or repair data; regression tests
verify rejected owners, sentinel data and migration history remain unchanged.
No CLI, secret, adapter, package, contract or migration changes. Changed files:
`LocalDatabaseBootstrap.cs`, `LocalBootstrapTests.cs`, this handoff and verification
evidence; ignored Task 4 report was updated.

Actual verification (SDK PATH/DOTNET_ROOT exported):

- RED ownership-regression filter: **5 failed, 0 passed/skipped**, 1m01s;
  every case returned exit 0 instead of safe mismatch exit 1.
- `dotnet build CrmPlatform.sln -c Release --no-restore`: **PASS**, zero
  warnings/errors, 5.60s.
- `dotnet test tests/Integration/Integration.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~LocalBootstrapTests|FullyQualifiedName~RolePrivilegeTests|FullyQualifiedName~MigrationTests'`:
  **19 passed, 0 failed/skipped**, 1m47s (13 bootstrap, five migration, one role case).
- `git diff --check`: **PASS**. The prior full 60-test suite applies to
  `19cb472`; it was not rerun for this SQL-only review fix, per the controller's
  verification scope. Compose/P0 acceptance remains open.

Branch remains `feat/wave-0-plan-2-local-stack-observability`, base `19cb472`.
Separate fix commit subject:
`fix(local): reject bootstrap ownership drift across user schemas`.
No other intended/uncommitted work or known blocker; ready for repeat independent
review. No push/merge/deploy. Next: rerun review, then resume sequential Tasks 5–8.
