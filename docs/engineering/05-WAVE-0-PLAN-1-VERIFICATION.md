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
