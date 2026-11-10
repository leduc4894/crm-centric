# Wave 0 Plan 2 — verification evidence

Plan 2 remains open. This record covers Tasks3–7, including container/bootstrap
prerequisites, full local Compose lifecycle, realm drift and failure/recovery.
Task8 clean-source archive startup is pending. Remote DemoModule, concurrent event
duplicates, tenant isolation and restore acceptance are not established by these
local checks.

## Toolchain and verified upstream images (2026-11-09)

The host used Linux amd64, 7,941 MiB RAM, no swap, and approximately 67 GiB
available disk after builds. Docker Engine **29.9.3**, Compose **v5.7.0** and
host .NET SDK **10.0.403** were available. Task3 used runtime containers directly;
Tasks6–7 below verify the full stack with Compose **v5.7.0**. This is the tested
version, not a claim that older Compose releases have passed the full stack.
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

### Task 4 review fix round 1 — ownership drift

Review of `19cb472` identified that relation ownership checks only covered
`platform`, and existing managed schema/history ownership was not required to
match `crm_migrator`. Five additional warm-state cases reproduced incorrect
acceptance: application-owned public/custom-schema tables, unrelated platform
schema/history owners, and an unexpected admin-owned platform relation.

The validator now rejects app relation ownership across user schemas and requires
known migrator ownership for existing platform schema/relations. Pre-migration
absence and authorized migrator ownership stay valid. Tests verify safe rejection
without ownership/data repair and retained sentinel/history data.

- RED:
  `dotnet test tests/Integration/Integration.csproj -c Release --no-restore --filter FullyQualifiedName~Warm_bootstrap_rejects_relation_or_managed_schema_ownership_drift_without_repair`:
  **5 failed, 0 passed/skipped**, 1m01s (incorrect exit 0 instead of mismatch).
- `dotnet build CrmPlatform.sln -c Release --no-restore`: **PASS**, zero
  warnings/errors, 5.60s.
- GREEN:
  `dotnet test tests/Integration/Integration.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~LocalBootstrapTests|FullyQualifiedName~RolePrivilegeTests|FullyQualifiedName~MigrationTests'`:
  **19 passed, 0 failed/skipped**, 1m47s. This includes all 13 bootstrap cases
  plus existing migration/role regressions. `git diff --check`: **PASS**.

The full image suite was not repeated for this SQL-only fix under the controller's
verification scope. The prior 60-test full suite is evidence for `19cb472`.
No CLI, secrets, adapters, packages, contract or migration changes. Current branch
remains the Plan 2 feature branch; separate fix commit subject
`fix(local): reject bootstrap ownership drift across user schemas`.
Repeat independent review and Compose/P0 gates remain pending. Private captures:
mode 0600 `/tmp/crm-task4-review1-{red,build,green}.txt`.

## Task 5 — isolated Keycloak realm and warm metadata inspection

Task 4's ownership review fix passed independent review at `02d0962`. Task 5
adds the `crm-local` startup import, two distinct public Authorization Code/S256
browser clients, canonical localhost issuer, and a read-only trusted-host realm
inspector. Image digests are unchanged. No CRM authentication, login callback,
end-user token issuance, production deployment, public API/schema or dependency
changes are included.

The [IdP guide](../operations/05-IDP-CONFIGURATION.md) contains exact Task 6
file/environment/argv contracts and production design boundaries. It supersedes
the earlier eight-file/five-password intermediate state contract: state now has
**nine files/six independent passwords**, including separate
`idp_db_admin_password`. Earlier intermediate state fails safely without rotation.
Keycloak receives only its own database-login and bootstrap-admin password files.
The dedicated non-superuser `keycloak` PostgreSQL login owns its isolated database
and public schema; SQL/password errors are suppressed during private initialization.
The older runtime health test now also uses a dedicated non-superuser login.

Actual Task 5 RED/intermediate results:

- `dotnet test tests/Integration/Integration.csproj -c Release --no-restore --filter FullyQualifiedName~KeycloakContainerTests`:
  initial **3 failed, 0 passed/skipped**, 2m30s. Real Keycloak management readiness
  succeeded; absent `crm-local` discovery/admin metadata returned 404.
- `--filter FullyQualifiedName~Idp_database_initializer`: **1 failed**, 4s;
  real PostgreSQL did not have the required login (expected true, actual null).
- `--filter FullyQualifiedName~Nonlocal_hostname`: **1 failed**, 1s;
  the old adapter accepted a nonlocal canonical hostname and exited 0.
- Filesystem RED: **1 failed/6 passed**, expected nine files, actual eight.
- First implementation runtime run: **3 failed/1 passed**, 4m51s; DB initializer
  passed, but source-directory permissions made the copied import directory
  unreadable. The image now explicitly grants directory traversal.
- Next runtime run reached public discovery/JWKS/client/database checks, then
  caught completed-response handling in the Python inspector. A real socket
  regression reproduced **1 failed/2 passed** (expected drift, actual unavailable).
  The bounded read loop now stops when the HTTP response closes.
- Another real socket regression reproduced malformed HTTP escaping the safe
  status boundary: **1 failed/1 passed**. HTTP protocol errors now map to
  `unavailable` with `LOCAL_IDP_NOT_READY` and a fresh correlation ID.

Final runtime/regression evidence is recorded below. All commands
use SDK PATH/DOTNET_ROOT `/tmp/crm-dotnet-10.0.403`; captures remain private
mode0600 `/tmp/crm-task5-*`. No raw response, password, token or key material is
reproduced here. Compose/clean-source/outage and Wave 0/P0 acceptance remain open.

Task 5 final focused GREEN:
`dotnet test tests/Integration/Integration.csproj -c Release --no-restore --filter FullyQualifiedName~KeycloakContainerTests`:
**5 passed, 0 failed/skipped**, 3m04s. Actual public discovery/JWKS, persisted
Admin API client flags/redirects/origins/no-secret, no seeded realm users,
restricted authenticated PostgreSQL owner/schema, unchanged warm restart,
intentional persisted grant drift, unavailable stopped provider, port override
and safe nonlocal-host rejection all passed. Helper statuses also assert exact
reason mappings and correlation length. Runtime captures inspect/log-scan all
three independent secret sentinels without printing raw data.

Final Python local suite: **10 passed**, 1.156s; contract suite: **32 passed**,
3.376s. Locked restore **PASS**. Python compilation, all entrypoint Bash syntax
and `git diff --check`: **PASS**.

Final Task 5 solution build: **PASS**, zero warnings/errors, 2.84s. Final regression:
`dotnet test CrmPlatform.sln -c Release --no-build --no-restore --filter 'Category!=Compose'`:
**70 passed, 0 failed/skipped**, 7 minutes. This includes all previous behavior,
actual built-image context/history/layer/secret audits, the dedicated-role old
runtime fixture and all five Keycloak cases. Captures:
`/tmp/crm-task5-{build-final,regression,focused,local-final,contracts}.txt` (0600).
No unresolved test failure or skipped prerequisite. Final self-review also made
owned fixture cleanup remove its anonymous PostgreSQL volumes; no global prune
was performed. The implementation is ready for independent review. Full Compose,
clean-source export, outage/recovery and Wave 0/P0 acceptance remain open.

### Task 5 review fix round 1 — absolute HTTP deadline

Review of `44e1e7e` found that urllib's timeout was a socket inactivity timeout
while reading HTTP status/headers. A peer could keep sending header bytes within
that interval and exceed the promised total inspection deadline indefinitely.
Real socket regression with a 0.2s budget reproduced status **0.726s** and headers
**0.676s**; body reads were already bounded. Focused RED:
`.venv/bin/python -m unittest discover -s tests/Local -p test_realm.py -v`:
**two failing subcases across four test methods**, 1.740s.

The synchronous transport now uses standard-library HTTP parsing with a private
per-connection socket reader that applies the remaining absolute budget to every
raw read, including status/header parsing, and to connect/send operations. It
connects directly to the local profile's numeric IPv4 loopback address and keeps
the canonical localhost Host header. It uses no proxy, redirect following,
background request worker, global monkeypatch or signal handler. Response/socket
ownership is closed synchronously on every exit. JSON/result/secret interfaces,
metadata checks and all infrastructure configuration are unchanged.

An intermediate implementation failed four assertions because HTTPConnection is
not a context manager; explicit `contextlib.closing` corrected cleanup. The
blocked test peer was terminated with its test process; this failed attempt is
not GREEN. Final focused Python: **4 passed**, 0.664s. Final all-local command:
`.venv/bin/python -m unittest discover -s tests/Local -v`: **11 passed**, 1.830s,
including trickled status/header/body, prompt peer EOF after helper return,
malformed HTTP, complete body and existing secret state checks.

Actual Keycloak focused result and final syntax/diff result are below. The
previous 70-test full non-Compose run is evidence for `44e1e7e`; the full suite
was not repeated for this Python-only transport fix under the controller's
verification scope. Captures remain mode0600 `/tmp/crm-task5-review1-*`.

- `PATH=/tmp/crm-dotnet-10.0.403:$PATH DOTNET_ROOT=/tmp/crm-dotnet-10.0.403 dotnet test tests/Integration/Integration.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~KeycloakContainerTests`:
  **5 passed, 0 failed/skipped**, 3m05s. Real public/admin metadata, grant drift,
  warm restart, overridden canonical port and isolated DB identity remain valid.
- Final `python3 -m py_compile scripts/local/realm.py tests/Local/test_realm.py`
  and `git diff --check`: **PASS**. No unresolved failure. Separate fix commit
  subject: `fix(local): enforce absolute realm inspection deadline`.

### Task 6 — six-service Compose lifecycle

The public SDK-free `dev-up`, `dev-down` and `dev-smoke` commands now use the real
pinned six-service stack. CRM and IdP have separate PostgreSQL volumes/networks;
only API/Keycloak loopback ports are published. Up builds current source, removes
the previous API and both one-shots, waits real DB health, explicitly recreates
bootstrap/migration, then verifies API live/ready, container-local health and
actual persisted IdP metadata. Developer down preserves data/secrets/realm with
20s stop grace and a finite outer deadline. Smoke validates project/checkout,
secret mount sources, exact port bindings and container-local probes before
accepting host endpoints. Shared nonblocking OS locks serialize every action by
local Docker endpoint/project across checkout/state paths.

A separate owner-only `<state_dir>.stack.json` records public project/checkout/
state identity, ports, secret filenames and pending/ready provisioning, surviving
down. The Task4 secret manifest and function signature remain unchanged. Existing
volumes require matching lifecycle metadata; metadata is atomically published,
never replaces foreign/symlink state, and never substitutes for actual DB marker/
ownership validation. No automatic reset, rotation or data migration was added.

Initial real CLI RED was **4 failed/0 passed/skipped** against a compilable
unimplemented baseline,652ms. First runtime attempt was **3 passed/1 failed**,5m14s:
port preflight misclassified recently closed Docker listeners' TCP TIME_WAIT as
an active conflict. A real socket RED reproduced it; reusable-address bind now
allows restart while a live listener remains rejected. Separate down/up rerun
passed **1/1**,2m26s. Smoke state-creation, metadata persistence/FIFO and symlink
configuration error regressions also had behavioral RED/GREEN. One intervening
focused run (**3 passed/1 failed**,5m11s) was superseded while the metadata contract
was added: its old cold-up process lacked metadata and later smoke correctly
rejected it. None of those failed attempts is counted as final GREEN.

Fresh focused command after metadata implementation:
`dotnet test tests/Integration/Integration.csproj -c Release --no-restore --filter 'Category=Compose&FullyQualifiedName~LocalStackTests'`:
**4 passed,0 failed/skipped**,5m15s. It verifies SDK-free PATH, real successful
one-shots and health, warm container recreation, exactly one history row,
admin-only sentinel preservation, secret/realm/volume survival, actual app DDL/
history DML denial and secret/network/storage boundaries. Fixture cleanup checks
only random test-project labels and exact image tags, including exited one-shots,
and deletes its owned secret directory plus sibling metadata. Developer lifecycle
has no destructive test cleanup.

Final harness: .NET SDK10.0.403 at `/tmp/crm-dotnet-10.0.403`; Python3.12.3;
Docker29.9.3/Compose5.7.0; uid/gid1000; 7941MiB RAM, no swap. All upstream pins are
unchanged from Tasks3/5. Harness SDK PATH/DOTNET_ROOT is removed for commands under
test. Captures `/tmp/crm-task6-*.txt` are owner-only and are not reproduced here.
Build and startup durations are emitted separately by the CLI; default build900s,
startup180s, finite positive overrides <=3600s. No credential/token/raw capture is
included in this record.

- Locked solution restore: PASS.
- Final solution build: PASS,0 warnings/errors,4.52s.
- Python local suite: **19 passed**,4.661s.
- Contract suite: **32 passed**,1.940s.
- Python compilation, all entrypoint/wrapper Bash syntax: PASS.
- Resolved Compose configuration: PASS,six services/two networks/two volumes,
  only loopback application listeners; separate from real runtime evidence.
- `dotnet test CrmPlatform.sln -c Release --no-build --no-restore --filter 'Category=Compose'`:
  **4 passed,0 failed/skipped**,7m14s,exit0. Full real runtime; no static substitute.
- `dotnet test CrmPlatform.sln -c Release --no-build --no-restore --filter 'Category!=Compose'`:
  **70 passed,0 failed/skipped**,8m28s,exit0. No unresolved failure or skipped prerequisite.
- Final Python compilation, all Bash syntax and `git diff --check`: PASS.

Task7 failure/leak/outage/concurrency gates and Task8 clean-source export remain
planned. No remote DemoModule E2E, tenant isolation, production login, restore
readiness or Wave0/P0 completion is claimed.

### Task6 review fix round1 — exact credential grants

Review of `007883b` found that source-directory-only ownership checks accepted an
API bind to a higher-privilege credential file from the same directory. `_owned`
now requires every service's exact source filename, `/run/secrets` destination,
bind type and readonly flag, rejecting missing/extra/duplicate or reassigned
credentials. All six documented service maps are covered; data volumes remain
allowed. No secret/metadata compatibility or public contract change.

Focused Python RED: **14 failing subcases in1 new method;8 existing methods passed**,
3.241s. Focused GREEN **9 passed**,3.999s; all Local **20 passed**,5.139s.
Command: `python3 -m unittest discover -s tests/Local -v`.
The real SQL/secret boundary Compose test also replaces only its random owned API
with a healthy API using the same-directory migrator connection, then asserts
smoke fails with `LOCAL_SECRET_STATE_MISMATCH` and correlation. Actual runtime
command `dotnet test tests/Integration/Integration.csproj -c Release --no-restore --filter 'Category=Compose&FullyQualifiedName~Sql_and_secret_mount_boundaries_hold'`:
**1 passed,0 failed/skipped**,1m25s,exit0. Actual healthy privilege substitution
was rejected; all valid service mounts passed. Python compilation/diff checks PASS;
fixture cleanup left no running containers. Baseline full70/4 suites remain evidence
for `007883b`; the narrow fix uses the controller-approved focused scope.
Captures `/tmp/crm-task6-review1-*.txt` are0600; no raw output/credential is reproduced.

### Task 7 — failure, isolation and secret gates (2026-11-10)

Task 7 adds nine real Compose failure tests and one multi-stage runtime secret
check, with test-fixture helpers only. Product source, six-service configuration,
public contracts, migration schema and dependency pins are unchanged. Tests stop
actual dependencies, reject unsafe warm bootstrap and a PostgreSQL-rejected
migration, recover interrupted provisioning, reject missing/changed credentials,
and detect persisted Keycloak client drift without resetting it. Same API ID is
required through CRM/IdP outages. Foreign healthy HTTP endpoints cannot satisfy
smoke and survive rejection. Two actual lifecycle commands contend for the shared
project lock across state paths/copied checkout; killing the first releases it,
and retry retains fixture data/history/secrets. Two simultaneous projects have
disjoint containers/networks/mounts and data. Shutdown is checked with its owned
API process unable to respond.

Secret checks capture all services and CLI/config diagnostics in memory, requiring
safe events while excluding generated passwords/connections and query/header/
bearer/cookie sentinels. They exercise success, actual bad API authentication,
bootstrap, migration and unreadable IdP startup credential failures. Container and
image config/history plus actual published API/migrator/PostgreSQL/Keycloak layer
exports are scanned; gzip blobs are decompressed. Exports use a private temporary
directory and are deleted. No raw secret capture appears in assertion messages.

Test-first baseline was **8 passed/1 failed/0 skipped,total9**,12m46s. IdP recovery
had assumed immediate full realm readiness after database management health. A
safe tagged rerun reproduced that exact phase; bounded actual full-smoke polling
then passed **1/1**,1m35s. The first secret/stronger-lock run failed both fixture
assumptions: generic Docker scalar JSON broke the diagnostic formatter; after CLI
kill, one exact remaining prerequisite stage was assumed. Object/string guards
fixed the formatter. Subsequent secret runtime/layer audit passed in4m26s; stronger
lock synchronization revealed that warm DB container recreation disconnected its
session blocker. A safe diagnostic rerun confirmed both facts. The final lock
fixture holds actual dev-up in the normal bounded stop of an owned SIGSTOP API,
then verifies busy rejection, process-kill release and data/history/credential
retention. Focused proof passed **1/1**,1m37s. These are test-harness corrections;
no fabricated product RED/GREEN or production defect fix is claimed.

Source frozen after those focused passes. Harness SDK10.0.403 remains under
`/tmp/crm-dotnet-10.0.403`; developer commands strip it. Docker29.9.3/Compose5.7.0,
~8GiB RAM; one project at a time except the intentional parallel-project case.
Image/build caches reused, not a fresh-pull claim. Commands and exact chronological
attempts are in the local Task7 report; captures `/tmp/crm-task7-*.txt` are0600.

- `dotnet restore CrmPlatform.sln --locked-mode`: PASS.
- `dotnet build CrmPlatform.sln -c Release --no-restore`: PASS,0 warnings/errors,1.59s.
- `.venv/bin/python -m unittest discover -s tests/Contract -v`: **32 passed**,1.802s.
- `.venv/bin/python -m unittest discover -s tests/Local -v`: **20 passed**,5.023s.
- Python compilation, all local Bash entrypoint syntax and whitespace checks: PASS.
- `dotnet test CrmPlatform.sln -c Release --no-build --no-restore --filter 'Category=Compose' --logger 'console;verbosity=normal'`:
  **14 passed,0 failed/skipped,total14**,23.3303min,exit0.
- `dotnet test CrmPlatform.sln -c Release --no-build --no-restore --filter 'Category!=Compose' --logger 'console;verbosity=normal'`:
  **70 passed,0 failed/skipped,total70**,7.0451min,exit0.
- Final documentation whitespace check: PASS; frozen implementation unchanged.

Task7 implementation and mandatory gates are complete, ready for independent
review. The final commit's three source blobs must match the frozen SHA256 values
in the [2026-11-10 handoff](../handoff/2026-11-10-wave-0-plan-2-implementation-handoff.md).
These fresh gates may be reused for Task8 only when that source is identical;
the exact-SHA clean archive startup/smoke/down remains mandatory.

Task8 clean-source export and remote DemoModule/tenant/restore acceptance remain
open. This evidence does not claim production authentication or Wave0/P0 completion.
