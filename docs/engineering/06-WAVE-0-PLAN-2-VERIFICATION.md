# Wave 0 Plan 2 — verification evidence

Plan 2 remains open. This record covers Task 3 container prerequisites and Task 4 bootstrap;
full Compose lifecycle, clean-source startup, realm metadata/drift, recovery,
remote DemoModule, concurrent duplicates, tenant isolation and restore gates are
not established by these container tests.

## Toolchain and verified upstream images (2026-11-09)

The host used Linux amd64, 7,941 MiB RAM, no swap, and approximately 67 GiB
available disk after builds. Docker Engine **29.9.3**, Compose **v5.7.0** and
host .NET SDK **10.0.403** were available. Compose itself has not yet been used
for this task: the minimum tested full-stack Compose version remains pending.
The host SDK is `/tmp/crm-dotnet-10.0.403`; export its directory on `PATH` and
set `DOTNET_ROOT` when running tests that launch `dotnet` child processes.

All upstream references in [images.lock.env](../../infra/compose/images.lock.env)
were pulled and inspected, with architecture amd64, before adapter implementation.
These are exact tags and registry manifest digests, not locally guessed hashes.

| Purpose | Official image tag | Verified digest |
| --- | --- | --- |
| Locked build SDK | `mcr.microsoft.com/dotnet/sdk:10.0.403-noble` | `sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e` |
| App/migrator runtime | `mcr.microsoft.com/dotnet/aspnet:10.0.14-noble` | `sha256:1a48394b6f29d3a32c79d9edecd4abda0ecbe8776d567dbfc4590ed6979c740c` |
| CRM/IdP database base | `postgres:18.6-bookworm` | `sha256:f1ea2a1a4fb7b68d4836be1fdcb8a6466a89b072158bf9d97663600538c294c5` |
| Keycloak base | `quay.io/keycloak/keycloak:26.8.2` | `sha256:3879c623248408de29a5303178b5816c77e8dfef3890c361f8c0bbeaa979ba3b` |

Official sources: [.NET SDK tags](https://github.com/dotnet/dotnet-docker/blob/main/README.sdk.md),
[.NET 10.0.14 release](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.14/10.0.14.md),
[PostgreSQL file-based password interface](https://github.com/docker-library/docs/blob/master/postgres/content.md),
[canonical PostgreSQL entrypoint](https://github.com/docker-library/postgres/blob/master/18/bookworm/docker-entrypoint.sh),
[Keycloak downloads](https://www.keycloak.org/downloads),
[Keycloak containers](https://www.keycloak.org/server/containers), and
[Keycloak management health](https://www.keycloak.org/server/health).
Actual pulls/inspect and runtime tests supply evidence in addition to these docs.
Initial attempts to pull nonexistent .NET `trixie-slim` tags failed; these were
prerequisite discovery failures, not behavioral RED. The verified .NET 10 base
is Ubuntu Noble. SDK execution reports `10.0.403`, matching `global.json`.

No probe image or copied native executable is used. The pinned .NET runtime
contains Bash **5.2.21** and GNU `timeout` **9.4**; Keycloak contains Bash
**5.1.8** and GNU `timeout` **8.32**. These executable versions are locked by
their parent image digests. The Keycloak image reports version **26.8.2**,
OpenJDK **21.0.13**. Probes use their image's native tools, fixed container
loopback ports, and one four-second total budget; Docker adds a five-second
healthcheck timeout. API checks both `/health/live` and `/health/ready` on
8080; Keycloak checks `/health/ready` on management port 9000.

## UID, ownership and file-secret contract

Upstream .NET images default to root and provide an optional app account
1654:1654; Keycloak defaults to 1000:0; PostgreSQL starts as root and hands off
to postgres 999:999. The platform and Keycloak targets take only public
`LOCAL_UID`/`LOCAL_GID` build arguments, default 1000:1000. Values must be
numeric, nonzero and within the supported signed 32-bit range. Builds with
root or invalid UIDs fail. The lifecycle builder must pass the actual non-root
host owner UID/GID, and state validation must ensure files belong to that owner.
A root-owned host state requires an explicitly provisioned non-root owner or a
safe failure; silently using UID 0 or broadening secret permissions is invalid.

Actual Task 3 tests run UID/GID **1000:1000**, with a host directory **0700**,
files **0600**, and per-file read-only mounts. Platform and Keycloak adapters
require non-root runtime, regular/readable/nonempty files, mode 0600 and matching
file owner. Keycloak's application tree, including data/conf, is chowned at
build time to the selected UID/GID; its runtime data directory was written by
UID 1000. No runtime chmod, root application handoff, or 0644 workaround is used.
Existing named volumes with other ownership must fail/require explicit operator
reconciliation rather than be silently reowned.

- Platform: `PLATFORM_CONNECTION_FILE`, default
  `/run/secrets/platform_connection`, supplies `ConnectionStrings__Platform`
  privately before `exec dotnet`. API/migrator select fixed assemblies. Existing
  host execution bypasses this adapter and keeps the Plan 1 environment interface.
  Supplying both a file and plaintext connection environment is rejected. All
  extra container argv are rejected, so .NET command-line configuration cannot
  replace the file. Task 4 must add a fixed migrator bootstrap-mode mapping only,
  never restore arbitrary configuration passthrough.
- PostgreSQL: `POSTGRES_PASSWORD_FILE` is mandatory. The wrapper checks the
  private file, then delegates to the canonical pinned entrypoint. Its native
  `file_env` reads as root, unsets the file setting and passes the private password
  through its canonical `gosu` handoff; the resulting postgres process is UID 999.
  This behavior already passed the baseline test, so a duplicate password reader
  was not added. `LOCAL_DATABASE_SERVICE` may select `crm-db` or `idp-db` for safe
  adapter failures. Canonical initdb/volume behavior is preserved.
- Keycloak: `KC_DB_PASSWORD_FILE` and `KC_BOOTSTRAP_ADMIN_PASSWORD_FILE`, defaults
  `/run/secrets/idp_db_password` and `/run/secrets/idp_admin_password`, supply only
  private process environment. The image fixes public `KC_DB=postgres` and health
  enablement at build and runtime: build-time selection alone did not preserve
  the PostgreSQL driver when using local `start-dev`. Runtime was verified against
  real PostgreSQL by querying the created master realm table. The canonical public
  hostname/realm/import choices remain Task 5 responsibilities.

No secret enters build ARG/ENV, Docker Config, health arguments or argv. Private
process environment and host administrator access remain the documented local
boundary. Safe adapter errors use `LOCAL_CONFIGURATION_INVALID` and a fresh
correlation ID. Platform owned log service names now match the approved shared
contract exactly: `crm-api` and `crm-migrate` (`crm-bootstrap` is Task 4).

## Task 3 RED/GREEN and verification

Commands below use the SDK PATH/DOTNET_ROOT above and private captures under
`/tmp/crm-task3-*`; no raw log, environment, history or secret capture is printed.

- Initial built API/migrator baseline: focused container/logging filter
  **9 failed, 2 passed, 0 skipped** (1m04s). Five logging cases rejected old
  service labels; the API could not consume its file; three invalid-file cases
  returned the old diagnostic. Publish/context audits were already green.
- Additional built adapter baseline: PostgreSQL and Keycloak tests **1 failed,
  1 passed, 0 skipped** (11s). Keycloak management health failed; native PostgreSQL
  file secret and privilege drop already passed.
- Boundary RED: root/invalid UID builds and PostgreSQL missing-file diagnostic
  **3 failed, 0 passed/skipped** (1s). Missing-file failure was unstructured vendor
  text; both invalid UID builds previously succeeded.
- First GREEN attempt **13 passed, 4 failed** (1m38s): stale separately built host
  migrator, API test DNS-name normalization, PostgreSQL initdb temporary-socket
  readiness race and Keycloak startup. Full solution rebuild, normalized container
  DNS hostnames and TCP-specific PostgreSQL readiness corrected harness assumptions.
- Second GREEN attempt **16 passed, 1 failed** (1m12s): the remaining Keycloak
  failure was a PostgreSQL URL using the development default driver. Explicit
  public `KC_DB=postgres` corrected that runtime configuration.
- Combined focused GREEN: `dotnet test tests/Integration/Integration.csproj -c Release --no-restore --filter 'FullyQualifiedName~ContainerRuntimeTests|FullyQualifiedName~StructuredLoggingTests'`:
  **17 passed, 0 failed/skipped** (2m04s).

- Expanded final image audit: `dotnet test tests/Integration/Integration.csproj -c Release --no-restore --filter FullyQualifiedName~ContainerRuntimeTests`:
  **12 passed, 0 failed/skipped** (2m23s), auditing all four image layers/configs/
  history and the full excluded-directory sentinel set. Cached image build was
  **4.71 seconds**; cold build/pull and full-stack startup timing remain Task 8.
- Additional argv-override RED: `--filter FullyQualifiedName~Arguments_cannot_override_file_configuration`:
  **2 failed, 0 passed/skipped** (1s). Existing passthrough let child application
  configuration consume the supplied value; the adapter now rejects extra argv.

- Argv guard GREEN: **2 passed, 0 failed/skipped** (1s).
- Final `dotnet build CrmPlatform.sln -c Release --no-restore`: **PASS**, zero
  warnings/errors, 3.20s.
- Final `dotnet test CrmPlatform.sln -c Release --no-build --no-restore --filter 'Category!=Compose'`:
  **52 passed, 0 failed/skipped**, 3m15s, including all 14 final container cases.
- Locked restore, 32 contract tests, five shell syntax checks and final
  `git diff --check`: **PASS**. No Compose cases exist yet for this task; the
  mandatory full-stack Compose gate remains open.

The [Task 3 handoff](../handoff/2026-11-09-wave-0-plan-2-implementation-handoff.md)
records the final state and concrete next steps.
Container tests use actual built targets and real PostgreSQL/Testcontainers.
They verify API-only application mounts, PID 1/runtime UID, mounted mode, database
connectivity, self-probe dependency isolation/deadline, safe invalid-file errors,
no SDK/source/test/fixture in publish, sentinel-free configs/history/layers and
an actual `COPY` audit of the Docker-filtered context. The sentinel audit excludes
`.git`, `.venv`, `.superpowers`, local state/env, tests, build outputs and local
secret configuration. Public contracts, migrations and dependency lockfiles are
unchanged. No Compose/P0/production acceptance claim follows from these results.

## Task 4 — persistent secret state and local database bootstrap

Based on approved Tasks 1–3, parent `23d7952` on
`feat/wave-0-plan-2-local-stack-observability`. No public contract, dependency
or application migration changed. Full Compose and P0 acceptance remain open.

`ensure_secret_state(state_dir: Path, project: str, existing_volumes: bool)`
returns `SecretState(directory, project, files)`; `files` maps the eight fixed
names below to absolute paths. Lifecycle callers serialize their operations and
must determine whether either project's named data volume exists before calling.
On Linux, private temporary files/directories are fsynced and atomically published
with `renameat2(RENAME_NOREPLACE)`, including refusal to replace an empty foreign
directory. The v1 manifest stores project and file SHA-256 fingerprints, never
password values. Existing state is read-only validation; missing/changed state,
foreign project/owner, broad permissions or symlinks fail with
`LOCAL_SECRET_STATE_MISMATCH`. Root-host generation fails safely. The caller must
emit its safe JSON/correlation envelope; the helper never prints values.

| State filename | Use |
| --- | --- |
| `crm_admin_password` | crm-db `POSTGRES_PASSWORD_FILE` |
| `crm_migrator_password` | bootstrap migrator credential file |
| `crm_app_password` | bootstrap application credential file |
| `idp_db_password` | idp-db and Keycloak database password file |
| `idp_admin_password` | Keycloak bootstrap administrator file |
| `crm_admin_connection` | bootstrap administrative connection, `postgres` maintenance DB |
| `crm_migrator_connection` | default migrator connection, fixed `crm` DB/`crm_migrator` role |
| `crm_app_connection` | API connection, fixed `crm` DB/`crm_app` role |

All five passwords use independent standard cryptographic randomness; directory
0700/files0600, owned by the invoking non-root user. Connections use internal
`crm-db:5432`. This is a local secret store, not a production secret manager.

The only new CLI mode is `Platform.Migrator.dll --local-bootstrap`; the default
Up/app grant path remains unchanged. Typed options map environment settings
`LocalBootstrap__AdminConnectionString`, `LocalBootstrap__DatabaseName` (`crm`),
`LocalBootstrap__ProjectId`, `LocalBootstrap__MigratorSecretFile` and
`LocalBootstrap__ApplicationSecretFile`. Runtime adapters accept only this exact
single flag for the migrator, validate mounted files, and populate the private
process environment. API accepts no additional argv or bootstrap admin setting.
Bootstrap adapter selectors and defaults:

- `LOCAL_BOOTSTRAP_ADMIN_CONNECTION_FILE=/run/secrets/crm_admin_connection`
- `LOCAL_BOOTSTRAP_MIGRATOR_SECRET_FILE=/run/secrets/crm_migrator_password`
- `LOCAL_BOOTSTRAP_APPLICATION_SECRET_FILE=/run/secrets/crm_app_password`

Task 6 must mount only these three files into `crm-bootstrap`; API mounts only
`crm_app_connection`, default migration only `crm_migrator_connection`. Initialize
crm-db with maintenance `postgres`, **not** `POSTGRES_DB=crm`: bootstrap creates
the dedicated database, and rejects an unknown existing `crm` database/roles.
Pass actual host UID/GID through Task 3 public build args; never broaden permissions.

An admin-owned private `_local.bootstrap` table in the **maintenance postgres
DB** records project/database and provisioning/completion. It is separate from
domain migrations. An advisory lock serializes bootstrap; the marker and roles
are created transactionally before database creation, so an interrupted controlled
provision can finish fixed grants. Both actual role credentials must authenticate;
role/DB ownership, role memberships, base ACLs and app schema/history restrictions
are validated. Completed state performs no ACL or password repair. Warm validation
accepts the authorized migrator-owned `platform` schema/history. Unknown state or
drift returns safe `LOCAL_SECRET_STATE_MISMATCH`; operational bootstrap failure
uses `LOCAL_BOOTSTRAP_FAILED`, configuration uses `LOCAL_CONFIGURATION_INVALID`.
Every CLI result is one JSON root with `crm-bootstrap` and correlation ID.

Passwords are escaped using fixed identifiers and standard-conforming SQL literal
rules. Before any credential-bearing SQL, the bootstrap disables session statement,
error-statement, duration and sample logging. The PostgreSQL adapter also fixes
those server settings at startup; provider errors/SQL are never emitted by the
owned CLI. Vendor operational logs remain enabled.

Actual Task 4 evidence (SDK PATH/DOTNET_ROOT as above):

- Python baseline RED: **2 failed, 1 error**, missing implementation. Initial
  filesystem GREEN **3 passed**. Supplementary interruption/actual foreign-owner
  characterization raised coverage to five passing tests.
- Python atomic-publication regression RED **1 failed, 5 passed**: ordinary rename
  could overwrite an empty foreign directory. No-replace Python GREEN: **6 passed**,
  real filesystem permissions/ownership and injected interrupted write included.
  A lifecycle-directory import regression then failed **1/7** tests because
  `secrets.py` shadowed the stdlib module; switching to standard cryptographic
  `os.urandom` resolved the collision. Final Python GREEN: **7 passed**, 1.255s.
- `dotnet test tests/Integration/Integration.csproj -c Release --no-restore --filter FullyQualifiedName~LocalBootstrapTests`:
  initial behavioral RED **6 failed, 0 passed/skipped**, 28s; old mode emitted
  `crm-migrate`. First implementation GREEN **6 passed**, 44s, real PostgreSQL.
- Real entrypoint/CLI RED **1 failed**, 5s (mode rejected); after adapter support,
  focused local/migration/role/security GREEN **19 passed**, 53s.
- Schema ACL drift regression RED **1 failed, 3 passed**, 32s: the bootstrap
  accepted app CREATE on `platform`. Corrected validation rejects it while
  retaining authorized migrator ownership. Final focused command below:
  **20 passed, 0 failed/skipped**, 58s.
  `dotnet test tests/Integration/Integration.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~LocalBootstrapTests|FullyQualifiedName~MigrationTests|FullyQualifiedName~RolePrivilegeTests|FullyQualifiedName~OperationalSecurityTests'`.
- Locked restore: **PASS**. Final solution build: **PASS**, zero warnings/errors,
  2.26s on the final repeated build. The first implementation build had four Unix platform-analysis warnings;
  explicit platform guards corrected them before final verification.
- Contract suite: **32 passed**, 2.409s. Shell syntax, Python compilation and
  `git diff --check`: **PASS**. No skipped prerequisite is counted as evidence.

Full non-Compose regression:
`dotnet test CrmPlatform.sln -c Release --no-build --no-restore --filter 'Category!=Compose'`:
**60 passed, 0 failed/skipped**, 4m04s. This includes the eight new local cases
and the rebuilt runtime-image/context/security regression. Captures
are owner-only files under `/tmp/crm-task4-*`; no raw capture or credentials are
tracked. Compose topology, cold-source startup, outage/recovery and restore drills
remain pending Tasks 6–8; these tests do not close Wave 0 or P0.
