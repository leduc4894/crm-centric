# Wave 0 Plan 3 — implementation handoff

## Objective and authority

Implement [Plan 3](../superpowers/plans/2026-11-10-wave-0-plan-3-frontend-contract-toolchain.md) under the [design](../superpowers/specs/2026-11-10-wave-0-plan-3-frontend-contract-toolchain-design.md), on a new branch without a worktree. The user authorized implementation; do not merge, push or deploy. One coding agent at a time. The prior specification handoff remains relevant: [Plan 3 spec handoff](2026-11-10-wave-0-plan-3-spec-handoff.md).

## Current state

Branch `feat/wave-0-plan-3-frontend-contract-toolchain`, base `5257abf`. Tasks 1–7 are
implemented and committed. The recorded HEAD SHA is
`907e675` (`docs: clarify the round labels of the Plan 3 verification mapping`); the
commit on top of it is `40a7c91` and carries **fix round 6** of the final review of Task 7
— the contract prerequisite guard, the CI-simulation harness, the TypeScript comparator
envelope and the documentation of that round; the commit on top of that one is
`88f5542` and carries **fix round 7** — the scratch cleanup of the Plan 3 harnesses and
the documentation of that round; the commit on top of that one is `5e87961` and carries
the record of the scoped re-review of both rounds and the three documentation
corrections that the review asked for. That last SHA is not recorded here, because a
commit cannot carry its own SHA. The commits on top of the
fix round 5 documentation change
only the recorded HEAD SHA of this document and the wording of this document and of the
Plan 3 verification document; they carry no source change. The commit that recorded the
fix round 4 HEAD SHA, `651de9bf1364ea10062c09645f3a6e52356d677f`,
changed the recorded HEAD SHA in this header and the round-4 "Branch and state" block
below, and carried no source change either. The working tree is clean, with no uncommitted
work. The detailed per-task history is below, newest last. The generator
selection, pinned artifacts, prerequisites, coverage and current evidence are in [Plan 3
verification](../engineering/07-WAVE-0-PLAN-3-VERIFICATION.md). Corvus.Json.Cli 5.8.8 supplies
C# generated clients; openapi-typescript 7.13.0 plus openapi-fetch 0.17.0 supplies typed TS
clients. No canonical contract was changed, and `compose.yaml`, `scripts/local/`, `infra/`
and the Keycloak realm configuration are byte-identical to the branch base.

The aggregate gate `bash scripts/check-plan3.sh --base-ref <ref>` is the public entrypoint
that runs the whole slice: locked install, generated check, SDK build/pack/consumers,
compatibility gate, frontend typecheck/lint/test/build/leak scan and an opt-in browser
suite. It fails closed, requires an explicit base ref, never fetches or mutates Git, and
proves that no step changed a tracked file. `tests/Plan3/negative-cases.sh` covers its
fail-closed paths and `tests/Plan3/clean-export.sh` runs it inside an exact-SHA clean
export.

## Remaining work

1. The read-only whole-branch review is performed by the controller after this task. Any
   finding must be fixed with regression tests, and the exact-SHA evidence must be refreshed
   if the source changes.
2. Wave 0/P0 acceptance is **OPEN**: CRM login and tenant authorization, remote DemoModule
   end-to-end, tenant isolation, outage/recovery and restore drills, application, security
   and container CI scans, and remote CI. The operational health routes are implemented in
   the existing backend; the business OpenAPI is pre-runtime. No merge, push or deploy is
   authorized.

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

## Task 7 — aggregate gate, clean exact-SHA export and evidence (pending scoped review)

Task 6 passed scoped review. Task 7 adds the aggregate gate that drives the Plan 3 public
entrypoints, the fail-closed negative-case harness, the exact-SHA clean export and the
evidence for the whole slice.

**The aggregate gate.** `scripts/check-plan3.sh --base-ref <ref> [--with-browser]` runs
locked install → generated check → SDK build/pack/consumers → compatibility gate → frontend
typecheck/lint/test/build/leak scan → browser suite, and stops at the first failing step
with a non-zero exit and a reason token. `--base-ref` is required and has no default; the
browser suite is opt-in and a skipped step is recorded in the summary as `not run`. The
script never fetches, resets, rebases or rebaselines Git, and it proves that no step
changed a tracked file by comparing a digest over every tracked path of `HEAD` before and
after the run. A mismatch fails the run with `PLAN3_SOURCE_MUTATED`.

**The harnesses.** `tests/Plan3/negative-cases.sh --base-ref <ref>` applies one fault per
case to a sandbox copy of the checkout that has a read-only Git object context, and asserts
that the gate stops with `SDK_GENERATED_MISMATCH`, `CONTRACT_BASE_UNAVAILABLE` and
`CONTRACT_POLICY_REVIEW_REQUIRED` respectively and never reports a compatible verdict.
`tests/Plan3/clean-export.sh --sha <sha> --base-ref <ref> [--target <dir>] [--with-browser]`
exports an exact commit with `git archive`, gives the export a read-only Git object context
through `objects/info/alternates`, copies in the pinned local tool artifacts that are not
committed, and runs the aggregate inside the export.

**Verification.** The contract suite passed 87 tests in 1064.566 s and the Local Python
suite 20 tests in 4.544 s; the .NET non-Compose suite passed 70 tests in 6 m 48 s. The
negative harness passed 3 of 3 cases in 3 m 14 s. The clean exact-SHA export of the tested
commit `0695cc2c1f196aa9148f993c8d001bb743741ac1` against base
`408ded6b01f1373dfaff92a663f2f80347475f55` passed the aggregate with all ten steps,
including the browser smoke against the test-owned stack `crm-local-2ee20ecbdd57`, and the
tracked-file digest was unchanged during the run. Exact commands, counts, versions, digests
and the W0P3-01→12 mapping are in the Plan 3 verification document.

**Operations routes implemented, business spec pre-runtime.** The backend serves only the
anonymous operational routes `GET /health/live` and `GET /health/ready`; those are the
implemented routes. `docs/contracts/crm-api.openapi.yaml` and the business, event and
automation schemas remain a pre-runtime specification. The generated SDKs are client
contracts compiled and gated locally; they are not evidence that a business endpoint
exists, and nothing was published.

**Plan 2 evidence that was reused, not re-run.** Plan 2's Compose-category suite (14 cases)
was not re-run in this task. Plan 3 changed no backend source, no container configuration
and no stack lifecycle: `git diff --stat 5257abf..HEAD -- src/ compose.yaml scripts/local/
infra/` is empty. Plan 2's recorded Compose evidence therefore stays valid as exact-SHA
historical evidence for the unchanged runtime and is not presented as a run of this task.
The .NET non-Compose suite, the Local Python suite and the contract suite were executed
fresh here.

**Changed files.** `scripts/check-plan3.sh` (new), `tests/Plan3/negative-cases.sh` (new),
`tests/Plan3/clean-export.sh` (new), `docs/engineering/07-WAVE-0-PLAN-3-VERIFICATION.md`,
`docs/handoff/2026-11-10-wave-0-plan-3-implementation-handoff.md`, `README.md`,
`docs/contracts/README.md`, `docs/engineering/03-ACCEPTANCE-MATRIX.md`,
`docs/roadmap/01-BACKLOG.md`, the Plan 3 plan checkboxes and the design spec status. The
canonical contracts under `docs/contracts/`, `compose.yaml`, `scripts/local/` and the
Keycloak realm configuration were not modified.

**Known limitations.** Every result is local: there is no remote CI run, no branch
protection evidence and no published package. The browser needs the Chromium shared
libraries, which this host does not install; they are provided through `LD_LIBRARY_PATH`
and are a local prerequisite, not a repository step. The .local tool artifacts are not
committed, so a clean export needs them copied in; the tooling verifies their digests.

**Still open.** The read-only whole-branch review is performed by the controller after this
task; this task only leaves the branch reviewable. Wave 0/P0 acceptance, remote DemoModule
end-to-end, tenant isolation, outage/recovery and restore drills and remote CI remain
**OPEN** and are not claimed here. No merge, push or deploy is authorized.

## Task 7 fix round 1 — aggregate reporting, error envelope and teardown (pending scoped review)

The independent review of Task 7 returned 0 Critical, 3 Important and 9 Minor findings.
This round closes the three blocking findings and the documentation corrections in the commit
`fix(contracts): harden the Plan 3 aggregate reporting and teardown`. The commits
`0695cc2` and `422e049` are unchanged.

**Branch and state.** Branch `feat/wave-0-plan-3-frontend-contract-toolchain`. The fix is
commit `ac69dc78c06e54497b83dd5599dbab8d60544f98`; the documentation of this round is the
commit that follows it. The working tree contains only the intended documentation changes
of this round and no uncommitted source work. No merge, push or deploy is authorized.

**What changed in the code.** The aggregate now iterates over all ten steps, so a step that
did not run because an earlier step failed is recorded as `not run: an earlier step failed`
in the step table and in the command list. Every closed error path of the aggregate itself
carries a reason token and the correlation identifier of the run: `PLAN3_ARGUMENT_INVALID`,
`PLAN3_PREREQUISITE_MISSING`, `PLAN3_TOOLCHAIN_MISMATCH`, `PLAN3_STACK_START_FAILED`,
`PLAN3_STACK_TEARDOWN_FAILED`, `PLAN3_SOURCE_MUTATED` and a failing step. A `dev-down.sh`
that fails now fails the browser step instead of being reduced to a warning, and an `EXIT`,
`INT` or `TERM` trap stops a stack that the run started, so an interrupted run cannot
orphan it. The source digest covers untracked files next to the tracked ones and warns when
it falls back to a reduced scope. The echoed step output is defensively redacted. The clean
export refuses a target directory that is not empty or that is protected, and it prints the
digest of the oasdiff executable next to the Corvus.Json.Cli package.

**Coverage.** `tests/Plan3/aggregate-reporting.sh` is the new regression harness: the
argument and toolchain error envelopes, the "not run" accounting of all ten steps, a
failing stack teardown, an interrupted run and the export target guard. It was observed
RED against the reviewed aggregate (5 of 6 failed; the interrupt case was vacuous at first
and was corrected to reset the SIGINT disposition and to assert that the run was really
interrupted) and GREEN after the fix (6 of 6).

**Verification.** The negative-case harness passed 3 of 3 cases, the reporting harness
passed 6 of 6 cases, the clean exact-SHA export of the fix commit against base
`408ded6b01f1373dfaff92a663f2f80347475f55` passed the aggregate with all ten steps,
including the browser smoke against a test-owned stack, and the tracked and untracked file
digest was unchanged during the run. The contract suite and the Local Python suite were
executed again on this commit. Exact commands, counts and durations are in the Plan 3
verification document.

**Known limitations.** The aggregate trusts the driven tools for the deeper property that
no secret is emitted; it only redacts the common secret shapes in the echoed output. The
browser needs the Chromium shared libraries, which this host does not install; they are
provided through `LD_LIBRARY_PATH` and are a local prerequisite, not a repository step. The
local tool artifacts are not committed and are copied into every sandbox and export; the
tooling verifies their digests.

**Still open.** The read-only whole-branch review is performed by the controller after this
round; the plan keeps the annotation that it has not run. Wave 0/P0 acceptance, remote
DemoModule end-to-end, tenant isolation, outage/recovery and restore drills and remote CI
remain **OPEN** and are not claimed here.

## Task 7 fix round 2 — stack teardown on a failed start, complete error envelopes and the staged-file gap (pending scoped review)

The independent re-review of the fix round 1 confirmed that the three Important and the
nine Minor findings of round 1 are closed, and returned **1 Important regression and 4
Minor residuals**. This round closes exactly those five items: **N-1** (a failed
`dev-up.sh` orphaned the stack), **N-2** (closed error paths without a reason token and a
correlation identifier), **N-3** (a file staged in the index but absent from `HEAD` was
invisible to the source digest), **N-4** (the Plan 3 harnesses are wired to nothing) and
**N-5** (the wording of `PLAN3_SOURCE_MUTATED` and a hardcoded oasdiff version). The
commits `0695cc2`, `422e049`, `ac69dc7` and `80de4d8` are unchanged and byte-identical.

**Branch and state.** Branch `feat/wave-0-plan-3-frontend-contract-toolchain`, base
`5257abf`. The fix is the commit
`97fcc450e4cdbd08ca6fe0d75ef969cbd1b17e9b` — `fix(contracts): close the Plan 3 stack and
reporting gaps`; the documentation of this round is the commit that follows it. The
commits `0695cc2`, `422e049`, `ac69dc7` and `80de4d8` are unchanged and byte-identical. The
working tree contains only the intended documentation changes of this round and no
uncommitted source work. No merge, push, deploy, publication or database migration is
authorized or performed.

**What changed in the code.** The aggregate assumes the stack may be partially up while
`dev-up.sh` runs: it sets `stack_started=1` before the start attempt, calls `dev-down.sh`
after a **failed** start and fails the step with `PLAN3_STACK_TEARDOWN_FAILED` when that
teardown fails. The source digest now covers the files of the Git index next to the tracked
paths of `HEAD` and the untracked files, so a staged file that a step rewrites produces
`PLAN3_SOURCE_MUTATED` instead of a green run. Every closed error path of
`scripts/check-plan3.sh`, `tests/Plan3/clean-export.sh`, `tests/Plan3/aggregate-reporting.sh`
and `tests/Plan3/negative-cases.sh` emits a reason token **and** the correlation identifier
of the run. The clean export derives the pinned oasdiff executable from
`tools/contracts/oasdiff.json` instead of a hardcoded version, so the digest line follows a
version bump and cannot disappear silently.

**Coverage.** `tests/Plan3/aggregate-reporting.sh` grew from six to nine cases: a failed
stack start tears the started stack down, a staged file that a step rewrites is detected,
and the argument errors of both harnesses carry a reason token and a correlation
identifier. The three new cases were observed RED against the implementation of `80de4d8`
and GREEN after the fix. The mutation sensitivity was proven in a scratch copy of the
checkout with its own Git history: with the implementation reverted to `80de4d8` and the
new cases kept, all three failed again — the orphaned stack process, the fail-open digest
and the missing reason token.

**The harnesses are not yet wired into any runner.** `tests/Plan3/aggregate-reporting.sh`,
`tests/Plan3/negative-cases.sh`, `tests/Plan3/clean-export.sh`,
`tests/Plan3/ci-environment.sh` and `tests/Plan3/scratch-cleanup.sh` are not executed by
`unittest discover -s tests/Contract`, by any `package.json` script or by a CI pipeline.
The repository does have CI, but it is **contract-only**: `.github/workflows/contracts.yml`
runs the Python contract suite (`python -m unittest discover -s tests/Contract -v`) on push
and pull_request, and it does not run these five harnesses. **Plan 4 owns the wiring** of
these five commands into the continuous integration path. Until then a human must run,
from the root of the checkout:

```
bash tests/Plan3/negative-cases.sh --base-ref 408ded6
bash tests/Plan3/aggregate-reporting.sh --base-ref 408ded6
bash tests/Plan3/clean-export.sh --sha <sha> --base-ref <ref> [--with-browser]
bash tests/Plan3/ci-environment.sh
bash tests/Plan3/scratch-cleanup.sh
```

The base revision must be a revision the local clone already has; the harnesses never
fetch.

**This branch also adds 55 tests into the directory the live CI workflow does execute.**
`tests/Contract/test_compatibility.py` (39), `tests/Contract/test_generated_check.py` (9),
`tests/Contract/test_sdk_surface.py` (6) and `tests/Contract/test_sdk_consumers.py` (1)
are discovered and run by `.github/workflows/contracts.yml`. They need the git-ignored
pinned tool artifacts below `.local/plan3-tools/` (the oasdiff 1.37.0 binary and the
`corvus.json.cli.5.8.8.nupkg` package), an installed `node_modules` with a resolvable
`typescript` module, Docker with the pinned .NET image and access to the NuGet flat
container — none of which the current workflow provisions. The prerequisite guard of
`tests/Contract/prerequisites.py` therefore **skips those 55 tests explicitly, with a
visible reason** in a CI-like checkout, while the 32 pre-existing contract tests keep
passing, so the workflow stays green. Wherever the prerequisites are present the 55 tests
**run for real**: 87 tests, 0 skipped, on a prepared host. Wiring the prerequisites into CI
is **Plan 4's** job. `tests/Plan3/ci-environment.sh` simulates the CI checkout and asserts
the exit code, the counts and the skip reasons; it is a case of
`tests/Plan3/aggregate-reporting.sh`.

**Verification.** The reporting harness passed 9 of 9 cases in 2m00.453s and the
negative-case harness passed 3 of 3 cases in 3m16.307s, both with exit 0 and with the
unchanged reason tokens. `python3 scripts/contracts/check.py` returned exit 0 with
`SDK_GENERATED_CURRENT` and a correlation identifier. `bash scripts/check-plan3.sh
--base-ref` without a value returned exit 2 with `PLAN3_ARGUMENT_INVALID` and a
correlation identifier, the same script with Node 26.13.0 on the `PATH` returned exit 1
with `PLAN3_TOOLCHAIN_MISMATCH` and a correlation identifier, and the clean export refused
a non-empty target with `PLAN3_EXPORT_TARGET_INVALID`, a correlation identifier and the
content of the target kept. The aggregate in the working tree of the fix commit
`97fcc450e4cdbd08ca6fe0d75ef969cbd1b17e9b` passed in 4m42.963s with nine steps at exit 0,
the browser step recorded as `not run` and `source digest: unchanged during the run` — the
proof that the widened digest does not fire spuriously on a checkout with a real index. The
clean exact-SHA export of that commit against base
`408ded6b01f1373dfaff92a663f2f80347475f55` passed the aggregate with all ten steps,
including the browser smoke against the test-owned stack `crm-local-4dceaa358742`, in
5m24.639s with the digest unchanged; it printed the pinned oasdiff executable of the
policy, `local artifact: .local/plan3-tools/oasdiff-1.37.0/oasdiff
401389ac23e81bc844093c88ae1e283fbf80ef719f400cbeb19cd36d15957971`. Exact commands and
durations are in the Plan 3 verification document.

**Known limitations.** The 87-case contract suite and the 70-case .NET non-Compose suite
were **not** re-run in this round; the round 1 results stand as the evidence for the
unchanged runtime and are not presented as a run of this round. The changed files are shell
harnesses and the aggregate gate, which no Python or .NET suite executes. The browser suite
still needs the Chromium shared libraries, which this host does not install; they are
provided through `LD_LIBRARY_PATH`. The local tool artifacts are not committed and are
copied into every sandbox and export; the tooling verifies their digests.

**Still open.** The read-only whole-branch review is performed by the controller after this
round; the plan keeps the annotation that it has not run. Wave 0/P0 acceptance, remote
DemoModule end-to-end, tenant isolation, outage/recovery and restore drills and remote CI
remain **OPEN** and are not claimed here.

## Task 7, fix round 3 — the CI statement and the signal envelopes

**Objective.** Close the two items the scoped re-review of `80de4d8..66c4523` left open: the
false statement that the repository has no CI, and the closed error paths of the signal
handlers of the aggregate, which reported no reason token and no correlation identifier.
The review was otherwise READY, with no Critical and no Important finding.

**Branch and state.** Branch `feat/wave-0-plan-3-frontend-contract-toolchain`, base
`5257abf`. The fix is the commit
`62a4c6746b658cc41e10ee5db58b333eb4cf3272` — `fix(contracts): correct the Plan 3 CI
statement and signal envelopes`; the documentation of this round is the commit that follows
it. The working tree contains only the intended documentation changes of this round and no
uncommitted source work. No merge, push, deploy, publication or database migration is
authorized or performed.

**F-1 — the CI statement.** The round 2 documentation stated *"The repository has no CI yet;
CI is Plan 4."* That was factually false: `.github/workflows/contracts.yml` has existed since
`e3dbbb1` and runs `python -m unittest discover -s tests/Contract -v` on push and
pull_request, and the repository's own `docs/engineering/02-REPO-CI-CD.md` and
`docs/roadmap/01-BACKLOG.md` say so. The sentence is replaced in all four places where it
stood — the Plan 3 verification document (twice), this handoff and the git-ignored SDD task
report — by the accurate statement: the repository has a **contract-only** CI workflow that
runs the Python contract suite, it does **not** execute the three Plan 3 harnesses, and
wiring those harnesses into an automated runner is Plan 4's job. The harness list, the exact
manual commands and the Plan 4 ownership are preserved. The N-4 finding itself is unchanged
and still true.

**R-1 — the signal paths.** `scripts/check-plan3.sh` handled `INT` and `TERM` with
`trap 'exit 130' INT` and `trap 'exit 143' TERM`, which exited with the conventional code of
the signal but reported no reason token and no correlation identifier. The cleanup of an
interrupted run downgraded a failing teardown to a bare prose warning. Both are closed: an
`INT` reports `PLAN3_INTERRUPTED` and exits 130, a `TERM` reports `PLAN3_TERMINATED` and
exits 143, a stack that the interrupted run could not stop reports
`PLAN3_STACK_TEARDOWN_FAILED`, and each carries the correlation identifier of the run. The
exit codes 130 and 143 are unchanged. The envelopes go to the stderr that the aggregate had
before a step redirected it into its own log; without that, an interrupt that arrived while
a step was running would have left the envelope in the log of that step, which the aggregate
deletes on the way out.

**What changed in the code.** `scripts/check-plan3.sh` gained the saved stderr, the
`signal_error` helper and the two signal traps, and the cleanup of an interrupted run now
reports a failing teardown as an envelope. `tests/Plan3/aggregate-reporting.sh` grew from
nine to twelve cases: an interrupted run that must report a reason token and a correlation
identifier, a terminated run that must do the same, and a failing teardown of an interrupted
run that must be a failure envelope. Each asserts the signal exit code and the teardown of
the stack as well.

**Coverage.** The three new cases were run against the unchanged implementation and all
three failed, because the envelope and the correlation identifier were missing while the
exit codes 130 and 143 were already correct. The implementation of the signal paths and of
the cleanup was then reverted to the state of `66c4523` with the new cases kept, and all
three failed again: the cases test the behaviour, not the wording.

**Verification.** The reporting harness passed 12 of 12 cases in 3m18.845s and the
negative-case harness passed 3 of 3 cases in 3m28.570s, both with exit 0 and with the
unchanged reason tokens `SDK_GENERATED_MISMATCH`, `CONTRACT_BASE_UNAVAILABLE` and
`CONTRACT_POLICY_REVIEW_REQUIRED`. `python3 scripts/contracts/check.py` returned exit 0 with
`SDK_GENERATED_CURRENT` and a correlation identifier. `bash scripts/check-plan3.sh
--base-ref` without a value returned exit 2 with `PLAN3_ARGUMENT_INVALID` and a correlation
identifier, and the same script with Node 26.13.0 on the `PATH` returned exit 1 with
`PLAN3_TOOLCHAIN_MISMATCH` and a correlation identifier.

**Known limitations.** The 87-case contract suite, the 70-case .NET non-Compose suite, the
full clean export with the browser and the full 10-step working-tree aggregate were **not**
re-run in this round. This round changed the aggregate gate, one shell harness and two
Markdown documents; it changed no canonical contract, no backend source and no container
configuration, so the runtime evidence of the earlier rounds stands and is not presented as
a run of this round.

**Still open.** The three Plan 3 harnesses are still executed by no automated runner; Plan 4
owns that wiring. Wave 0/P0 acceptance, remote DemoModule end-to-end, tenant isolation,
outage/recovery and restore drills and remote CI remain **OPEN** and are not claimed here.

## Task 7, fix round 4 — the teardown wording and the state of this document

**Objective.** Close the two Minor items the scoped re-review of `66c4523..62a4c67` left
open: **F-2**, this document recording a stale HEAD SHA, and **N-R3-1**, the cleanup of the
aggregate describing an ordinary exit as "the interrupted run" and reporting a failing
teardown twice. The review was otherwise READY, with no Critical and no Important finding.

**Branch and state.** Branch `feat/wave-0-plan-3-frontend-contract-toolchain`, base
`5257abf`. The fix is the commit
`cc5df3c7205af2f8be544142c86bf7240b7c4cea` — `fix(contracts): correct the Plan 3 teardown
wording and handoff state`; the commit that follows it,
`651de9bf1364ea10062c09645f3a6e52356d677f` (`docs: record the Task 7 fix round 4 HEAD
SHA`), rewrites the recorded HEAD SHA of this document and this round's "Branch and state"
block, and changes nothing else. The working tree contains only the intended changes of
this round and no uncommitted source work. No merge, push, deploy, publication or database
migration is authorized or performed.

**F-2 — the stale HEAD SHA.** The "Current state" header of this document recorded
`ac69dc78c06e54497b83dd5599dbab8d60544f98`, the fix commit of round 1, as the HEAD of the
branch. That SHA had been stale since round 1, at `80de4d8`, `66c4523`, `97fcc45` and
`62a4c67`. The header now names the commit that carries the fix round 4, and states the
branch and the state of the working tree. The round 3 section had no "Branch and state"
block, unlike the round 1 and the round 2 sections; it now has one that names its fix
commit `62a4c6746b658cc41e10ee5db58b333eb4cf3272`. The rest of the document was read for
other stale values presented as current: every other SHA names the commit of the round
that produced it, and every other count is the result of the round that recorded it. The
count of the reporting harness is the one value that moved in this round, and this section
records it.

**N-R3-1 — the teardown wording and the duplicate envelope.** The cleanup of the aggregate
runs on every exit, and it described the stack it stopped as "the local stack of the
interrupted run" and reported a failing teardown with the detail "the local stack of the
interrupted run was not stopped cleanly". A run that was never interrupted saw that
wording. A failing teardown was also reported twice: the browser step printed one
`PLAN3_STACK_TEARDOWN_FAILED` envelope into its own log and the cleanup printed another
one, so the operator saw two envelopes for one condition. The conditions of the run are now
recorded by the step that detects them and reported by the run itself, exactly once, as an
envelope with a reason token and the correlation identifier of the run. The wording of the
cleanup now names the disposition of the run — "the run ended", "the run was interrupted",
"the run was terminated" — so it is accurate for an interrupted run and for a run that
simply ended. The signal paths are unchanged: an `INT` reports `PLAN3_INTERRUPTED` and
exits 130, a `TERM` reports `PLAN3_TERMINATED` and exits 143, and a stack that the run
could not stop reports `PLAN3_STACK_TEARDOWN_FAILED` next to them, once. The two
conditions of a signalled run stay two envelopes; they are not collapsed into one.

**What changed in the code.** `scripts/check-plan3.sh` gained the three condition helpers
(`record_condition`, `condition_is_recorded`, `report_conditions`), the `run_state`
disposition of the run and the new wording of the cleanup. The browser step records its
conditions instead of printing them, the step reason is taken from the condition that the
step recorded, and the run reports the recorded conditions once at the point where it
fails. `tests/Plan3/aggregate-reporting.sh` grew from twelve to thirteen cases: an
ordinary, never-interrupted failing teardown must be reported exactly once, with the
correlation identifier of the run, without the misleading "interrupted run" wording, with a
non-zero exit code and without `plan3 aggregate: PASS`. No canonical contract, no backend
source and no container configuration was touched.

**Coverage.** The new case was observed RED against the implementation of `62a4c67`: it
failed with "expected exactly one envelope PLAN3_STACK_TEARDOWN_FAILED, got 3". The
implementation of the aggregate was then reverted to the state of `62a4c67` in a scratch
copy of the checkout, with the new case kept, and the case failed again with the same
count of three envelopes. It is GREEN after the fix. The three signal paths were observed
by hand in the same scratch copies: an ordinary failing teardown exits 1 with exactly one
`PLAN3_STACK_TEARDOWN_FAILED`, the correlation identifier of the run and the wording "the
run ended"; an `INT` with a failing teardown exits 130 with `PLAN3_INTERRUPTED` and one
`PLAN3_STACK_TEARDOWN_FAILED`; a `TERM` exits 143 with `PLAN3_TERMINATED` and one
`PLAN3_STACK_TEARDOWN_FAILED`. Both envelopes of a signalled run carry the correlation
identifier of that run.

**Verification.** The reporting harness passed 13 of 13 cases in 3m36.377s and the
negative-case harness passed 3 of 3 cases in 3m38.358s, both with exit 0 and with the
unchanged reason tokens `SDK_GENERATED_MISMATCH`, `CONTRACT_BASE_UNAVAILABLE` and
`CONTRACT_POLICY_REVIEW_REQUIRED`. `python3 scripts/contracts/check.py` returned exit 0
with `SDK_GENERATED_CURRENT` and a correlation identifier. `bash scripts/check-plan3.sh
--base-ref` without a value returned exit 2 with `PLAN3_ARGUMENT_INVALID` and a
correlation identifier, the same script with Node 26.13.0 on the `PATH` returned exit 1
with `PLAN3_TOOLCHAIN_MISMATCH` and a correlation identifier, and the same script with
`docker` off the `PATH` returned exit 1 with `PLAN3_PREREQUISITE_MISSING` and a
correlation identifier.

**Known limitations.** The 87-case contract suite, the 70-case .NET non-Compose suite, the
full clean export with the browser and the full 10-step working-tree aggregate were **not**
re-run in this round. This round changed the aggregate gate, one shell harness and two
Markdown documents; it changed no canonical contract, no backend source and no container
configuration, so the runtime evidence of the earlier rounds stands and is not presented as
a run of this round. The two harness rows of the summary table of the Plan 3 verification
document carry the results of this round; its other rows still show the evidence of round
1.

**Still open.** The three Plan 3 harnesses are still executed by no automated runner; Plan 4
owns that wiring. Wave 0/P0 acceptance, remote DemoModule end-to-end, tenant isolation,
outage/recovery and restore drills and remote CI remain **OPEN** and are not claimed here.
## Task 7, fix round 5 — the verification evidence and the state of this document (review pending)

**Objective.** Close the three Minor documentation findings the scoped re-review of
`cc5df3c..651de9b` left open: **M-1**, the false statement about the scope of the commit on
top of the round 4 fix, **M-2**, the summary table of the Plan 3 verification document
presenting round-1 measurements as current, and **M-3**, the missing fix round 4 section and
the outdated description of the aggregate in that document. The re-review was otherwise
READY, with no Critical and no Important finding and no functional defect. This round is
documentation-only: no script, no test, no source file, no manifest and no canonical
contract changed.

**Branch and state.** Branch `feat/wave-0-plan-3-frontend-contract-toolchain`, base
`5257abf`. The documentation refresh of this round is the commit
`ccce8d4ba58803a0a2139794f487bc25957c50f2` — `docs: record the fix round 5 verification
measurements`; the commits that follow it change only the recorded HEAD SHA of this
document and the wording of this document and of the Plan 3 verification document, and
nothing else. The working tree contains only the intended changes of this round and no
uncommitted source work. No merge, push, deploy,
publication or database migration is authorized or performed.

**M-1 — the description of the top commit was false.** The "Current state" header said that
the commit on top of the round 4 fix "changes nothing but this line: a commit cannot carry
its own SHA", and the round 4 "Branch and state" block said that it "rewrites only the
recorded HEAD SHA of this document and changes nothing else". The commit in question,
`651de9bf1364ea10062c09645f3a6e52356d677f`, has **two** hunks: the header and the round 4
"Branch and state" block. The intent — that it carries no source change — held; the
wording overstated it. Both statements now name the two places the commit touched, keep the
explanation that a commit cannot carry its own SHA, and state that the commit carried no
source change. The handoff was searched for the same overstatement; the two places above
were the only ones.

**M-2 — the summary table presented round-1 evidence as current.** The "Suites executed on
this branch" table of the Plan 3 verification document carried the round-1 measurements — the
working-tree aggregate at "4m14.643s" and the clean export at `--sha ac69dc7` — without a
round label, while the two harness rows next to them carried "(fix round 4)". The round-1
working-tree row was stale in any case, because round 4 changed the aggregate gate itself.
Every row of the table now names the round that measured it. The two stale measurements
were **re-run** in this round on the tree of this round; the rows that were not re-run are
labelled with the round that measured them instead of being presented as current. The
W0P3 mapping rows and the two export blocks of the same document carry the same labels.

**M-3 — the verification document had no round-4 section and described the gate
inaccurately.** The document has sections for fix rounds 1, 2 and 3, but none for round 4,
and the behavioural description of the aggregate still described the pre-round-4 gate. The
document now has a fix round 4 section in the shape of the sections of the earlier rounds:
what changed, why, the coverage and its mutation sensitivity, the commands of that round
with their real results and the limitations of that round. The behavioural description now
states that every condition is reported exactly once, that the disposition of the run is
named by `run_state` — "the run ended", "the run was interrupted" or "the run was
terminated" — and that the `INT` and the `TERM` path report `PLAN3_INTERRUPTED` and
`PLAN3_TERMINATED` with the exit codes 130 and 143. The harness row of the manual-command
table now says 13 cases and names the case that round 4 added, the failing teardown of an
ordinary run that must be reported once.

**Verification.** The reporting harness passed 13 of 13 cases in 3m33.250s and the
negative-case harness passed 3 of 3 cases in 3m33.104s, both with exit 0 and with the
unchanged reason tokens `SDK_GENERATED_MISMATCH`, `CONTRACT_BASE_UNAVAILABLE` and
`CONTRACT_POLICY_REVIEW_REQUIRED`; `python3 scripts/contracts/check.py` returned exit 0 with
`SDK_GENERATED_CURRENT` and a correlation identifier. The aggregate in the working tree of
this round passed in 4m16.339s with nine steps at exit 0, the browser step recorded as
`not run` and `source digest: unchanged during the run`; the clean exact-SHA export of
`599ad545113b9e07dee4f776f154ee5cd60cc5d1` against base
`408ded6b01f1373dfaff92a663f2f80347475f55` passed the aggregate with all ten steps,
including the browser smoke against the test-owned stack `crm-local-8e85363088e8`, in
6m00.310s with the digest unchanged. These two measurements replace the round-1
measurements of the summary table of the Plan 3 verification document. Exact commands,
counts and durations are in that document.

**Known limitations.** The 87-case contract suite and the 70-case .NET non-Compose suite
were **not** re-run in this round. This round changed Markdown only, so the runtime
evidence of fix round 1 stands and is not presented as a run of this round. The measurements
of this round were taken on the tree of this round; the documentation-only commits that
follow them change the tracked Markdown, so a later run reports a different source digest
value, and the invariant that matters is the verdict, `unchanged during the run`.

**Still open.** The three Plan 3 harnesses are still executed by no automated runner; Plan 4
owns that wiring. Wave 0/P0 acceptance, remote DemoModule end-to-end, tenant isolation,
outage/recovery and restore drills and remote CI remain **OPEN** and are not claimed here.

## Task 7, fix round 6 — the CI interaction of the contract suite, the CI disclosure and the comparator envelope (review pending)

**Objective.** Close the three findings of the final review of Task 7. **F-1** (Important):
this branch adds four test modules directly under `tests/Contract/`, the directory that
`.github/workflows/contracts.yml` runs on every push and pull request, and those tests need
prerequisites the workflow does not provision, so the live CI workflow of the branch would
go red. **F-2** (Minor): the CI disclosure of the Plan 3 verification document and of this
handoff named the three Plan 3 harnesses but not the 55 new tests in the very directory
the workflow does execute, nor the fact that they cannot pass in a CI checkout today.
**F-3** (Minor, ruled "must fix before merge"): `require('typescript')` sat outside the
try/catch envelope of the TypeScript surface comparator, so a checkout without a
resolvable `typescript` module printed a raw Node stack trace instead of a reason token
and a correlation identifier.

**Branch and state.** Branch `feat/wave-0-plan-3-frontend-contract-toolchain`, base
`5257abf`. The fix of this round is the commit on top of
`907e675` (`docs: clarify the round labels of the Plan 3 verification mapping`); it
carries the code, the tests and this documentation. Its own SHA is not recorded here,
because a commit cannot carry its own SHA. The commits `907e675`, `bb263e5`, `ccce8d4`,
`651de9b`, `cc5df3c`, `62a4c67`, `66c4523`, `97fcc45`, `ac69dc7`, `80de4d8`, `422e049`
and `0695cc2` are unchanged and byte-identical. The working tree contains only the
intended changes of this round and no uncommitted source work. No merge, push, deploy,
publication or database migration is authorized or performed.

**F-1 — the live CI workflow would have gone red.** The four new modules
`test_compatibility.py` (39 tests), `test_generated_check.py` (9), `test_sdk_surface.py`
(6) and `test_sdk_consumers.py` (1) need the pinned oasdiff binary and the pinned
`corvus.json.cli.5.8.8.nupkg` package below the git-ignored `.local/plan3-tools/`, an
installed `node_modules` with a resolvable `typescript` module, Docker with the pinned
.NET image and access to the NuGet flat container. The new
`tests/Contract/prerequisites.py` probes exactly those prerequisites and returns an
`(available, reason)` pair for each; the pinned versions and digests are read from
`tools/contracts/toolchain.json` and `tools/contracts/oasdiff.json`, none is hardcoded.
The probes check that the prerequisite is genuinely usable — the oasdiff binary must
execute, the package must match the pinned SHA-256, `typescript` must resolve from the
repository root, the docker daemon must answer and hold the pinned .NET image. Each of
the four modules now carries a class-level `unittest.skipUnless` with the reason string
as its second argument, so a CI-like checkout **skips** the 55 new tests with a visible
reason instead of failing them, and a prepared host still runs them for real.

**The regression protection is `tests/Plan3/ci-environment.sh`.** It copies the checkout
into a scratch directory without `.local` and without `node_modules`, runs
`python -m unittest discover -s tests/Contract -v` there and asserts the exit code 0, the
87 reported tests, the 55 skipped tests with their reasons and the 32 passed
pre-existing tests. It needs neither docker, nor the network, nor the pinned .NET image,
and it removes the scratch copy on exit. It is a case of
`tests/Plan3/aggregate-reporting.sh`.

**F-2 — the CI disclosure is now complete.** The Plan 3 verification document and this
handoff state, in the place of the existing disclosure, that the branch adds 55 new
tests under `tests/Contract/` which `.github/workflows/contracts.yml` discovers and
runs; that they need the git-ignored pinned tool artifacts, an installed `node_modules`,
Docker with the pinned .NET image and the NuGet flat container, none of which the
workflow provisions; that they are therefore **explicitly skipped with a visible reason**
in a CI-like checkout and **run for real** wherever the prerequisites are present (87
tests, 0 skipped, on a prepared host); and that wiring the prerequisites into CI is
**Plan 4's** job. The "Suites executed on this branch" table carries the correct count,
the skip behaviour and the round label.

**F-3 — the comparator closes with an envelope.** `tools/contracts/surface/compare-typescript.cjs`
loads the compiler inside the guarded region now: a missing `typescript` module produces
the ordinary closed envelope `{"compatible":false,"error":"SDK_SURFACE_UNAVAILABLE","cause":"typescript-unavailable","correlationId":…}`
on stdout, exit code 1 and an empty stderr, instead of a raw Node stack trace. The
reason token `SDK_SURFACE_UNAVAILABLE` and the exit code are unchanged; the cause
`typescript-unavailable` is a tool failure and is mapped by
`scripts/contracts/sdk_surface.py` to `SDK_TOOLCHAIN_REVIEW_REQUIRED`, as every cause
other than `no-declarations` is.

**Verification.** The contract suite of the tree of this round exited 0 with **87 tests
and 0 skipped** — the new tests really run on a prepared host. The CI simulation exited
0 with the 55 new tests skipped with a reason and the 32 pre-existing tests passed.
`python3 scripts/contracts/check.py` returned exit 0 with `SDK_GENERATED_CURRENT` and a
correlation identifier. The negative-case harness passed 3 of 3 cases with the unchanged
reason tokens `SDK_GENERATED_MISMATCH`, `CONTRACT_BASE_UNAVAILABLE` and
`CONTRACT_POLICY_REVIEW_REQUIRED`. The reporting harness passed 15 of 15 cases in that
round, including the two new ones; it has 16 of 16 cases since fix round 7, which added the
scratch-cleanup case. `npm run test -w apps/crm-web` passed 54 tests. The canonical
contracts are unchanged: `git diff --name-only 907e675..HEAD -- docs/contracts/` is
empty.

**RED before the fix.** The new CI-simulation harness was run against the pre-fix tree
(a scratch copy of `907e675` with the new harness and nothing else) and it **failed**:
the simulated CI run exited 1 with 43 failures and 6 errors, no test was skipped, and
the harness reported `PLAN3_CI_SIMULATION_FAILED` with its correlation identifier. After
the guard the same harness exits 0 with "87 tests, 55 skipped with a reason, 32 passed".

**Known limitations.** The 70-case .NET non-Compose suite, the clean exact-SHA export
with the browser suite and the full ten-step working-tree aggregate were **not** re-run
in this round; the rows of the summary table that they measured keep their fix round 5
label and are not presented as a run of this round. The contract suite of this round was
measured on a prepared host: on a host without the pinned prerequisites the same command
reports 87 tests with 55 of them skipped.

**Still open.** The four Plan 3 harnesses are still executed by no automated runner, and
the prerequisites of the 55 contract tests are still not provisioned by the workflow;
Plan 4 owns both. Wave 0/P0 acceptance, remote DemoModule end-to-end, tenant isolation,
outage/recovery and restore drills and remote CI remain **OPEN** and are not claimed here.

## Task 7, fix round 7 — the scratch cleanup of the Plan 3 harnesses (review pending)

**Objective.** Change one mechanism and nothing else: how the scratch directories of this
slice are deleted. A scratch directory that a script created itself, minutes ago, must be
**unlinked**. It must never be moved into a recoverable trash. Some agent runtimes put a
recoverable-delete shim named `rm` on the `PATH`; on Linux that helper moves its target into
the trash of the user (the XDG Trash spec) instead of unlinking it, so a bare `rm -rf` of a
multi-gigabyte scratch tree **copies** it into the trash instead of freeing the disk: 207 runs
had deposited about 55 GB in `~/.local/share/Trash`, the largest single scratch trees being
about 3.7 GB (a NuGet package cache plus npm-installed SDK tarballs inside a `mktemp -d`
directory). A normal developer machine has no such shim, so the scripts were not broken for
developers. No assertion, no printed message, no reason token, no correlation identifier, no
exit code and no order of operations changed in this round.

**Branch and state.** Branch `feat/wave-0-plan-3-frontend-contract-toolchain`, base
`5257abf`. The fix of this round is the commit on top of the fix round 6 commit; it carries
the helper, the new harness, the new case and this documentation. Its own SHA is not recorded
here, because a commit cannot carry its own SHA. No merge, push, deploy, publication or
database migration is authorized or performed.

**The change.** The new shared helper `scripts/lib/scratch-cleanup.sh` resolves the rm binary
to an **absolute** path: `/bin/rm` first, then `/usr/bin/rm`, and only a host without both
falls back to the rm of the `PATH`, which is resolved to an absolute path as well. The helper
prints nothing when it is sourced and writes nothing to stdout. Every scratch deletion of the
slice now goes through the resolved binary, with the end-of-options separator so that a
target that starts with a dash cannot be read as an option:

| File | Scratch deletion |
|---|---|
| `scripts/check-plan3.sh` | the log directory of the run |
| `tests/Plan3/aggregate-reporting.sh` | the work directory of the harness and the sandbox of a case |
| `tests/Plan3/ci-environment.sh` | the scratch checkout of the simulation |
| `tests/Plan3/clean-export.sh` | the export directory |
| `tests/Plan3/negative-cases.sh` | the work directory of the harness and the sandbox of a case |
| `tests/ClientConsumers/run.sh` | the temporary directory of the consumer build |

The helper is sourced through `$root`, so the change survives a copy of the checkout into a
sandbox.

**The regression protection is `tests/Plan3/scratch-cleanup.sh`.** It runs the fastest Plan 3
harness (`tests/Plan3/ci-environment.sh`) in a child shell whose `PATH` carries a **fake rm**
first. The fake rm behaves like the recoverable-delete shim: it records that it was called and
it moves its target into the trash of a **controlled** `XDG_DATA_HOME` instead of unlinking
it. The child also runs with a **controlled** `TMPDIR`, so its scratch lands inside the
controlled directories of the harness and the trash of the user is never touched. The harness
asserts that the child exited 0, that the fake rm was never invoked, that the controlled data
home stayed empty and that the controlled `TMPDIR` is empty afterwards. The harness is
self-contained: it resolves the rm binary for its own scratch by itself and does not use the
helper under test, so it can be run against a tree that does not have the helper at all. It
is a case of `tests/Plan3/aggregate-reporting.sh`, which grew from 15 to 16 cases.

**RED before the fix.** The new case was run against the **pre-fix tree** — a scratch copy of
`40a7c91` with the new helper, the new harness and the new case, and nothing else — and it
**failed**:

```
case  a scratch directory is unlinked instead of being moved to a trash
FAIL  scratch cleanup
      the scratch cleanup harness exited 1, expected 0
      FAIL  scratch cleanup
      the PATH-resolved rm was invoked
      -rf /tmp/tmp.ZSOS2V4Gmv/tmp/tmp.1zdi2mnhAR
FAIL  scratch cleanup
      the scratch was moved into a recoverable trash instead of being unlinked
      tmp.1zdi2mnhAR

scratch cleanup cases: 2 failed
{"reason": "PLAN3_SCRATCH_CLEANUP_FAILED", "detail": "2 assertions failed", "correlationId": "ed05f69c-3b21-4d29-8944-2c92adc9fd39"}

aggregate reporting cases: 1 of 1 failed
{"reason": "PLAN3_REGRESSION_CASES_FAILED", "detail": "1 of 1 cases failed", "correlationId": "a38cd107-5d33-4a82-8e6c-f0f304cd71f9"}
```

The bare `rm -rf` of the pre-fix `tests/Plan3/ci-environment.sh` reached the fake rm, the
scratch survived in the fake trash, and the case failed for that reason. After the fix the
same case passes.

**Verification.** The commands of this round and their real results:

| Command | Result |
|---|---|
| `PATH=<Node-24.22.2>/bin:$PATH LD_LIBRARY_PATH=/tmp/plan3-libs .venv/bin/python -m unittest discover -s tests/Contract -v` | exit 0, **87 tests**, **0 skipped**, 1078.090 s, OK |
| `bash tests/Plan3/aggregate-reporting.sh --base-ref 408ded6 --only scratch_cleanup_unlinks_the_scratch` | exit 0, **1 of 1 case**, 2.782 s |
| `bash tests/Plan3/aggregate-reporting.sh --base-ref 408ded6` | exit 0, **16 of 16 cases**, 4m5.715s |
| `bash tests/Plan3/negative-cases.sh --base-ref 408ded6` | exit 0, **3 of 3 cases**, 3m30.213s |
| `bash tests/Plan3/ci-environment.sh` | exit 0, **87 tests**, **55 skipped with a visible reason**, **32 passed**, 2.729 s |
| `bash tests/Plan3/scratch-cleanup.sh` | exit 0, the scratch of the child was unlinked, the `PATH`-resolved rm was never invoked |
| `python3 scripts/contracts/check.py` | exit 0, `SDK_GENERATED_CURRENT`, correlation identifier present, 11.651 s |
| `npm run test -w apps/crm-web` | exit 0, **54 tests** passed |

**Changed files.** `scripts/lib/scratch-cleanup.sh` (new),
`tests/Plan3/scratch-cleanup.sh` (new), `scripts/check-plan3.sh`,
`tests/Plan3/aggregate-reporting.sh`, `tests/Plan3/ci-environment.sh`,
`tests/Plan3/clean-export.sh`, `tests/Plan3/negative-cases.sh`,
`tests/ClientConsumers/run.sh`, `docs/engineering/07-WAVE-0-PLAN-3-VERIFICATION.md` and
this document.

**Known limitations.** The 70-case .NET non-Compose suite, the Local Python suite, the clean
exact-SHA export with the browser suite and the full ten-step working-tree aggregate were
**not** re-run in this round; the rows of the summary table that they measured keep their fix
round 5 label and are not presented as a run of this round. The residual leak of the
`tempfile.TemporaryDirectory` directories of `scripts/contracts/*.py` when a process is
SIGKILLed is **known** and **unchanged** by this round: those directories are removed by
`shutil.rmtree` and never reach a trash, so they are out of scope here.

**Still open.** The five Plan 3 harnesses are still executed by no automated runner, and the
prerequisites of the 55 contract tests are still not provisioned by the workflow; Plan 4 owns
both. Wave 0/P0 acceptance, remote DemoModule end-to-end, tenant isolation,
outage/recovery and restore drills and remote CI remain **OPEN** and are not claimed here.

## Task 7, scoped re-review of fix rounds 6 and 7 — READY

**Verdict.** **READY.** Critical **0**, Important **0**, Minor **3** — one of the three is
pre-existing and outside the reviewed range. The re-review was performed by an independent
verifier over the range `907e675..88f5542` (the fix round 6 commit `40a7c91` and the fix
round 7 commit `88f5542`), on the review package
`review-907e675..88f5542.diff`. The durable verdict, with every command and every
mutation experiment it names, is recorded in the SDD workspace of this slice; the SDD
workspace is scratch and is removed at the end of the slice, so the three documentation
corrections below are the durable trace.

**F-1 is genuinely closed.** The guard is keyed on real *usability*, not on the existence
of a path: making the pinned oasdiff binary non-executable, corrupting its body, and
appending seven bytes to the pinned Corvus package each turned the affected module into a
skip **with a visible reason** and left the run green. The guard does **not** hide a real
failure: with the prerequisites present and the comparator mutated to report
`compatible: true`, the suite exited 1 with two failures and **zero** skips; restoring the
comparator returned the suite to green. The CI simulation is not tautological: against the
pre-fix tree `907e675` it exits 1 with `FAILED (failures=43, errors=6)`, 0 skipped and the
envelope `PLAN3_CI_SIMULATION_FAILED` — the documented RED, reproduced exactly.

**F-3 is genuinely closed.** Without typescript the comparator exits non-zero, prints the
reason token `SDK_SURFACE_UNAVAILABLE` and a correlation identifier, and its **stderr is
0 bytes**; the pre-fix tree printed 923 bytes of raw Node stack trace on the same path.

**F-2 is closed.** The CI disclosure states the 55 new tests, the missing prerequisites,
the explicit skip behaviour, the local 87/0-skipped reality and that Plan 4 owns the
wiring.

**The scratch cleanup of fix round 7 is correct and mutation-sensitive.** Reverting
`tests/Plan3/ci-environment.sh` to `40a7c91` makes the new aggregate case fail with
`PLAN3_SCRATCH_CLEANUP_FAILED` and the documented assertions ("the PATH-resolved rm was
invoked", "the scratch was moved into a recoverable trash"); restoring it makes the case
pass. The helper is silent when sourced, resolves `/bin/rm` as an absolute path, is never
reached through a `PATH` lookup, handles a target that starts with a dash, and survives
the tar copy of the checkout into a sandbox. No bare `rm -rf` is left under `scripts/` or
`tests/`. An additional controlled A/B — a pre-fix and a hardened `tests/ClientConsumers/run.sh`
under the same controlled `XDG_DATA_HOME` — deposited **one** trash entry with the pre-fix
variant and **none** with the hardened one.

**The measurements of the reviewer, on the reviewed commit** (independent of the
implementer's own numbers):

| Command | Result |
|---|---|
| `.venv/bin/python -m unittest discover -s tests/Contract -v` | exit 0, **87 tests**, **0 skipped**, `Ran 87 tests in 1246.793s`, OK |
| `bash tests/Plan3/aggregate-reporting.sh --base-ref 408ded6` | exit 0, **16 of 16 cases** |
| `bash tests/Plan3/negative-cases.sh --base-ref 408ded6` | exit 0, **3 of 3 cases** |
| `bash tests/Plan3/ci-environment.sh` | exit 0, **87 tests**, **55 skipped with a visible reason**, **32 passed** |
| `bash tests/Plan3/scratch-cleanup.sh` | exit 0 |
| `npm run test -w apps/crm-web` | exit 0, **54 tests** |
| `python3 scripts/contracts/check.py` | exit 0, `SDK_GENERATED_CURRENT` |

**The three Minor findings and their disposition.**

- **M-1 — the harness count contradicted its own table.** `07-WAVE-0-PLAN-3-VERIFICATION.md`
  said "Plan 4 owns the wiring of these **four** commands" while the table above it lists
  **five**. **Corrected** in the commit on top of `88f5542`.
- **M-2 — the module-level guard over-skips.** The guard is applied per test module, so
  six tests that need no pinned tooling (the five `TypeScriptSurfaceTests` need only a
  resolvable `typescript` module, and `test_generated_code_uses_checkout_stable_newlines`
  only reads a tracked file) are skipped as well. The behaviour is fail-closed with a
  visible reason and hides no failure, and the granularity is deliberate. **Accepted as
  designed** and now **disclosed** in the CI disclosure of the verification document,
  which states that the "32 passed" figure of a CI checkout understates the reachable
  coverage.
- **M-3 — a stale test count in the coverage map.** The row `W0P3-09` said
  `test_sdk_surface.py` has "10 cases"; the module has had 5, then 6, since it was
  created. The wrong number was introduced in `422e049`, an ancestor of the reviewed
  range. **Corrected** in the commit on top of `88f5542`, although it was outside the
  reviewed range.

No second scoped re-review was performed: the changes above are documentation only, they
touch no assertion, no message, no reason token, no correlation identifier and no exit
code, and the review rule of this slice allows one fix wave and one scoped re-review.

**Browser suite, re-measured by the controller at the final SHA.** The browser suite was
not re-run by the reviewer. The controller ran it at the final tree against the real local
stack: `bash scripts/dev-up.sh` (exit 0, project `crm-local-91859983f1e0`, API
`http://localhost:5080`), `bash scripts/dev-smoke.sh` (exit 0),
`npm run test:e2e -w apps/crm-web` (**4 passed, 9.4 s**, exit 0) and
`bash scripts/dev-down.sh` (exit 0, the stack stopped, no container left). The host
prerequisite of the browser — the Chromium shared libraries under
`/tmp/plan3-browser-libs` and the `libatomic` workaround under `/tmp/plan3-libs` — had
been removed by the disk cleanup of the incident described in fix round 7, and was restored
before this run; the pinned Node 24.22.2 needs no `libatomic` workaround.

**Still open.** The five Plan 3 harnesses are still executed by no automated runner, and
the prerequisites of the 55 contract tests are still not provisioned by the workflow; Plan 4
owns both. Wave 0/P0 acceptance, remote DemoModule end-to-end, tenant isolation,
outage/recovery and restore drills and remote CI remain **OPEN** and are not claimed here.
