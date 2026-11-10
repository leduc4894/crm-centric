# Wave 0 Plan 3 — implementation handoff

## Objective and authority

Implement [Plan 3](../superpowers/plans/2026-11-10-wave-0-plan-3-frontend-contract-toolchain.md) under the [design](../superpowers/specs/2026-11-10-wave-0-plan-3-frontend-contract-toolchain-design.md), on a new branch without a worktree. The user authorized implementation; do not merge, push or deploy. One coding agent at a time. The prior specification handoff remains relevant: [Plan 3 spec handoff](2026-11-10-wave-0-plan-3-spec-handoff.md).

## Current state

Branch `feat/wave-0-plan-3-frontend-contract-toolchain`, base `5257abf`. Task 1 source and verification are being prepared for the plan's Task 1 commit; consult `git log`/`git status` for the current commit and any uncommitted work. Task 1 still requires the SDD read-only task review and any required fixes before it can be marked complete. Tasks 2–7 have not been implemented.

The generator selection, pinned artifacts, prerequisites, coverage and current evidence are in [Plan 3 verification](../engineering/07-WAVE-0-PLAN-3-VERIFICATION.md). Corvus.Json.Cli 5.8.8 supplies C# generated clients; openapi-typescript 7.13.0 plus openapi-fetch 0.17.0 supplies typed TS clients. Generated C# and TypeScript SDKs, provenance, lockfiles, generation/check entrypoints and Task 1 tests are in the pending changes. No canonical contract was changed.

Completed verification before handoff: pinned `npm ci` passed; both C# outputs built with locked restore and zero compiler warnings/errors; both TS packages built and strict rule fixture compiled; C# rule and TS HTTP boundary fixtures passed; a canonical validator accepted valid remote rule and rejected a missing action installation; a staged clean index export passed the generated-check. Focused tests observed RED then GREEN for tree/ref tampering, safe reference resolution, no bytecode mutation on check, and checkout-stable newlines. Final `.venv/bin/python -m unittest discover -s tests/Contract -v` passed 38 tests (69.850 s); `python3 scripts/contracts/check.py` returned `SDK_GENERATED_CURRENT`. Consult the verification document and SDD task report for exact evidence; do not infer Task 1 review or later tasks from these checks.

## Remaining work

1. Review Task 1 against its brief, fix any material findings with regression tests, and verify the generated SDKs and provenance from the commit.
2. Complete Task 2 independent packaged consumer and wire tests, then Tasks 3–7 in order. Task 3/4 compatibility and security gates, Task 5/6 frontend and browser behavior, and Task 7 aggregate/clean export are not complete.
3. Record updated evidence and handoff at the end of subsequent work. The operations health routes are implemented in the existing backend; the business OpenAPI is pre-runtime. Remote DemoModule E2E, tenant isolation, outage/recovery, restore drills, full Wave 0/P0 acceptance and remote CI remain open.

## Task 1 review fixes

The independent Task 1 review identified two Important findings: missing Python/toolchain prerequisites did not always produce safe structured errors, and provenance omitted locally referenced files. Regression tests first reproduced both failures. Both entrypoints now report missing PyYAML or required toolchain keys as `SDK_TOOLCHAIN_UNAVAILABLE` with correlation ID; provenance includes hashes for the validated transitive local reference closure. The changed-reference regression generates twice and verifies a nested input hash changes while unchanged inputs retain their hashes. These fixes require the SDD scoped re-review before Task 1 is marked complete; Tasks 2–7 remain planned.

## Task 2 implementation (pending review)

Task 1's two review fixes passed scoped re-review. Task 2 adds independent npm/NuGet packaged consumers, their lock and HTTP wire assertions under `tests/ClientConsumers/`, plus `tests/Contract/test_sdk_consumers.py`. Both SDK languages are tested through captured HTTP, including status/problem/correlation, ETag/If-Match, 412/428 no retry, encoding, cancellation, anonymous operations after a synthetic business bearer, and valid AutomationRule wire variants. Package audit checks for server/web references; canonical invalid-rule fixture remains separate. NuGet pack ZIP timestamps are normalized to permit repeatable locked package restoration; generated SDK files and contracts were not edited.

Verification: Node 24.22.2 and host libatomic workaround with `.venv/bin/python -m unittest discover -s tests/Contract -v` passed 42 tests in 148.030s, including packaged consumers. A standalone packaging run passed with locally installed artifacts and locked NuGet restore. A first attempt at a NuGet consumer lock failed NU1403 due to nondeterministic package timestamps; archive timestamp normalization followed by fresh locked restore passed. Task 2 requires independent SDD review before marking complete. Tasks 3–7, real browser and Wave 0/P0 acceptance remain open. Branch `feat/wave-0-plan-3-frontend-contract-toolchain`; Task 2 commit follows this update.

## Task 2 review remediation (in progress)

Task 2 independent review raised four important issues: null-field assertion accepted missing email, assertion diagnostics could reveal a synthetic bearer on failure, generated .NET health responses dropped content type, and generated mutation responses did not expose the correlation response header. The remediation adds explicit failure-path tests and a non-generated, versioned metadata-handler facade packaged with each C# SDK. Generator provenance includes the facade template. The fix commit and scoped review remain pending.

Review-fix verification: the 42-test contract suite passed in 143.261 seconds with the pinned Node and host libatomic workaround, including packaged-consumer tests and generated-check. The Task 2 fix commit follows this update; scoped independent review is still required before proceeding.

## Task 3 implementation (review pending)

Task 2 review fixes passed scoped independent review. Task 3 adds a local Git-base compatibility gate using pinned oasdiff 1.37.0, effective security/scope and webhook signature checks, conservative JSON Schema review detection, and mutation fixtures. Input snapshots and validated local references reject remote references; oasdiff runs in a digest-pinned container with `--network none --pull=never`. Added schemas require review until fixture validation can be demonstrated. See the Plan 3 verification document for artifact pins and limitations. Task 3 is pending its commit and independent SDD review; do not infer Task 4 SDK-surface enforcement, frontend, browser or P0 completion from it. Consult the Task 3 report and current Git state for actual verification and status.

Task 3 verification: final `PATH=<Node-24.22.2>/bin:$PATH LD_LIBRARY_PATH=/tmp/plan3-libs .venv/bin/python -m unittest discover -s tests/Contract -v` passed 62 tests in 165.137s. This includes real oasdiff fixtures, existing HMAC and contract checks, and local package consumers. `python3 scripts/contracts/compatibility.py --base-ref HEAD` passed against `7c7a972394ef41ae346c9dba91e8535e87fc8c6e` prior to commit. Task 3 commit follows this update; independent review is still required.

## Task 3 review remediation (pending scoped review)

Independent Task 3 review identified that referenced schema files outside the `*.schema.json` filename pattern were omitted from semantic comparison and hashes, referenced Path Items were omitted from effective security checks, and unsupported OpenAPI minor versions could pass unchanged. The fix hashes the full validated standalone-schema reference closure, compares referenced content conservatively (with schema annotations normalized where valid), resolves internal Path Item references, fails closed for external Path Item references, and restricts OpenAPI versions to supported 3.0/3.1 patches. Network isolation remains in place. Regression and full-suite results and the fix commit are recorded in the Plan 3 verification document and SDD task report; scoped review remains pending.

Task 3 review-fix final verification: `PATH=<Node-24.22.2>/bin:$PATH LD_LIBRARY_PATH=/tmp/plan3-libs .venv/bin/python -m unittest discover -s tests/Contract -v` passed 68 tests in 167.381s. The fix commit follows this update and requires scoped independent review before Task 3 is complete.

## Task 4 implementation (pending scoped review)

Task 3 passed scoped independent review. Task 4 adds separate base/current SDK regeneration from the resolved Git base and current contract snapshots. A base with an existing toolchain is regenerated using its own generator, configuration and pinned .NET image; a first bootstrap uses the selected current toolchain on both sides and reports `bootstrap-no-released-consumer`. Missing historical tooling, missing historical consumer artifacts and tool errors fail closed as `SDK_TOOLCHAIN_REVIEW_REQUIRED`. The gate compares TypeScript exported declarations and compiled .NET public types/members including nullability, and compiles the Git-base TS and C# consumer fixtures against candidate SDKs. Candidate changes to existing comparison policy or waivers require review. Reported toolchain hashes identify generator/configuration drift. Independent scoped review is still required before Task 4 is complete.

Changed files for Task 4: `scripts/contracts/compatibility.py` (baseline configuration and SDK-surface stage, reason classifications), `scripts/contracts/sdk_surface.py` (new: isolated base/candidate preparation, generation, surface comparison, baseline consumer compilation), `tools/contracts/surface/compare-typescript.cjs` and `tools/contracts/surface/dotnet/{Program.cs,Surface.csproj}` (new: public-surface comparators), `tests/Contract/test_sdk_surface.py` (new) and `tests/Contract/test_compatibility.py` (Task 4 regressions). The canonical contracts under `docs/contracts/` were not modified.

Task 4 final verification: `PATH=<Node-24.22.2>/bin:$PATH LD_LIBRARY_PATH=/tmp/plan3-libs .venv/bin/python -m unittest discover -s tests/Contract -v` exited 0 with 83 tests in 814.153s. `python3 scripts/contracts/compatibility.py --base-ref 817948f` exited 0 with `CONTRACT_COMPATIBLE`, `sdkBaseline: historical-toolchain` and `sdkToolchainChanged: false` in 2m13.682s. A temporary generator-only break was rejected with `SDK_SURFACE_INCOMPATIBLE` and `sdkToolchainChanged: true`, and a temporary .NET-only nullability break was rejected the same way, with the HTTP contract unchanged in both probes. Both probes were reverted; the worktree holds only the Task 4 changes.

Review remediation in this task: the closed SDK-toolchain error path dropped `sdkBaseline`/`sdkToolchain` from the report, and the new surface comparators emitted a reason token without a correlation ID. Both are fixed with regression cases (`test_missing_historical_consumer_artifacts_fail_closed`, `test_unreadable_declarations_report_reason_and_correlation`).

Known limitations: the gate proves public-surface compatibility and baseline-consumer compilation only, not runtime behaviour or release governance; the TypeScript comparison is conservative about additions that add a required member; the new comparison tooling under `tools/contracts/surface/` is only protected against candidate edits from the next commit onwards because it does not exist in base `817948f`.

## Task 4 review fix round 1 (pending scoped review)

Independent review of `6c7b801` returned no Critical finding and three Important findings, all fixed in a separate follow-up commit. The TypeScript comparator now has a closed error envelope (reason token plus correlation ID, no raw stack trace) and the gate distinguishes a candidate that declares no public surface (`SDK_SURFACE_INCOMPATIBLE`) from a tool failure (`SDK_TOOLCHAIN_REVIEW_REQUIRED`). The tautological generator-only case was renamed and tightened to assert the exact reason it can actually reach; generator-only break detection stays covered by the real-generator cases. The policy/waiver scan now covers `tools/contracts/`, `scripts/contracts/`, `docs/contracts/` and the repository root, including untracked files, without matching ordinary names such as `.gitignore`; editing `toolchain.json` or `generate.py` remains outside the policy-review path. A failed .NET baseline-consumer build is now separated into a genuine compile break and a restore/infrastructure failure. The bootstrap path now has a regression asserting one pinned configuration for both sides.

Changed files: `scripts/contracts/compatibility.py`, `scripts/contracts/sdk_surface.py`, `tools/contracts/surface/compare-typescript.cjs`, `tests/Contract/test_compatibility.py`, `tests/Contract/test_sdk_surface.py`, `docs/engineering/07-WAVE-0-PLAN-3-VERIFICATION.md` and this handoff. The canonical contracts were not modified.

Fix-round verification: `.venv/bin/python -m unittest tests.Contract.test_compatibility -v` exited 0 with 39 tests in 753.902s; the scanner suite together with the four policy-scan cases exited 0 with 10 tests in 35.480s; `python3 scripts/contracts/compatibility.py --base-ref 817948f` exited 0 with `CONTRACT_COMPATIBLE` in 2m35.527s. The full `tests/Contract` suite result for this fix round is recorded in the verification document and the SDD task report.

## Task 5 frontend workspace and restricted local proxy (pending scoped review)

Task 4 review fix round 1 is committed. Task 5 turns the repository root into an npm workspace root and adds the `apps/crm-web` application. The root `package.json` declares `workspaces: ["apps/crm-web", "sdks/typescript/*"]`, pins `packageManager npm@11.19.1`, and delegates `dev`, `build`, `typecheck`, `lint` and `test` to `@crm-centric/crm-web`. The root `package-lock.json` is regenerated and committed; `npm ci` from the repository root installs the workspace from that lockfile.

The dev server binds `localhost:5173` with a strict port and proxies exactly `/health/live` and `/health/ready` to `http://127.0.0.1:${LOCAL_API_PORT:-5080}`. The proxy allowlist is a single regular-expression proxy key derived from the allowlist, so the two cannot drift apart. The proxy target and the allowlist are validated at startup: a malformed port fails with `WEB_PROXY_TARGET_INVALID`, a non-loopback host with `WEB_PROXY_TARGET_NOT_LOOPBACK`, both with a correlation ID and before any server starts. The identity provider, the admin API and the business API are never proxied and no wildcard CORS origin is configured. The whole environment is never loaded into the browser bundle.

The `HealthClient` is a thin adapter over the generated `@crm-centric/operations-client`: same-origin requests, no authorization, cookie or tenant header, no token storage, no browser storage writes, and validation of the server reason token and correlation ID before exposure. The frontend consumes the generated client from its generated TypeScript entry point through a Vite alias and a TypeScript path mapping, because the generated package's published entry point is the uncommitted `dist/` build artifact; this keeps the byte-compared `sdks/` tree free of build output.

Changed files: `package.json`, `package-lock.json`, `apps/crm-web/` (package manifest, Vite and Vitest configuration, TypeScript configurations, ESLint flat configuration, proxy and dev-server configuration with their tests, the health adapter with its tests, the app shell, the README and a scoped `.gitignore`) and the four `sdks/*/provenance.json` files. `docs/contracts/`, `compose.yaml`, the Keycloak realm and entrypoints and `scripts/local/` were not modified.

Cross-task effect that was explicitly checked: making the root an npm workspace root changes the root manifest and lockfile, which Task 1's generator includes in its configuration hash. `configurationSha256` therefore changes and the committed `sdks/` provenance was regenerated; `git diff HEAD --stat -- docs/contracts sdks/` shows four files, one line each, all `configurationSha256` values. No generated client code, schema, contract or lock changed. The isolated `npm ci --offline --ignore-scripts --no-audit --no-fund` that `generate.py` and `sdk_surface.py` perform was probed against the new lockfile in both snapshot shapes and exited 0 in both, producing the `openapi-typescript` and `tsc` binaries those scripts require. Neither script needed a change and no Task 1 or Task 4 check was weakened.

Task 5 verification: RED was observed with 21 failed and 6 passed of 27 tests for behavioural reasons; GREEN is 27 passed tests in 3 test files. Typecheck, lint and build exit 0. `npm ci` from the root exits 0 with 211 packages. Live checks: the dev server proxied only the health path to a loopback stub API and served every other path itself; a foreign listener on 5173 made `npm run dev` exit 1 with `Port 5173 is already in use`; a malformed port and a non-loopback host each exited 1 with a reason token and a correlation ID and started no server. `python3 scripts/contracts/check.py` returned `SDK_GENERATED_CURRENT`; `python3 scripts/contracts/compatibility.py --base-ref 408ded6` returned `CONTRACT_COMPATIBLE` with `sdkToolchainChanged: true` and identical base and current contract input hashes; `.venv/bin/python -m unittest tests.Contract.test_sdk_consumers -v` exited 0 with 1 test in 66.994s. Exact commands, output and limitations are in the Plan 3 verification document and the SDD task report.

Known limitations: the proxy behaviour is proven against a loopback stub API, not against the running backend, and there is no browser end-to-end test yet. The adapter does not classify transport failures, impose the 5 second deadline, manage stale-response races or render health states, and `vite preview` serves static assets only and is not a production reverse proxy. Task 5 requires independent SDD review before it is marked complete.

## Task 5 fix round 1 — credential stripping and matcher escaping (pending scoped review)

The independent review of `ffcc7ae` returned one blocking Important finding and one recommended finding, both at the proxy boundary.

**I-1 — the dev proxy relayed the browser's credential headers to the upstream API.** http-proxy copies the incoming request headers into the outgoing request and deletes `authorization` only when `options.auth` is set, which the health proxy does not set. A loopback stub received `authorization`, `cookie` and `x-tenant-id` from the browser. The operational endpoints are anonymous by contract, so this would hand a browser session or a tenant selection to the API host as soon as the browser holds credentials. The fix removes those headers at the proxy boundary in the proxy's own `configure`/`proxyReq` hook, so it applies to every proxied request. The stripped list is explicit and small: `authorization` (the `bearerAuth` scheme of the CRM API contract), `cookie` (the browser session) and `x-tenant-id` (the canonical tenant context header of the CRM API, `docs/contracts/crm-api.openapi.yaml`). No invented tenant header name was added. `Accept`, `Content-Type` and `X-Correlation-Id` are forwarded unchanged.

**M-4 — the proxy matcher was only incidentally regex-safe.** The matcher joined the allowlist paths with `|`, so a future path such as `/health/live.v2` would also have matched `/health/liveXv2`. `buildProxyMatcher` now escapes every regular-expression metacharacter of every path, making the "allowlist and matcher cannot drift apart" guarantee structural.

Covering tests: `apps/crm-web/config/devServer.test.ts` (`never relays a credential-bearing header to the upstream API`, against a real Vite dev server and a real loopback stub that records the headers it received) and `apps/crm-web/config/healthProxy.test.ts` (`does not let a regex metacharacter in an allowlist path widen the matcher`, five metacharacter cases, plus `keeps the allowlist paths as literal alternatives`).

Fix-round verification: RED was `npm run test -w apps/crm-web` exiting 1 with 2 failed and 28 passed of 30 tests — the I-1 failure was `expected 'Bearer secret-token' to be undefined` and the M-4 failure was `/health/live.v2 must not widen to /health/liveXv2: expected true to be false`; both behavioural. GREEN is exit 0 with 30 passed tests in 3 test files (1.54 s). `npm run typecheck -w apps/crm-web` and `npm run lint -w apps/crm-web` exit 0. The live path was re-verified with a loopback stub on `127.0.0.1:5080`: `GET /health/live` carrying `Authorization`, `Cookie`, `X-Tenant-Id`, `Accept` and `X-Correlation-Id` returned 200 and the stub logged `authorization: null, cookie: null, x-tenant-id: null, accept: application/json, x-correlation-id: live-correlation-1`; `GET /health/ready` returned 200 and reached the stub; `/api/contacts` and `/admin/tenants` never reached the stub. `python3 scripts/contracts/check.py` returned `SDK_GENERATED_CURRENT`; this round changed no root manifest or lockfile, so the SDK provenance was not regenerated and `git diff ffcc7ae --stat -- docs/contracts sdks/` is empty.

Binding requirement for later slices: the root `package.json` and the root `package-lock.json` are inputs to the SDK generator's configuration hash and `scripts/contracts/check.py` compares the `sdks/` tree byte for byte, so **after any change to the root manifest or lockfile `python3 scripts/contracts/generate.py` must be re-run and the regenerated `sdks/*/provenance.json` committed, or the generated check reports `SDK_GENERATED_MISMATCH`.** Task 6 adds a browser test runner and will change the lockfile, so it will hit exactly this. The requirement is recorded in `apps/crm-web/README.md` and in `docs/operations/04-LOCAL-DEVELOPMENT.md`.

The frontend developer workflow is now in `docs/operations/04-LOCAL-DEVELOPMENT.md` as well as `apps/crm-web/README.md`: pinned Node and npm, the root `npm ci`, `npm run dev -w apps/crm-web`, the strict port, the two-path proxy allowlist, the credential stripping, the verification commands, the static build and preview limitations, and the provenance regeneration requirement.

Deferred by instruction and still open: `WEB_HEALTH_UNAVAILABLE` as a fourth unlisted local code, the missing 5 second deadline and transport-failure classification, the missing success-body validation, `changeOrigin: false` forwarding the browser `Host` header to the upstream, the weak assertions, the strict-port test binding the real 5173, and the declared `@crm-centric/operations-client` dependency never being the imported artifact. The `Host` header still reaches the upstream — the live stub logged `host: localhost:5173` — which is the current `changeOrigin: false` behaviour and is unchanged in this round.

Task 6, Task 7, the browser tests, the exact-SHA clean export, Wave 0/P0 and remote acceptance remain open. No merge, push or deploy authorized.
## Task 6 — health UI, cancellation and browser behaviour (pending scoped review)

Task 5 fix round 1 is committed. Task 6 adds the operational health UI, the health adapter with its
deadline and classification, the browser end-to-end suite and the leak scan, and it closes the
findings that the Task 5 review carried forward.

**The adapter and the UI.** `apps/crm-web/src/health/healthClient.ts` now exposes
`checkHealth(signal: AbortSignal): Promise<HealthResult>` over the generated operations client. The
result is discriminated by `kind` over `ready`, `unavailable`, `timeout`, `network` and `invalid`.
The local classification codes are exactly `WEB_REQUEST_TIMEOUT`, `WEB_NETWORK_ERROR` and
`WEB_CONTRACT_INVALID`; the fourth code `WEB_HEALTH_UNAVAILABLE` is gone. One deadline of five
seconds covers the request and the body read. The success body is accepted only as
`{"status":"ready"}`; a Problem is accepted only when it matches the canonical schema, and a
malformed server correlation identifier is never displayed. A server correlation identifier is
carried only by the `unavailable` variant; the local paths carry a client correlation identifier
with the `web-` prefix, so a network error can never show a server reason or correlation.

`apps/crm-web/src/health/HealthStatus.tsx` shows the readiness state and a `Check again` action,
announces the state through `role="status"` with `aria-live="polite"`, never polls, cancels the
pending request on a repeated check and on unmount, and ignores a stale response. Nothing is
written to browser storage, a service worker or a cookie.

**The carried-forward findings.** The credential strip now happens on the incoming request, before
the proxy copies the headers to the outgoing request, so it applies to every proxied request; the
proxy request hook alone was bypassable by a request with `Expect: 100-continue`, which a browser
cannot send. The strict-port test binds an ephemeral port and closes the dev server it starts, so
it no longer collides with the browser suite. The declared `@crm-centric/operations-client`
dependency and its resolution to the generated source are documented in `apps/crm-web/README.md`.

**Changed files.** `apps/crm-web/src/health/` (the adapter, its test, the UI, its test and the
test-only fetch stub), `apps/crm-web/src/App.tsx`, `apps/crm-web/config/healthProxy.ts`,
`apps/crm-web/config/devServer.ts`, `apps/crm-web/config/devServer.test.ts`,
`apps/crm-web/playwright.config.ts`, `apps/crm-web/tests/e2e/health.spec.ts`,
`apps/crm-web/scripts/leakScan.mjs`, `apps/crm-web/package.json`, `apps/crm-web/tsconfig.json`,
`apps/crm-web/eslint.config.js`, `apps/crm-web/.gitignore`, `apps/crm-web/README.md`,
`package.json`, `package-lock.json`, the four `sdks/*/provenance.json` files,
`docs/operations/04-LOCAL-DEVELOPMENT.md` and this Plan 3 verification and handoff document. The
canonical contracts, `compose.yaml`, `scripts/local/` and the Keycloak realm were not modified.

**Dependencies.** `@playwright/test` 1.65.0, `jsdom` 31.0.0, `@testing-library/react` 16.3.3,
`@testing-library/dom` 10.4.2 and `@testing-library/user-event` 14.6.9, all pinned exactly. The
root manifest and lockfile changed, so `python3 scripts/contracts/generate.py` was re-run and the
regenerated `sdks/*/provenance.json` files are part of this change; only the
`configurationSha256` line differs in each of them.

**Task 6 verification.** RED was `npm run test -w apps/crm-web` exiting 1 with 13 failed and 32
passed of 45 tests — all behavioural, no compile error and no missing prerequisite. GREEN is exit 0
with 46 passed tests in 4 test files. Typecheck, lint and build exit 0.
`npm run test:e2e -w apps/crm-web` exits 0 with 4 passed browser tests, including one smoke test
through the development proxy to the real API and PostgreSQL of the local stack, which fails
loudly when the stack is not running. The browser is `chromium-1254` (Chrome for Testing
158.0.8127.18) from `cdn.playwright.dev`, pinned by the lockfile and not committed. The leak scan
exits 0 and was proven not to be vacuous. `python3 scripts/contracts/generate.py` exited 0 with
`SDK_GENERATED` and `python3 scripts/contracts/check.py` exited 0 with `SDK_GENERATED_CURRENT`. The
local stack was stopped again and no listener was left on 5080, 5173 or 8080.

**Known limitations.** The browser smoke proves the same-origin path to the real local stack on this
host; it does not prove production hosting, remote CI, tenant or user readiness, business
endpoints or the identity provider callback. The readiness state covers the database and the schema
only. The client correlation identifier falls back to a non-cryptographic source when
`crypto.randomUUID` is unavailable, and the Problem boundary ignores unknown properties. The
browser needs system libraries this host does not install; they were extracted into a temporary
directory for the run and nothing was added to the repository.

Task 7, the exact-SHA clean export, Wave 0/P0 and remote acceptance remain open. No merge, push or
deploy is authorized.
## Task 6 fix round 1 — enforced deadline, stack provenance, markup scan and correlation fallback (pending scoped review)

The independent review of `fecac28` found the implementation correct but four requirements that
were not enforced by the suite. This round closes them without changing the behaviour of the
application.

**I-1 — the five second deadline is enforced.** The unit suite pins the literal
(`expect(HEALTH_DEADLINE_MS).toBe(5_000)`) and the browser timeout test measures the elapsed time
until the deadline fires and requires it to be greater than four and smaller than nine seconds, so
a too short and a too long deadline both fail. RED evidence: with the constant temporarily
re-valued to `500` the suite failed with `expected 500 to be 5000` (1 failed, 53 passed); the
constant was restored afterwards.

**I-2 — the smoke test identifies the container of the stack.** The new
`apps/crm-web/tests/e2e/stackProvenance.ts` resolves the compose project of the checkout the same
way `scripts/local/compose.py` does and reads the API container through the compose labels
`com.docker.compose.project` and `com.docker.compose.service=crm-api`, additionally requiring the
compose container name `<project>-crm-api-<index>`. A foreign container that publishes the API port
is rejected. The new unit file `apps/crm-web/tests/e2e/stackProvenance.test.ts` covers the
rejection cases; RED evidence: with the pre-fix selection three cases failed. A live impersonation
experiment with a foreign container publishing `127.0.0.1:5080->5432/tcp` confirmed that the
pre-fix port substring matched while the new check rejects it. The Vitest include pattern was
extended to `tests/**/*.test.ts` and the Playwright `testMatch` narrowed to `**/*.spec.ts`, so the
two runners never claim the same file.

**M-1 — the safety checks read the rendered markup.** The leak scan scans `page.content()` and the
markup of the application root, the browser test asserts on the same. RED evidence: with the raw
payload in a `title` attribute the previous text-only scan exited 0, the new scan reported ten
findings by name and exited 1.

**M-2 — every closed error path carries a correlation identifier.** The `unavailable` variant now
always carries a client correlation identifier; a valid server correlation identifier is still shown
as `Server correlation:` and a malformed one is never displayed, so no server identifier is
fabricated.

**M-3 — the OS-library prerequisite is documented** in `apps/crm-web/README.md` and
`docs/operations/04-LOCAL-DEVELOPMENT.md`, including `npx playwright install-deps chromium` and the
equivalent `LD_LIBRARY_PATH` workaround. It is a local prerequisite of the browser only and was not
added to the build, the test scripts or any continuous integration path.

Changed files: `apps/crm-web/src/health/healthClient.ts`,
`apps/crm-web/src/health/healthClient.test.ts`, `apps/crm-web/src/health/HealthStatus.tsx`,
`apps/crm-web/src/health/HealthStatus.test.tsx`, `apps/crm-web/tests/e2e/health.spec.ts`,
`apps/crm-web/tests/e2e/stackProvenance.ts`, `apps/crm-web/tests/e2e/stackProvenance.test.ts`,
`apps/crm-web/playwright.config.ts`, `apps/crm-web/vitest.config.ts`,
`apps/crm-web/scripts/leakScan.mjs`, `apps/crm-web/README.md`,
`docs/operations/04-LOCAL-DEVELOPMENT.md`, the Plan 3 verification document and this handoff. The
root manifest and lockfile did not change, so the SDK provenance was not regenerated and
`python3 scripts/contracts/check.py` still returns `SDK_GENERATED_CURRENT`.

Fix-round verification: `npm run test -w apps/crm-web` exited 0 with 54 passed of 54 tests in 5
test files (7.11 s); `npm run typecheck -w apps/crm-web` exited 0 (12.41 s); `npm run lint -w
apps/crm-web` exited 0 (2.36 s); `npm run build -w apps/crm-web` exited 0 (1.24 s);
`npm run test:e2e -w apps/crm-web` exited 0 with 4 passed (9.1 s) against the running stack;
`npm run scan:leaks -w apps/crm-web` exited 0. The stack was stopped again and nothing listened on
5080, 5173 or 8080.

Still deferred by instruction: the `StrictMode` double request in development, the "no persistence"
test that covers the adapter but not the rendered component, and the message of the
stack-provenance check for a missing `docker` binary.

Task 7, the exact-SHA clean export, Wave 0/P0 and remote acceptance remain open. No merge, push or
deploy is authorized.
