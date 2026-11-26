# CRM Centric web application

Minimal React + TypeScript workspace for the CRM platform frontend.

This slice provides the platform shell, browser OIDC sign-in, tenant selection, a restricted
same-origin development proxy and the operational health UI.

## Prerequisites

| Tool    | Pinned version | Source                                                      |
| ------- | -------------- | ----------------------------------------------------------- |
| Node.js | 24.22.2        | `.node-version`, enforced by `engine-strict=true` in `.npmrc` |
| npm     | 11.19.1        | `packageManager` in the root `package.json`                 |

The root `.npmrc` sets `save-exact=true`, so every dependency is pinned to an exact version. Do not
introduce `^` or `~` ranges.

The browser test runner additionally needs the pinned Chromium build. The browser binary is a local
prerequisite and is never committed:

```bash
npx playwright install chromium
```

On a bare Linux host the browser also needs its shared libraries (GTK, ATK, X11, GBM, ALSA and
friends). Install them with the distribution packages of your distribution:

```bash
npx playwright install-deps chromium
```

`npx playwright install-deps` needs a package manager with administrative rights; it is a local
prerequisite of the browser and is **not** part of the build, the test scripts or any continuous
integration path of this repository. Extracting the same libraries into a temporary directory and
pointing `LD_LIBRARY_PATH` at it is an equivalent local workaround.

`@playwright/test` 1.65.0 pins the browser build; the resolved build of this repository is
`chromium-1254` (Chrome for Testing 158.0.8127.18), which `npx playwright install --dry-run
chromium` prints. The version is fixed by the committed `package-lock.json`.

## Install

Install from the repository root so the workspace lockfile stays authoritative:

```bash
npm ci
```

The backend stack is started separately; see
[the local development guide](../../docs/operations/04-LOCAL-DEVELOPMENT.md). The frontend never
starts or stops the developer stack.

## Develop

```bash
npm run dev -w apps/crm-web
```

The dev server binds `http://localhost:5173` with a strict port: if the port is already occupied
the server fails with `Port 5173 is already in use` instead of selecting another port.

The local API stack must already be running; this application never starts or stops the developer
stack. The proxy target defaults to `http://127.0.0.1:5080` and can be changed with the
`LOCAL_API_PORT` environment variable:

```bash
LOCAL_API_PORT=5090 npm run dev -w apps/crm-web
```

`LOCAL_API_HOST` is accepted but restricted to the loopback literals `127.0.0.1` and `::1`. A
malformed port or a non-loopback host fails at startup with a reason token
(`WEB_PROXY_TARGET_INVALID` or `WEB_PROXY_TARGET_NOT_LOOPBACK`) and a correlation ID, before any
server is started. The dev proxy can therefore never become a server-side request forgery entry
point.

### Proxy allowlist

The dev server proxies the following exact paths to the validated loopback API:

| Path           | Target                             |
| -------------- | ---------------------------------- |
| `/health/live` | `http://127.0.0.1:${LOCAL_API_PORT:-5080}` |
| `/health/ready`| `http://127.0.0.1:${LOCAL_API_PORT:-5080}` |
| `GET /api/v1/tenants` | `http://127.0.0.1:${LOCAL_API_PORT:-5080}` |

The tenant-list proxy accepts GET only, forwards its bearer header but strips cookies and the
tenant header, and rejects upstream redirects. Other API and IdP paths are not forwarded.
The health allowlist is validated at startup and a wildcard CORS origin is never configured.

### Credential stripping at the proxy boundary

The operational endpoints are anonymous by contract, so the proxy removes the credential-bearing
headers before the upstream request is sent. The stripped list is explicit and small:

| Header | Reason |
| --- | --- |
| `authorization` | The `bearerAuth` scheme of the CRM API contract |
| `cookie` | The browser session, which must never reach the API host |
| `x-tenant-id` | The canonical tenant context header of the CRM API contract |

The removal happens on the incoming request, before the proxy copies the request headers to the
outgoing request, so the credentials are never part of that copy. The proxy's own request hook is
not the only mechanism: Vite emits that hook only when the outgoing request carries no `Expect`
header, so a request with `Expect: 100-continue` would otherwise keep its credentials. A browser
cannot send `Expect` — it is a forbidden header name — so the browser threat model was never
affected, but the control now applies to every proxied request.

`Accept`, `Content-Type` and `X-Correlation-Id` are legitimate request headers and are forwarded
unchanged. The `Host` header is still the browser host (`localhost:5173`) because the proxy keeps
`changeOrigin: false`; that behaviour is tracked separately and is not changed here.

## Browser sign-in and tenant selection

Set these public Vite settings before starting the development server (the values are not secrets):

```bash
VITE_OIDC_ISSUER=http://localhost:8080/realms/crm-local \
VITE_OIDC_CLIENT_ID=crm-web-local \
VITE_OIDC_CALLBACK_URL=http://localhost:5173/auth/callback \
VITE_OIDC_LOGOUT_URL=http://localhost:5173/ \
VITE_OIDC_LOCAL_HTTP=true npm run dev -w apps/crm-web
```

The issuer must match discovery exactly; callback and logout URLs must match the browser origin
and the provider's registered exact URLs. The HTTP flag works only for localhost in development;
production requires HTTPS. Local Keycloak client audience and warm-realm procedures are documented
in [IdP configuration](../../docs/operations/05-IDP-CONFIGURATION.md). The API must use the
same issuer and its configured `crm-api` audience. No client secret belongs in browser config.

The browser uses code + S256 with state and nonce; a short-lived single-use transaction is held
in session storage through the redirect. Issued tokens and selected tenant remain in memory, so a
full reload requires signing in again. The list supports pagination and suspended tenants cannot be
selected. Selection is not authorization: the API validates tenant membership and staff binding.
Logout clears local memory before submitting a provider logout request. Provider availability and
session termination are separate from local cleanup; logout is not a promise that issued tokens are
immediately revoked.

Production ingress must serve the SPA and tenant-list API on the same HTTPS origin, route the exact
callback to the SPA, preserve the intended authorization header only on API routes, and strip
untrusted forwarding headers. The development proxy does not provide production routing. The built
callback prelude must execute before resources load: configure a CSP hash or nonce for the actual
emitted inline script and accommodate Trusted Types if enforced; do not enable unrestricted inline
scripts. The focused ordering evidence currently covers Chromium, not all browsers.

## The health UI

The home page shows the operational connection status of the platform and a **Check again** action.

- The page performs one check when it mounts and one more per explicit retry. It never polls and
  never retries on its own, and it stores nothing in browser storage, a service worker or a cookie.
- Every check has a total deadline of **five seconds including the body read**. A request that is
  still pending after the deadline is aborted and reported as `WEB_REQUEST_TIMEOUT`.
- The state is announced through an `aria-live` region (`role="status"`), and the retry action is a
  native button, so it is operable with the keyboard.
- A repeated check cancels the pending request, an unmount cancels it as well, and a stale response
  never overwrites the state of a newer check.

The result is a discriminated union:

| State | Reason token | Correlation |
| --- | --- | --- |
| ready | — | — |
| unavailable | the server reason token of a valid Problem | the server correlation when it is valid, otherwise a client correlation (`web-…`) |
| timeout | `WEB_REQUEST_TIMEOUT` | a client correlation (`web-…`) |
| network | `WEB_NETWORK_ERROR` | a client correlation (`web-…`) |
| invalid | `WEB_CONTRACT_INVALID` | a client correlation (`web-…`) |

Only the three local classification codes exist. Every closed error path carries a reason token and
a correlation identifier: a server correlation identifier is only ever shown when the server sent a
usable one, and a malformed server correlation identifier is not displayed. A transport failure
never carries a server reason or a server correlation, and a client correlation identifier is
prefixed with `web-` so that it can never be read as a server one. The
rendered output contains the safe message, the reason token and the correlation identifier only:
never the response body, the problem title or detail, a stack trace, the request URL, a query
string or a token.

### The generated client and the declared dependency

`apps/crm-web/package.json` declares `@crm-centric/operations-client@0.1.0`, but every import of
that package is resolved to the **generated source** `sdks/typescript/operations/index.ts` — never
to the published `dist/` entry point, which is an uncommitted build artifact:

| Consumer | Resolution |
| --- | --- |
| Vite build and dev server | `resolve.alias` in `config/aliases.ts` |
| TypeScript | `paths` in `tsconfig.json` |
| Vitest | the same `resolve.alias` in `vitest.config.ts` |
| Playwright | the browser always loads the bundle that the dev server built with that alias |

The dependency is declared so the workspace manifest describes the surface the application uses;
the imported artifact is the generated source. If the alias is removed, the import fails, because
the package's compiled `dist/` output is not part of the committed tree.

## Verify

```bash
npm run typecheck -w apps/crm-web
npm run lint -w apps/crm-web
npm run test -w apps/crm-web
npm run build -w apps/crm-web
```

### Browser end-to-end tests

```bash
npm run test:e2e -w apps/crm-web
```

The suite starts the development server itself (strict port, `localhost:5173`) and contains four
browser tests: an intercepted 503 Problem, an intercepted timeout, an intercepted payload with a
script injection attempt, and one smoke test that goes through the development proxy to the **real
API and PostgreSQL** of the local stack. The smoke test reads the upstream responses directly,
compares them byte for byte with the response the browser received, requires the API's own
correlation identifier and `no-store` cache control, and requires the **API container of the
compose project of this checkout** — identified by its compose project and service labels and by
its compose container name — to publish the API port. A foreign container that publishes the same
port is not the stack and never satisfies that check. The test fails loudly when the stack is not
running.

The timeout test measures the elapsed time until the deadline fires: a deadline that is too short
and a deadline that is too long both fail the suite, and the unit suite pins the deadline value
itself.

The local stack must therefore be up before the browser suite runs:

```bash
bash scripts/dev-up.sh
npm run test:e2e -w apps/crm-web
bash scripts/dev-down.sh
```

### Leak scan

```bash
npm run scan:leaks -w apps/crm-web
```

The scan builds the bundle with source maps, drives the real application against a stub API that
answers with synthetic sentinel values, and then proves that no sentinel, secret, credential or
`.local/` metadata appears in the build artifacts, the source maps, the browser console, the
**rendered markup** of the document and the application root, or the process output of the
development server. The rendered markup is scanned and not only the visible text, so a payload
that reaches an attribute is caught as well. A finding is reported by name only; the payload is
never printed.

## Static build

```bash
npm run build -w apps/crm-web   # writes apps/crm-web/dist
```

The static build and the development proxy are two different things. `npm run preview -w apps/crm-web`
serves the static assets only; it is **not** a production reverse proxy and it does not proxy any
API path. A production deployment must configure its own same-origin upstream.

## Changing dependencies or the workspace root

Every dependency is pinned to an exact version. After **any** change to the repository-root
`package.json` or `package-lock.json`, the generated SDK provenance must be regenerated, because
both files are inputs to the SDK generator's configuration hash:

```bash
python3 scripts/contracts/generate.py
python3 scripts/contracts/check.py    # must print SDK_GENERATED_CURRENT
git add sdks/*/provenance.json
```

Without this the byte-for-byte generated check reports `SDK_GENERATED_MISMATCH`.

## What this slice does not provide

- No production IdP deployment or production reverse proxy.
- No business API calls and no contact data.
- No liveness indicator; the page shows the readiness state, which covers the database and the
  schema only and is not evidence of user, tenant or business readiness.

### Real-provider OIDC browser proof

The isolated browser proof creates a new Docker Compose project with its own Keycloak,
PostgreSQL and API, provisions disposable test identities and two tenant memberships, and
removes its owner-labeled resources afterward. It does not use an existing local stack.
Docker, Linux `/proc`, the pinned Node/npm toolchain, Chromium and its system libraries must be
available; `localhost:5173` must be free. The fixture fails closed if it cannot verify listener ownership. From the repository root run:

```bash
CRM_REAL_OIDC_TEST=1 npm exec --workspace @crm-centric/crm-web -- playwright test tests/e2e/browser-oidc.spec.ts --project=chromium --grep 'real provider signs'
```

The fixture keeps generated credentials and tokens in memory, and browser traces, video and
screenshots are disabled. It checks A/B tenant membership and a cross-tenant API denial,
provider logout, callback state/replay, session expiration, and selected failure and leak
paths. It is focused evidence, not the full Wave 1 or P0 acceptance suite.
