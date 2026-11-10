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
The 2026-11-10 clean archive run used source `34342cbb4f77458560759d6d644925ab1cecf702`,
UID/GID 1000:1000, fresh project/state/volumes and an empty HOME with only an
explicit environment allowlist. Image caches were reused. Docker 29.9.3/Compose
5.7.0 on Linux amd64 with 7,941 MiB RAM are tested versions, not supported-minimum
claims. Exact timings and retention/fixture cleanup are in the verification record.

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

## Frontend application

The web application is a separate npm workspace (`apps/crm-web`) and runs on the host, not in
Docker. It needs its own pinned Node and npm, and the local stack must already be up so the API
answers on the configured port.

| Requirement | Version |
| --- | --- |
| Node.js | `24.22.2` (`.node-version`, enforced by `engine-strict=true`) |
| npm | `11.19.1` (`packageManager` in the root `package.json`) |
| Chromium (browser tests only) | `chromium-1254`, Chrome for Testing `158.0.8127.18`, pinned by `@playwright/test` `1.65.0` |

Install from the repository root, because the root lockfile covers the whole workspace:

```bash
npm ci
```

Start the dev server after the stack is ready:

```bash
npm run dev -w apps/crm-web
```

The dev server binds `http://localhost:5173` with a strict port: if the port is occupied it fails
with `Port 5173 is already in use` and does not select another port. It proxies exactly two paths,
`/health/live` and `/health/ready`, to `http://127.0.0.1:${LOCAL_API_PORT:-5080}`. The identity
provider, the admin API and the business API are never proxied, and no wildcard CORS origin is
configured. `LOCAL_API_PORT` overrides the API port; `LOCAL_API_HOST` is restricted to the loopback
literals `127.0.0.1` and `::1`, and a malformed or non-loopback target fails at startup with a
reason token and a correlation ID before any server starts.

The proxy removes `authorization`, `cookie` and `x-tenant-id` before forwarding, so the
operational endpoints stay anonymous even once the browser holds a session. The removal happens on
the incoming request, before the proxy copies the request headers to the outgoing request. The
proxy's own request hook is not the only mechanism, because Vite emits that hook only when the
outgoing request carries no `Expect` header; a browser cannot send `Expect`, so the browser threat
model was never affected.

The home page shows the operational readiness state of the platform and a `Check again` action. One
check runs when the page mounts and one more per explicit retry; the page never polls. Every check
has a total deadline of five seconds including the body read and is classified as ready,
unavailable, `WEB_REQUEST_TIMEOUT`, `WEB_NETWORK_ERROR` or `WEB_CONTRACT_INVALID`. The state is
announced through an `aria-live` region, and the page renders the reason token and the correlation
identifier only — never the response body, a stack trace, the request URL or a token.

Verify the frontend:

```bash
npm run typecheck -w apps/crm-web
npm run lint -w apps/crm-web
npm run test -w apps/crm-web
npm run build -w apps/crm-web
```

The browser suite needs the pinned Chromium build, which is a local prerequisite and is never
committed:

```bash
npx playwright install chromium
```

On a bare Linux host the browser also needs its shared libraries (GTK, ATK, X11, GBM, ALSA and
friends). Install them with the distribution packages of your distribution, for example with
`npx playwright install-deps chromium`, which needs a package manager with administrative rights.
That step is a local prerequisite of the browser only: it is not part of the build, the test
scripts or any continuous integration path of this repository. Extracting the same libraries into a
temporary directory and pointing `LD_LIBRARY_PATH` at it is an equivalent local workaround.

The browser suite needs the local stack, because one of its four tests goes through the development
proxy to the real API and PostgreSQL and fails loudly when the stack is not running:

```bash
npm run test:e2e -w apps/crm-web
```

The leak scan proves that no secret, credential, sentinel or `.local/` metadata reaches the build
artifacts, the source maps, the browser console, the rendered markup or the process log:

```bash
npm run scan:leaks -w apps/crm-web
```

`npm run build -w apps/crm-web` writes the static assets to `apps/crm-web/dist`.
`npm run preview -w apps/crm-web` serves those assets only: it is not a production reverse proxy
and does not proxy any API path.

After any change to the repository-root `package.json` or `package-lock.json` — for example adding
a dependency — regenerate the SDK provenance and commit it, otherwise the generated check reports
`SDK_GENERATED_MISMATCH`:

```bash
python3 scripts/contracts/generate.py
python3 scripts/contracts/check.py    # must print SDK_GENERATED_CURRENT
git add sdks/*/provenance.json
```

The frontend details are in [`apps/crm-web/README.md`](../../apps/crm-web/README.md) and the
verification record is in
[the Plan 3 verification document](../engineering/07-WAVE-0-PLAN-3-VERIFICATION.md).
