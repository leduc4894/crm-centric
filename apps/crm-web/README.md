# CRM Centric web application

Minimal React + TypeScript workspace for the CRM platform frontend.

This slice delivers the workspace, the platform shell and the restricted same-origin development
proxy. The operational health states, the retry action and the browser end-to-end tests are a
later slice and are intentionally not implemented here.

## Prerequisites

| Tool    | Pinned version | Source                                                      |
| ------- | -------------- | ----------------------------------------------------------- |
| Node.js | 24.22.2        | `.node-version`, enforced by `engine-strict=true` in `.npmrc` |
| npm     | 11.19.1        | `packageManager` in the root `package.json`                 |

The root `.npmrc` sets `save-exact=true`, so every dependency is pinned to an exact version. Do not
introduce `^` or `~` ranges.

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

The dev server proxies exactly two paths to the loopback API:

| Path           | Target                             |
| -------------- | ---------------------------------- |
| `/health/live` | `http://127.0.0.1:${LOCAL_API_PORT:-5080}` |
| `/health/ready`| `http://127.0.0.1:${LOCAL_API_PORT:-5080}` |

Every other path, including the identity provider, the admin API and the business API, is served by
the dev server itself and is never forwarded. The allowlist is validated at startup and a wildcard
CORS origin is never configured.

### Credential stripping at the proxy boundary

The operational endpoints are anonymous by contract, so the proxy removes the credential-bearing
headers before the upstream request is sent. The stripped list is explicit and small:

| Header | Reason |
| --- | --- |
| `authorization` | The `bearerAuth` scheme of the CRM API contract |
| `cookie` | The browser session, which must never reach the API host |
| `x-tenant-id` | The canonical tenant context header of the CRM API contract |

`Accept`, `Content-Type` and `X-Correlation-Id` are legitimate request headers and are forwarded
unchanged. The `Host` header is still the browser host (`localhost:5173`) because the proxy keeps
`changeOrigin: false`; that behaviour is tracked separately and is not changed here.

### Changing dependencies or the workspace root

Every dependency is pinned to an exact version. After **any** change to the repository-root
`package.json` or `package-lock.json`, the generated SDK provenance must be regenerated, because
both files are inputs to the SDK generator's configuration hash:

```bash
python3 scripts/contracts/generate.py
python3 scripts/contracts/check.py    # must print SDK_GENERATED_CURRENT
git add sdks/*/provenance.json
```

Without this the byte-for-byte generated check reports `SDK_GENERATED_MISMATCH`. The same applies
to any later slice that adds a dependency, for example a browser test runner.

## Verify

```bash
npm run typecheck -w apps/crm-web
npm run lint -w apps/crm-web
npm run test -w apps/crm-web
npm run build -w apps/crm-web
```

## Static build

```bash
npm run build -w apps/crm-web   # writes apps/crm-web/dist
```

The static build and the development proxy are two different things. `npm run preview -w apps/crm-web`
serves the static assets only; it is **not** a production reverse proxy and it does not proxy any
API path. A production deployment must configure its own same-origin upstream.

## What this slice does not provide

- No authentication flow and no identity provider callback.
- No tenant context, no authorization headers and no token storage.
- No business API calls and no contact data.
- No operational health states, no retry action and no browser end-to-end tests.
