# Wave 0 Plan 1 — Task 1 evidence

Date: 2026-11-08. Local Ubuntu 24.04, linux-x64. Scope: backend bootstrap,
operational contract and database-independent liveness only. This does not close
Plan 1, Wave 0 or P0 acceptance. No CI, remote E2E, restore drill, migration,
readiness, role privileges or PostgreSQL recovery results are claimed.

## Toolchain and sources

SDK **10.0.403**, runtime/ASP.NET Core **10.0.14**. Installed in
`/tmp/crm-dotnet-10.0.403` after checking the archive SHA-512 against
[Microsoft release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json).
[Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
lists .NET 10 as active LTS through 2028-11-14. `global.json` disables roll-forward
and prerelease SDKs.

Archive: `https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.403/dotnet-sdk-10.0.403-linux-x64.tar.gz`.
Verified SHA-512:

```text
b7b3d8461cf98b07d2552ce9199702fa3ad010ac2671a5f8cd5136f7534e50f6855ba7bbb02b11494563f1d04156276a31cda1103b900e5eecd0d9473dc7b9c5
```

Reuse in this environment (the `/tmp` installation is disposable):

```bash
export DOTNET_ROOT=/tmp/crm-dotnet-10.0.403
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
source .venv/bin/activate
```

Package versions were selected from the official NuGet flat-container version
indexes, filtering out prereleases. Direct dependencies are centrally versioned
in `Directory.Packages.props`; all four projects commit `packages.lock.json` with
transitive versions and content hashes. EF relational is explicitly aligned with
EF Core, avoiding the provider's older minimum relational patch.

| Package | Version | Official metadata |
|---|---|---|
| Microsoft.EntityFrameworkCore | 10.0.14 | [NuGet](https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore/index.json) |
| Microsoft.EntityFrameworkCore.Relational | 10.0.14 | [NuGet](https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore.relational/index.json) |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | [NuGet](https://api.nuget.org/v3-flatcontainer/npgsql.entityframeworkcore.postgresql/index.json) |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.14 | [NuGet](https://api.nuget.org/v3-flatcontainer/microsoft.aspnetcore.mvc.testing/index.json) |
| Microsoft.NET.Test.Sdk | 18.11.0 | [NuGet](https://api.nuget.org/v3-flatcontainer/microsoft.net.test.sdk/index.json) |
| xunit | 2.9.3 | [NuGet](https://api.nuget.org/v3-flatcontainer/xunit/index.json) |
| xunit.runner.visualstudio | 4.0.1 | [NuGet](https://api.nuget.org/v3-flatcontainer/xunit.runner.visualstudio/index.json) |
| Testcontainers.PostgreSql | 4.16.0 | [NuGet](https://api.nuget.org/v3-flatcontainer/testcontainers.postgresql/index.json) |

Docker client/server **29.9.3**: `docker version` and `docker info` succeeded.
PostgreSQL **18.6** is supported per the
[PostgreSQL versioning policy](https://www.postgresql.org/support/versioning/).
The exact tag was checked in the
[Docker official image manifest](https://github.com/docker-library/official-images/blob/master/library/postgres),
pulled successfully and its registry digest confirmed by `docker image inspect`:

```text
postgres:18.6-bookworm@sha256:f1ea2a1a4fb7b68d4836be1fdcb8a6466a89b072158bf9d97663600538c294c5
```

This is the verified image pin for subsequent PostgreSQL fixtures. Task 1 does
not start a database or claim Testcontainers runtime coverage: its HTTP test
intentionally uses an unavailable endpoint.

Python **3.12.3**; no preexisting `.venv`. System Python lacked pip/ensurepip.
Created `.venv`, bootstrapped pip using `https://bootstrap.pypa.io/get-pip.py`,
then installed the existing `requirements-contracts.lock` unchanged. Actual pip
version: **26.3**. The install also resolved `typing-extensions==4.16.0` from the
existing dependency graph; no Python dependency files were modified.

## RED and GREEN

Run from repository root with the exports above. The RED runs used the same
commands below with explicit executable paths (`.venv/bin/python` and
`/tmp/crm-dotnet-10.0.403/dotnet`).

| Command | RED | Final GREEN |
|---|---|---|
| `python -m unittest discover -s tests/Contract -v` | 32 run, 3 failures: operational contract not implemented; 29 existing tests pass | 32 pass |
| `dotnet test tests/Integration/Integration.csproj --filter FullyQualifiedName~HealthTests` | 1 failure: expected OK, actual NotFound, route not mapped | 1 pass, 0 skipped |
| `dotnet restore CrmPlatform.sln --locked-mode --force --no-cache --packages /tmp/crm-task1-clean-nuget` | Not a RED step | All four restored into a fresh package directory |
| `dotnet test CrmPlatform.sln -c Release` | Not a RED step | 1 pass, 0 skipped |
| `dotnet build CrmPlatform.sln -c Release` | Not a RED step | All four projects, 0 warnings, 0 errors |

Contract tests cover OpenAPI validation, two anonymous GET routes, status enums,
response headers, independent baseline-equivalent Problem schema, rejected
missing reason/correlation and extra fields, validated success/error examples,
and retained business security. The existing business contract was not edited.

The real ASP.NET HTTP pipeline test points to a bound, non-listening TCP port
with an in-memory test connection string and a generated test credential. It
asserts the exact single-field live response, no-store, server-generated
correlation replacing caller input, and a fresh correlation on the next request.
No credentials are passed through command-line arguments or test logs.

Readiness and 500 Problem behavior are contract definitions only at this step.
The migrator builds, emits a safe `MIGRATOR_NOT_IMPLEMENTED` reason and correlation,
and exits 1; it cannot report a successful migration.

## Task 2 — explicit migrations and restricted application role

Task 2 uses the SDK, EF/Npgsql and PostgreSQL pins above. Added private
design-time dependency `Microsoft.EntityFrameworkCore.Design` **10.0.14** and
local `dotnet-ef` **10.0.14** in `dotnet-tools.json`, aligned with the verified
EF patch. Restore tools with `dotnet tool restore`.

Generated the initial migration, designer and snapshot with:

```bash
dotnet ef migrations add InitialPlatformInfrastructure --project src/Platform.Infrastructure --output-dir Persistence/Migrations
```

The entity-free generated Up was empty; explicitly added only
`EnsureSchema("platform")`. Provider-managed history is
`platform.__EFMigrationsHistory`; migration ID is
`20261108144703_InitialPlatformInfrastructure`. Real PostgreSQL verifies the
provider creates the schema before its history table. No invented entity/table
or startup migration is present.

### Dedicated database privilege boundary

Controller ruling supersedes the plan's database-owner migrator fixture:
a database owner retains CREATE even after REVOKE. The administrator therefore
owns each new disposable dedicated database. It provisions separate LOGIN roles
with runtime-generated credentials, neither superuser nor BYPASSRLS; the
migration role is a non-owner with only CONNECT and CREATE on that database.
It creates and owns the technical schema/history through EF. The application
role initially has only CONNECT.

In that **new dedicated database only**, administrator provisioning revokes ALL
database privileges from PUBLIC (including CREATE/TEMPORARY/CONNECT) and ALL
privileges on schema public from PUBLIC. It grants CONNECT/CREATE to the
migrator and CONNECT to the app. These are local database ACL changes, never
cluster-wide role defaults or changes to other host databases. The migrator
cannot revoke database-owner privileges: administrators must establish this
baseline before running it. Do not apply these revocations blindly to an
existing shared database.

The runner checks the configured app login exists, differs from current_user,
is not superuser/BYPASSRLS and has no effective database/public-schema CREATE.
It fails safely if those prerequisites are violated. After Up it grants only
USAGE on platform and SELECT on history to the app, quoting its identifier with
NpgsqlCommandBuilder. It does not create logins or grant future domain access.
Tests use an app role containing a hyphen and quote, inspect effective privileges
and ownership, SELECT history successfully, and observe SQLSTATE 42501 for
INSERT/UPDATE/DELETE, CREATE TABLE in platform/public, and CREATE SCHEMA.

Run with settings securely injected into the process environment or secret
store, never put credential values in shell arguments or logs:

```bash
dotnet run --project src/Platform.Migrator --no-build -c Release
```

Required environment keys: `ConnectionStrings__Platform` (migration identity),
`Database__ApplicationRole` (already-provisioned login name, not a password).
The API uses the same connection setting name with the app identity.
Only one runner may execute at a time. Success exits 0; configuration failures
exit 1 with DATABASE_CONFIGURATION_INVALID; other failures exit 1 with
DATABASE_MIGRATION_FAILED. Diagnostics contain a generated correlation GUID,
never raw exceptions/provider messages. A grant failure after committed
migrations can leave partial provisioning; retrying Up is idempotent.
Rollback/failure-path tests belong to Task 3, readiness to Task 4 and expanded
diagnostic/security tests to Task 5; no results for those tasks are claimed.

### Task 2 RED / GREEN

- RED: `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~MigrationTests|FullyQualifiedName~RolePrivilegeTests'`:
  3 failed / 0 passed, each expected exit 0 but got 1 from the real Task 1 stub.
  PostgreSQL containers and provisioning succeeded; this was a behavioral RED.
- GREEN: same command: 3 passed / 0 failed / 0 skipped. Release build:
  0 warnings, 0 errors. Each test owns its own container/database/roles.
- Final verification after exact migration/schema assertions and quoted-role
  coverage: `dotnet restore CrmPlatform.sln --locked-mode` passed;
  `dotnet build CrmPlatform.sln -c Release --no-restore` passed, 0 warnings/errors;
  `dotnet test CrmPlatform.sln -c Release --no-build --no-restore` passed
  4/4, no skips; `.venv/bin/python -m unittest discover -s tests/Contract -v`
  passed 32/32. Local-only evidence; Wave 0/P0 acceptance remains open.

## Task 3 — migration failure and rollback safety

The three new regression tests exercise the unchanged Task 2 production code
against the existing disposable Testcontainers PostgreSQL fixture. Initial
`dotnet test tests/Integration/Integration.csproj -c Release --filter FullyQualifiedName~MigrationTests`
passed 5/5, with zero failures/skips (43 seconds). This is **baseline regression
GREEN**, not behavioral RED: no defect required a production change, and no RED
was fabricated, per the controller ruling.

- `Down_then_up_preserves_unowned_objects` creates a sentinel table and row as
  the migration identity, invokes `IMigrator.MigrateAsync("0", cancellationToken)`
  only inside the disposable test, verifies history is empty and sentinel data
  survives, then runs Up and verifies one history row and preserved data.
- `Ddl_failure_never_reports_success` verifies the migration identity is neither
  superuser/BYPASSRLS nor database owner, with an explicit CREATE grant. Admin
  revokes CREATE before Up on an empty dedicated database. The runner exits 1,
  emits only a valid `DATABASE_MIGRATION_FAILED` JSON diagnostic with a nonempty
  correlation GUID, and produces no success stdout. No history table exists.
  Restoring CREATE permits two successful Up runs with exactly the initial row.
- `Missing_application_role_fails_safely` supplies a nonexistent role, checks the
  same safe failure contract and absence of history, then supplies the existing
  valid role and verifies two successful Up runs and the exact initial history.

Transaction boundaries: EF manages migration/history transactions; Down retains
technical schema/history table and removes the applied migration row. The DDL
failure here is at schema/history bootstrap, before an initial row exists; this
is not evidence of rolling back arbitrary future migration statements. Missing
role validation occurs before migration and leaves no partial provisioning in
this tested case. Grants execute after `MigrateAsync`, outside its completed
migration transaction. A later grant failure can therefore leave committed
migration history with incomplete privileges; retry Up after correcting the
cause is the recovery path, not automatic Down. That post-commit failure window
is documented, not injected by these tests. No production rollback CLI was added.

Final Task 3 checks: `dotnet restore CrmPlatform.sln --locked-mode` passed;
`dotnet build CrmPlatform.sln -c Release --no-restore` passed with 0 warnings/errors;
`dotnet test tests/Integration/Integration.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~MigrationTests|FullyQualifiedName~RolePrivilegeTests'`
passed 6/6, zero skipped (40 seconds); `dotnet test CrmPlatform.sln -c Release --no-build --no-restore`
passed 7/7, zero skipped (35 seconds). `.venv/bin/python -m unittest discover -s tests/Contract -v`
passed 32/32. An initial mistaken pytest invocation failed because pytest is not
installed; the existing unittest command above passed without dependency changes.
Task 3 does not close Plan 1, Wave 0 or P0 acceptance.
