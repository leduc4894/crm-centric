# Local development stack

Run from a checkout with Bash, Python 3, Docker Engine and Docker Compose available:

```bash
bash scripts/dev-up.sh
bash scripts/dev-smoke.sh
bash scripts/dev-down.sh
```

The developer path needs no host .NET SDK. Up builds pinned images from the locked
source, starts two separate PostgreSQL services, provisions CRM identities, runs
the existing migration, and checks the API plus Keycloak. API defaults to
`http://localhost:5080`; issuer defaults to
`http://localhost:8080/realms/crm-local`. Both published listeners bind IPv4
loopback only. Database ports and Keycloak management port are not published.

Use a non-root account with nonzero UID/GID and access to the local Unix-socket
Docker daemon. Build arguments map application/Keycloak runtime users to that
account so owner-only secret mounts remain readable. Verified tool versions and
actual test results are in [the verification record](../engineering/06-WAVE-0-PLAN-2-VERIFICATION.md).
No additional SDK, PostgreSQL client or curl is required by these commands.

Public environment settings:

| Setting | Default |
| --- | --- |
| `LOCAL_STACK_PROJECT` | `crm-local-` plus canonical checkout path hash |
| `LOCAL_STACK_STATE_DIR` | absolute `<checkout>/.local/stack/<project>` |
| `LOCAL_API_PORT` | `5080` |
| `LOCAL_IDP_PORT` | `8080` |
| `LOCAL_START_TIMEOUT_SECONDS` | `180` after build |
| `LOCAL_BUILD_TIMEOUT_SECONDS` | `900` |

Ports must differ and be 1–65535. Timeouts must be finite, positive and at most
3600 seconds. Custom state paths must be absolute, canonical and free of symlinks.
Within the checkout, state must be below `.local/` so both secret and metadata
files stay outside Git and the Docker context. Other custom locations must be
outside the checkout. Do not point a project at another checkout's resources or change its secret mount
path while containers exist. Each service has only its own network and required
secret files; the API receives only its application connection file.

A new project generates cryptographically random credentials in a 0700 state
directory; files and manifest are 0600. A separate 0600 sibling file
`<LOCAL_STACK_STATE_DIR>.stack.json` records public project/checkout/state identity,
ports, secret filenames and pending/ready provisioning status. Down preserves
both manifests, so port/configuration mismatch is still detected after containers
are removed. Reuse the same port settings for an existing project; choose a new
project/state for a separately configured stack. The metadata never replaces actual
database ownership/bootstrap validation. Keep these files with the data volumes.
Up reuses and validates it; missing/changed credentials with existing volumes fail
closed. Values never enter Compose interpolation, command arguments or build
arguments. Host administrators can read local files/process memory; these mounts
are a development mechanism, not an encrypted production secret manager.

Every up rebuilds current source and recreates bootstrap/migrator one-shots.
It removes the previous API before running them, so a failed migration cannot
leave an old ready API masking failure. Successful up requires real API live/ready
200, healthy container-local probes, and Keycloak discovery/JWKS and persisted
realm metadata. API readiness itself depends only on CRM PostgreSQL. See
[IdP configuration](05-IDP-CONFIGURATION.md) for realm drift and production limits.

Smoke checks the existing project's container labels, each service's exact secret
source filename and destination, bind type/read-only flag, exact loopback port
mapping, container-local health, host API endpoints and actual IdP realm metadata.
Missing, extra, writable or reassigned credential mounts fail
`LOCAL_SECRET_STATE_MISMATCH`, including another credential from the same state
directory. Healthy endpoints alone do not prove the API uses its restricted role. It does
not launch a host API or accept an unrelated HTTP listener as evidence. All three
commands take the same nonblocking OS lock, scoped to the local Docker endpoint
and project across checkout/state paths. Concurrent use fails
`LOCAL_LIFECYCLE_BUSY`; kernel lock release handles terminated callers.

Down stops/removes project containers and networks, allowing a 20-second shutdown
grace. It preserves named volumes, secrets and the realm. There is no automatic
volume removal, database drop, realm reset or password rotation. A retry after a
failed startup retains evidence/data and reuses the same credentials. Destructive
reset and credential reconciliation are separate deliberate operator actions.

Commands emit safe JSON with service/event/correlation, outcome or reason token,
project and public endpoints. Build and startup durations are separate. Captured
vendor diagnostics are not printed by the helper. Failure is nonzero; resolve the
reported dependency, port, secret state or realm issue before retrying. Do not
paste raw container logs or secret files into shared reports.

The .NET SDK is required only for development test harnesses:

```bash
dotnet test CrmPlatform.sln -c Release --filter 'Category!=Compose'
dotnet test CrmPlatform.sln -c Release --filter 'Category=Compose'
python3 -m unittest discover -s tests/Local -v
```

Compose fixtures use random project names, separate state/ports, and remove only
their own labeled resources. These local checks do not close Wave 0/P0 or establish
CRM login, tenant authorization, remote-module E2E or restore readiness.
