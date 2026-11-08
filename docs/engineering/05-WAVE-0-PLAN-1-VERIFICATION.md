# Wave 0 Plan 1 — backend foundation verification

2026-11-08; local Ubuntu 24.04 linux-x64. Tasks 1–5 have clean task reviews;
Task 6 final controller review is pending. This evidence covers only Plan 1.
Wave 0/P0, remote CI, OIDC, tenant isolation, business runtime, concurrent event
duplicates, remote DemoModule E2E and restore/DR acceptance remain OPEN.

## Reproducible versions and sources

- SDK **10.0.403**, runtime/ASP.NET Core **10.0.14**; global.json disables
  roll-forward/prerelease. Installed under `/tmp/crm-dotnet-10.0.403`.
  [Microsoft release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)
  and [support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
  were verified in Task 1. SDK archive SHA-512:
  `b7b3d8461cf98b07d2552ce9199702fa3ad010ac2671a5f8cd5136f7534e50f6855ba7bbb02b11494563f1d04156276a31cda1103b900e5eecd0d9473dc7b9c5`.
- EF Core/Relational/Design, MVC Testing and dotnet-ef **10.0.14**;
  Npgsql EF provider **10.0.3**; Test SDK **18.11.0**; xUnit **2.9.3**;
  VS runner **4.0.1**; Testcontainers.PostgreSql **4.16.0**.
  Direct pins: Directory.Packages.props; four committed packages.lock.json files
  pin transitive dependencies/content hashes. Task 1 checked stable versions at
  the [official NuGet index](https://api.nuget.org/v3/index.json).
- Docker client/server **29.9.3**, PostgreSQL **18.6**:
  `postgres:18.6-bookworm@sha256:f1ea2a1a4fb7b68d4836be1fdcb8a6466a89b072158bf9d97663600538c294c5`.
  Task 1 verified pull/digest against
  [official image metadata](https://github.com/docker-library/official-images/blob/master/library/postgres)
  and [PostgreSQL support policy](https://www.postgresql.org/support/versioning/).
  Tests use real isolated containers, never SQLite or skipped Docker tests.
- Python **3.12.3**, existing `.venv`, dependencies installed from unchanged
  requirements-contracts.lock. Local results do not imply remote CI pass.

## Final commands and source

Run at repository root; export DOTNET_ROOT and prepend its SDK directory to PATH.

```bash
dotnet restore CrmPlatform.sln --locked-mode
dotnet build CrmPlatform.sln -c Release --no-restore
dotnet test CrmPlatform.sln -c Release --no-build --no-restore
.venv/bin/python -m unittest discover -s tests/Contract -v
bash -n scripts/backend-smoke.sh
git diff --check
```

Tested source: `bd16017ce6bebee98601c112037d61b03bd71572` (candidate based on `2f889bd`).
The following evidence commit changes documentation only; it is not the tested
source SHA. Working-checkout
verification: locked restore passed; Release build zero warnings/errors;
integration 22 passed, zero failed/skipped; contracts 32 passed. No business
contract files or dependency pins changed in Task 6.

## Coverage and RED/GREEN evidence

| ID | Executable evidence | Result |
|---|---|---|
| W0P1-01 | Locked restore and Release build above; clean tracked-source export below | Working checkout and clean export PASS |
| W0P1-02 | MigrationTests: empty DB Up, exact schema/history, second Up | PASS |
| W0P1-03 | HealthTests, Ready_uses_application_identity; SmokeTests real API process | PASS |
| W0P1-04 | Unmigrated_database_is_not_ready_and_is_not_modified | live 200, ready 503, no startup DDL: PASS |
| W0P1-05 | Database_outage_recovers_without_api_restart; blocked-history timeout and caller cancellation tests | outage 503/live 200, recovery 200, bounded resource release: PASS |
| W0P1-06 | RolePrivilegeTests | actual CREATE/schema/history mutations denied, ownership/flags checked: PASS |
| W0P1-07 | Down_then_up_preserves_unowned_objects | disposable Down→Up, exact history and sentinel preserved: PASS |
| W0P1-08 | Migration failure tests and six OperationalSecurityTests cases | missing/malformed config, wrong auth, DDL failure, unexpected HTTP error, safe reason/correlation/logs: PASS |
| W0P1-09 | Missing_or_future_migration_is_not_ready; runtime Problem/header/media assertions and Python operational schema tests | empty/future history 503; schema and response checks: PASS |
| W0P1-10 | Python unittest suite | 32/32 PASS, business baseline unchanged |

Historical TDD evidence is summarized rather than repeated: Task 1 operational
contract RED 3 failures/29 pass and liveness RED 404, then GREEN 32/32 + 1/1.
Task 2 runner/privilege RED 3 failures (stub exit 1), then GREEN 3/3.
Task 3 regression tests were initially GREEN 5/5; controller accepted this because
Task 2 already implemented the safe behavior; no fabricated RED/production fix.
Task 4 seven readiness cases failed behaviorally before implementation, then seven
cases GREEN (including theory inputs). Task 5 six security cases RED, then GREEN;
full integration suite at `2f889bd` was 20/20.

Task 6 first focused command
`dotnet test tests/Integration/Integration.csproj -c Release --filter FullyQualifiedName~SmokeTests`
failed 1/1 because scripts/backend-smoke.sh was absent (nonzero script result).
This is missing-deliverable evidence, **not behavioral RED**. After implementation,
real PostgreSQL/process smoke passed 1/1. Final test expands to two cases:
valid app identity yields exact 200 live/ready; wrong app password fails readiness
with safe reason/correlation and no success marker. Both assert listener cleanup.
All credentials are generated in memory and passed only through process env;
captured process streams are not printed on assertion failures.

## Provisioning, retry and rollback boundary

[README](../../README.md#backend-foundation-linux) provides prerequisites and env
key names. Administrator owns a **new dedicated platform database**, revokes
PUBLIC database/public-schema privileges in that database only, and provisions
separate non-superuser/non-BYPASSRLS login identities. Migrator is a non-owner
with CONNECT/CREATE database; app has only CONNECT plus migrator-granted USAGE
platform/SELECT history. Testcontainers superuser only provisions/inspects fixtures.
Application must not inherit DDL through PUBLIC, ownership or role membership.

Runner Up: `dotnet src/Platform.Migrator/bin/Release/net10.0/Platform.Migrator.dll`.
Only one runner at a time; no concurrent-migrator support. Up is idempotent.
EF handles migration/history transactions; grants occur after commit and a grant
failure may leave partial provisioning. Correct the cause and retry Up.
The tested DDL failure occurs before initial history creation; it does not prove
atomicity for arbitrary future DDL. Failure paths emit safe reason/correlation.

Rollback test uses `IMigrator.MigrateAsync("0", cancellationToken)` on a disposable
DB, verifies retained technical schema/history table and sentinel data, then Up
restores the exact initial migration `20261108144703_InitialPlatformInfrastructure`.
No production Down CLI exists. Default production recovery is reviewed forward-fix;
no automatic Down, DROP database or cascade. DB stop/start recovery is **not a
backup restore/DR drill**.

## Clean tracked-source export

Tested source **`bd16017ce6bebee98601c112037d61b03bd71572`**, committed with final script/test
before export. Export directory `/tmp/crm-task6-export.9Y4JUJ` was created fresh by
`git archive bd16017ce6bebee98601c112037d61b03bd71572 | tar -x -C <fresh-dir>`.
Confirmed script present and no API bin/obj before restore. No untracked source,
venv, caller configuration or previous build artifacts were copied.

With DOTNET_ROOT/PATH set as above and
`NUGET_PACKAGES=/tmp/crm-task6-clean-nuget-bd16017` (fresh package directory):

```bash
dotnet restore CrmPlatform.sln --locked-mode
dotnet build CrmPlatform.sln -c Release --no-restore
dotnet test CrmPlatform.sln -c Release --no-build --no-restore --filter FullyQualifiedName~SmokeTests
```

Actual results: all four project restores PASS; build PASS, zero warnings/errors
(7.36s); smoke **2 passed, 0 failed, 0 skipped** (32s). The helper runs
`bash scripts/backend-smoke.sh` from the export root using only its own fixture's
disposable database/roles. Success capture is asserted exactly
`SMOKE_PASS live=200 ready=200`; failure capture asserts BACKEND_SMOKE_FAILED plus
a valid correlation GUID, no success marker. Both assert the listener is gone.
No WebApplicationFactory is used by these smoke cases.

Committed/exported script SHA-256 both:
`6c7c593c3e98b03aa8d06555ce9190eade97315fc98f39122210d36434aeea3e`.
Local redacted command logs: `/tmp/task6-export-restore.log`,
`/tmp/task6-export-build.log`, `/tmp/task6-export-smoke.log`; working-source logs:
`/tmp/task6-restore.log`, `/tmp/task6-build.log`, `/tmp/task6-full.log`,
`/tmp/task6-contract.log`. Logs/export are disposable local artifacts, not remote CI.

Evidence-only commit follows the tested source; resolve its SHA from branch
history by subject `docs(platform): finalize clean-export smoke evidence`.
Final controller review remains pending. No runtime source changed after the
verified candidate; docs-only updates do not trigger redundant suite reruns.

## Final-review fix wave — F1/F2 (2026-11-08)

The preceding `bd16017` export/hash evidence is historical and does not verify
this changed script. Final controller review/status remains pending.

- F1: match LISTEN socket inodes in the child's Linux `/proc/<pid>/net/tcp{,6}`
  with `/proc/<pid>/fd` before and after HTTP probes. All listeners on the selected
  port must belong to the launched child. No free-port precheck is used as proof.
  Python 3 and readable proc metadata are now explicit Linux prerequisites; failure
  to establish ownership fails closed. No product API or contract changes.
- F2: TERM then a three-second watchdog escalates to KILL; reap the owned child,
  cancel/reap the watchdog, preserve the original script exit status.
- Behavioral RED against the prior script: focused new theory failed 2/2.
  Foreign healthy listener was falsely accepted (172ms); TERM-ignoring child
  exceeded the 35-second test budget. `/tmp/final-fix-red.log`.
- GREEN: `dotnet test tests/Integration/Integration.csproj -c Release --no-restore
  --filter FullyQualifiedName~SmokeTests`: 4 passed, 0 failed/skipped (1m1s).
  `/tmp/final-fix-green.log`. Two deterministic lifecycle fixtures substitute only
  the dotnet process boundary; existing two cases still run real API/PostgreSQL.
  Foreign listener serves exact live/ready responses and survives smoke failure;
  owned TERM-ignoring server is gone on successful script exit.
- Credentials remain env-only and captured diagnostics withheld. Lifecycle
  fixtures use disposable placeholder values, never real credentials.

Final changed working source: `dotnet restore CrmPlatform.sln --locked-mode` PASS;
`dotnet build CrmPlatform.sln -c Release --no-restore` PASS (0 warnings/errors,
3.97s); `dotnet test CrmPlatform.sln -c Release --no-build --no-restore` PASS
24/24, 0 failed/skipped (1m1s). Contracts PASS 32/32 (1.542s). Logs:
`/tmp/final-fix-{restore,build,full,contract}.log`. Shell syntax and diff check PASS.
The source commit immediately following this record contains the final script;
its exact committed-source clean export is recorded separately below.

### Final script clean committed-source export

Tested source: **`e54eb3e728eddad000f536cadaca47b5a71f2956`**.
Fresh directory `/tmp/crm-final-fix-export.0DwEPq`, populated solely via
`git archive e54eb3e | tar -x -C <fresh-dir>`, with no copied bin/obj or caller
configuration. Fresh `NUGET_PACKAGES=/tmp/crm-final-fix-clean-nuget-e54eb3e`;
SDK DOTNET_ROOT/PATH as above. Commands run from export root:

```bash
dotnet restore CrmPlatform.sln --locked-mode
dotnet build CrmPlatform.sln -c Release --no-restore
dotnet test CrmPlatform.sln -c Release --no-build --no-restore --filter FullyQualifiedName~SmokeTests
```

Actual: four project restores PASS; Release build PASS, 0 warnings/errors
(6.88s); SmokeTests **4 passed, 0 failed/skipped** (1m3s). Includes real
API/PostgreSQL success/failure plus occupied-listener and bounded-cleanup cases.
The test helper invokes the exported `bash scripts/backend-smoke.sh` using
only disposable fixture values/env. Full .NET and contract suites were run in
the working source, not repeated in export; export coverage is focused smoke.

Final committed/working/exported script SHA-256 all match:
`471bae90aa92573bbad151045af4fc7ea45c6718fa8e5f1878378491d712cf3e`.
Logs `/tmp/final-fix-export-{restore,build,smoke}.log`. This supersedes the
historical Task 6 script export for the changed script. The following commit is
**documentation only**, subject `docs(platform): record final smoke fix export evidence`.
Controller final-review status and plan final checkbox remain untouched/pending.
