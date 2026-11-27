# Wave 2 Plan 4 handoff

## Task 1 timeline storage, cursor and contract

The user authorized implementing [Plan 4](../superpowers/plans/2026-11-27-wave-2-plan-4-timeline-contact-ui.md) on `wave-2` with sequential SDD. The user previously approved the strong updated-contact ETag on note creation. Focused checks run locally. The full suite is reserved for the Wave 2 GitHub CI exit.

Implementation commit `b3d831c` follows Plan 3 candidate `f8f2862`. Changed the CRM OpenAPI and generated clients, PostgreSQL timeline migration and application grants, API timeline cursor, SDK wire fixture, contract and integration tests, and operations docs. The new RLS table permits scoped application reads. The atomic note writer belongs to Task 2. The database stores summaries as UTF-8 bytes including embedded NUL. Cursor scope includes tenant, contact and sort. See [migration instructions](../operations/10-TIMELINE-MIGRATION.md) and [key rotation](../operations/08-CONTACT-CURSOR-KEY.md).

Local verification used the repository virtualenv and pinned Node/.NET toolchain. Logs are under ignored `.local/timeline-*.log`.

| Command or check | Observed result |
| --- | --- |
| `python3 -m unittest discover -s tests/Contract -p test_contact_timeline.py -v` | RED missing 201 headers, then PASS 1/1 |
| `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~TimelineSchemaTests'` | RED missing cursor class; final PASS 2/2 with real PostgreSQL 18.6 Testcontainers |
| `python3 scripts/contracts/generate.py`, then `python3 scripts/contracts/check.py` | SDK_GENERATED, SDK_GENERATED_CURRENT |
| Archived Plan 3 TypeScript client (f8f2862), mocked POST note with ETag and UTF-8 summary | PLAN3_NOTE_BODY_OK; updated wire fixture also TYPESCRIPT_WIRE_OK against archived clients |
| `python3 scripts/contracts/compatibility.py --base-ref origin/main --mvp-reviewed-policy` | Closed CONTRACT_POLICY_REVIEW_REQUIRED, base/target db04aea, correlation db40e02c-dde5-444d-931d-2029edc23b52 |
| `python3 scripts/contracts/compatibility.py --base-ref f8f2862` | CONTRACT_COMPATIBLE, all four TypeScript/.NET surface and historical consumer checks passed, correlation 24feefc6-1393-41e5-8aac-aacd977cf811 |
| Full suite, hosted CI, deployment, performance and P0 drills | Not run |

The existing actual-base policy gate remains in force. Review the changed candidate before release. The earlier Plan 3 reviewed-breaking verdict applied to that specific candidate. Wave 2 and P0 remain open.

The separate packaged-consumer check initially failed. Running `bash tests/ClientConsumers/run.sh` reproduced NU1403 on the unpublished 2.0.0 candidate after historical .NET and TypeScript wire checks passed. Refreshed the two candidate fixture locks from the normalized built package, following the [unpublished candidate procedure](../operations/07-CONTACT-SDK-MIGRATION.md). Historical consumer locks and compatibility policy remain intact.

After the lock refresh, `python3 -m unittest discover -s tests/Contract -p test_sdk_consumers.py -v` passed 1/1, with no skip, in 113.951s. The runner exercised the installed packages with a fresh npm cache and locked restores. The only outstanding Task 1 gate is the actual-base policy review. Next steps are independent Task 1 review, atomic note and timeline endpoints per Task 2, and eventual hosted wave-exit verification. The implementation and this handoff are committed separately from the ignored local SDD ledger and logs.

## Superseded required-header experiment

Task 1 review identified that the response header was optional in the generated TypeScript. Added a contract assertion for the header's `required` flag. The focused timeline contract command above failed 1/1 with `None is not True`, then passed 1/1 after declaring `required: true` in the CRM OpenAPI. Regeneration and generated-current checks passed. The TypeScript response now exposes `ETag: string`. No compiled .NET source changed. [The API migration](../operations/10-TIMELINE-MIGRATION.md) concerns storage. Runtime header emission belongs to Task 2.

The incremental gate against `f8f2862` now reports **SDK_SURFACE_INCOMPATIBLE** (exit 1, correlation `ef864d46-86c5-47a6-9a7f-aca470bf753f`), superseding the earlier optional-header result. The TypeScript surface comparison failed. The .NET surface comparison and both historical consumers passed. The archived Plan 3 TypeScript wire probe returned TYPESCRIPT_WIRE_OK. The actual-base gate remains **CONTRACT_POLICY_REVIEW_REQUIRED** (exit 1, correlation `076d1dd4-546c-4185-996c-64ef7a87306c`). The approved strong response header does not grant approval to waive the resulting SDK break. Review the TypeScript migration and seek the required ADR decision before acceptance. No policy, historical lock, deployment, CI or acceptance status changed.

Packaged consumer check (`python3 -m unittest discover -s tests/Contract -p test_sdk_consumers.py -v`) passed 1/1, zero skips. The required-header experiment was held uncommitted at `e3275cd` pending a compatibility decision. No fixture lock refresh was necessary for this correction. Decide between a versioned strict surface and explicit acceptance/migration of existing TypeScript response fixtures. Full suite, hosted CI and HTTP runtime tests were not run in this correction.

## Compatible ETag contract and client fallback

Selected the additive option in [Plan 4](../superpowers/plans/2026-11-27-wave-2-plan-4-timeline-contact-ui.md). Discarded the uncommitted required-header experiment. The OpenAPI documents that a successful server response always supplies the updated contact's strong ETag. If the client receives a response without the header, it must GET the contact and obtain its current ETag before its next conditional write. Generated TypeScript exposes `ETag?: string`. The generated .NET source is unchanged. The server guarantee must be tested at the HTTP boundary in Task 2 and the client fallback in the frontend tasks.

The timeline contract test failed on the previous description and then passed 1/1 after the update. Both SDK generation and the generated-current check passed. The archived Plan 3 TypeScript client returned TYPESCRIPT_WIRE_OK. On this final candidate the incremental gate against `f8f2862` returned CONTRACT_COMPATIBLE with both surfaces and both historical consumer checks passing, correlation `90ba36cb-fb03-4b15-8246-e516e0cbc55a`. The actual `origin/main --mvp-reviewed-policy` gate returned CONTRACT_POLICY_REVIEW_REQUIRED, correlation `1e2de056-7cf8-4be8-93b5-62b292a245c0`. No policy edit or release approval was made. The strict experiment's SDK break does not apply to this candidate. The contract/SDK inputs and the contract assertion are the only implementation files changed in this correction. Logs are in `.local/timeline-fallback-*.log`. The PostgreSQL schema and cursor tests passed on the unchanged runtime work above. Full suite, HTTP endpoint verification and hosted CI were not run in this correction.

The separate packaged-consumer check passed 1/1 with no skips using the existing candidate and historical locks. Next steps remain Task 2 runtime verification and review of the actual-base policy before release.

## Task 2 runtime and PostgreSQL verification

Implemented timeline GET and atomic note POST on `wave-2` at `be09e9a`, based on `ad1988b`. [Plan 4](../superpowers/plans/2026-11-27-wave-2-plan-4-timeline-contact-ui.md) and the approved strong updated-contact ETag govern this work. Changed TimelineEndpoints, Program, TimelineStore, AtomicContactNote migration, DatabaseRoleGrants, endpoint/concurrency/slow-body integration tests and the [migration instructions](../operations/10-TIMELINE-MIGRATION.md). No new contract or SDK changes. The body is read outside mutation locks. The database validates writer scope and expected version before atomically advancing the contact and writing the note, audit and outbox. The API sets the 201 response and strong ETag after commit.

Local verification used PostgreSQL 18.6 Testcontainers with the repository .NET toolchain. The initial `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~TimelineEndpointsTests|FullyQualifiedName~TimelineConcurrencyTests'` was RED with 8 failures for missing routes/lock waits. Intermediate run passed 14/14. A supplementary Unicode test first reproduced HTTP 413 on a valid fully escaped 1,000-scalar payload. After switching to the existing streaming JSON approach, the final timeline filter passed 16/16, zero skipped. The expanded filter `FullyQualifiedName~TimelineEndpointsTests|FullyQualifiedName~TimelineConcurrencyTests|FullyQualifiedName~TagFieldConcurrencyTests|FullyQualifiedName~ContactEndpointsTests|FullyQualifiedName~TimelineSchemaTests` passed 57/57, zero skipped. This includes a deferred commit failure, three insert failures, direct application-role invocation, membership revocation during body read and six barrier races. The failed-commit test exercises a server-side rejection. Ambiguous network loss after commit was not injected. `git diff --cached --check` passed. Logs and Task 2's detailed SDD report are ignored local artifacts.

No pending Task 2 implementation edits. Next steps are independent Task 2 review and Task 3 UI integration including the missing-header GET fallback. The actual-base policy decision, full hosted wave-exit run, release and P0 drills remain pending. No full suite or deployment ran in this task.

## Task 2 review correction for bounded note requests

Commit `8befd9a` on `wave-2` addresses independent review of request memory use in [Plan 4](../superpowers/plans/2026-11-27-wave-2-plan-4-timeline-contact-ui.md). TimelineEndpoints now authorizes and reads at most 16 KiB of note JSON into the buffer before parsing. The existing shared ContactEndpoints reader counts bytes per chunk and stops when the next chunk exceeds the cap, including when no Content-Length is available. Other callers retain the existing 8 KiB default. The note cap accommodates 1,000 escaped supplementary scalars (12,000 value bytes), JSON framing, and ordinary formatting. Additional whitespace can put the request over the total transport limit.

Extended DefinitionEndpointFixture and TimelineEndpointsTests with a 64 KiB chunked JSON request, asserting correlated HTTP 413 with REQUEST_INVALID, the original strong ETag, and unchanged timeline, audit and outbox counts. On real PostgreSQL Testcontainers the filtered Oversized_chunked test failed RED with actual HTTP 400. After the fix, `dotnet test tests/Integration/Integration.csproj --no-restore --filter 'FullyQualifiedName~TimelineEndpointsTests|FullyQualifiedName~Slow_body_holds_no_mutation_or_membership_lock_and_rechecks_revocation'` passed 14/14, zero skipped, including the escaped-scalar boundary and note revocation during a slow upload. The staged whitespace check passed. Logs are under ignored .local/timeline-body-review-*.log. All correction changes are committed. Independent re-review is next. Full CI and acceptance remain pending as recorded above.

## Task 3 scoped contact transport and shell

Implementation commit `e511140` on `wave-2` follows `516eec3` in [Plan 4](../superpowers/plans/2026-11-27-wave-2-plan-4-timeline-contact-ui.md). The user requested dedicated contact records from the list, with profile, tags and fields beside the timeline and note form. The shell implements list/record navigation and basic contact information using the prototype palette, spacing and responsive sidebar. Tasks 4 and 5 supply the remaining interactions.

Changed the CRM web App, contacts client/number codec, shell, styles and tests, dev proxy/alias configuration and tests, TypeScript paths, web package dependencies and npm lockfile. The generated CRM SDK drives every method. Custom numeric values stay as JSON number tokens in both directions. Conditional writes take an explicit strong ETag. The UI should call `requireETag` with the latest operation result before its next conditional write. A missing note ETag triggers a contact GET at that point. Do not silently retry conflict responses. Scoped request lifetime ends on principal, tenant or record navigation. The client independently checks live auth before a response can invalidate login or update the record.

Verification on Node 24.22.2/npm 11.19.1:

| Command or check | Result |
| --- | --- |
| `npm exec --workspace @crm-centric/crm-web -- vitest run src/contacts/crmClient.test.ts src/contacts/ContactShell.test.tsx config/contactsProxy.test.ts` | Initial RED 3 missing modules. Final PASS 27/27, no skips. |
| Additional focused REDs | 3/19 failed on invalid or escaped field JSON, 2/21 failed on real platform correlation format and invalid route IDs, 1/22 failed on an untyped number write. Final run covers all corrections. |
| Same Vitest command with `config/devServer.test.ts config/tenantProxy.test.ts src/auth/AuthProvider.test.tsx src/tenant/TenantProvider.test.tsx src/tenant/TenantPicker.test.tsx src/health/HealthStatus.test.tsx` | PASS 38/38, no skips |
| `npm run typecheck --workspace @crm-centric/crm-web`, targeted ESLint on changed source/proxy, `npm run build --workspace @crm-centric/crm-web`, staged whitespace check | PASS |
| Browser E2E, deployment, full suite, hosted CI and P0 drills | Not run in Task 3 |

Task 3 transport/shell edits are committed. Suite logs and SDD task report are ignored local artifacts. Next are independent Task 3 review, Tasks 4–5 UI implementation and Task 6 real-stack E2E. Actual-base policy review and wave-exit gates remain open.

### Task 3 independent review corrections

Commit `a455ebd` on `wave-2` addresses both important Task 3 findings. The CRM client now checks the documented success statuses of all sixteen operations before accepting an outcome. A 202 DELETE now closes with WEB_CRM_CONTRACT_INVALID. Deletion requires 204, contact upsert supports 200 and 201, and note creation requires 201. The proxy UUID pattern accepts both hex cases while preserving exact literal route names and method restrictions. The contact-shell history test uses an uppercase deep link and the loopback proxy test checks the uppercase record GET reaches the upstream.

On the pinned toolchain, the client/proxy RED run failed 3 of 26 checks, exposing DELETE/upsert acceptance of 202 and an upstream miss for uppercase IDs. `npm exec --workspace @crm-centric/crm-web -- vitest run src/contacts/crmClient.test.ts src/contacts/ContactShell.test.tsx config/contactsProxy.test.ts config/devServer.test.ts config/tenantProxy.test.ts` then passed 43/43 with no skips. Web typecheck, focused ESLint and staged whitespace checks passed. Changed files are the client, proxy allowlist and their tests, plus the shell test. Full suite and browser E2E remain pending. Next is independent re-review. The minor date-format validation observation was deferred by the reviewer.

## Task 4 contact list and profile

On `wave-2`, implementation `a8319df` builds on `09eca1c` for [Plan 4](../superpowers/plans/2026-11-27-wave-2-plan-4-timeline-contact-ui.md). The user requested an ActiveCampaign-style dedicated contact record and prototype visual language, with focused local verification and full CI at wave exit. Changed ContactShell and shell.css, added ContactList, ContactForm and ContactDetail plus list and form tests. No public contract, storage or dependency changes.

The list supports literal search, tag ID, updated-since and cursor pagination. The record uses a responsive two-column layout for the profile and upcoming activity panel. A duplicate email leaves the draft open and requires choosing a contact from the visible results, reviewing its GET data and explicitly confirming a conditional upsert. Conflicts preserve the draft. PATCH submits changed fields only. An ambiguous mutation requires user-directed reconciliation. The tenant response does not advertise write permissions, so an actual writer denial places the current view in read-only mode.

On the pinned Node toolchain the two requested Vitest files failed RED 6/6 before implementation and passed GREEN 8/8 with no skips. Supplementary REDs exposed stale candidate selection and PATCH including unchanged fields. The expanded run with ContactShell and crmClient tests passed 35/35. Web typecheck, targeted ESLint and staged whitespace checks passed. No full suite, browser E2E, deploy, hosted CI or P0 drill ran. The local SDD report and logs are ignored. No Task 4 edits remain pending. The separate branch handoff edit belongs to the coordinating session.

Next is independent Task 4 review, then Task 5 attribute and activity panels and Task 6 real-stack E2E. Actual-base review, hosted wave-exit gates and P0 acceptance remain pending.

### Task 4 independent review corrections

The Task 4 review requested two fixes under [Plan 4](../superpowers/plans/2026-11-27-wave-2-plan-4-timeline-contact-ui.md). On wave-2, based on 67b2d85, ContactForm now disables its profile inputs while awaiting a write. ContactList returns a correlated WEB_CRM_READ_CANCELLED when a filter supersedes a pending reconciliation read. The form stays blocked until its own refresh succeeds. ContactForm.test.tsx holds POST and PATCH responses and exercises a create timeout without an email followed by an interrupted refresh and a successful explicit refresh.

The targeted Vitest filter `freezes the submitted|keeps ambiguous create` failed RED 3/3. The combined ContactForm, ContactList, ContactShell and crmClient tests passed GREEN 38/38, no skips. Targeted ESLint, web typecheck and staged whitespace checks passed. No full suite, E2E or hosted CI ran. The unrelated branch handoff is outside this change. Next is independent re-review before Task 5.

## Task 5 contact attributes and activity

On `wave-2` based on `97f46dc`, Task 5 implements the dedicated record interaction requested by the user in [Plan 4](../superpowers/plans/2026-11-27-wave-2-plan-4-timeline-contact-ui.md). The profile, tag and typed field forms share the record's current ETag and write lock with the activity timeline and note composer. The layout stacks below 900px. The API authorizes field-definition creation separately from contact writes. A note 201 without a valid ETag triggers a contact GET. Conflicts and uncertain results retain unconfirmed drafts and require an explicit read before another write. Confirmed deletion clears the record.

Changed ContactDetail and shell.css, added ContactAttributes, CustomFieldSettings, ContactTimeline, the shared panel-read helper, three panel test files and their fixture. Updated ContactForm test response fixtures and added delayed scope-switch coverage to ContactShell tests. No public contract, migration or dependency change. This change is the commit titled “Add contact attributes and activity timeline to the dedicated record”. The unrelated working-tree branch handoff was excluded.

Verification used Node 24.22.2. The three Task 5 Vitest files failed RED 12/12 for missing UI and passed GREEN 20/20. Additional RED tests reproduced a missing current value during conflict review, retained deleted record information and lost note focus. The seven contact Vitest files passed 61/61. CRM web typecheck, focused ESLint and staged whitespace checks passed. Local test logs and the SDD report are ignored. No full suite, browser E2E, deployment, hosted CI or acceptance drills ran. Next are independent Task 5 review, Task 6 real-stack browser verification and wave-exit CI. There are no known local blockers.

### Task 5 independent review corrections

On `wave-2` based on `9c16929`, the review of [Plan 4](../superpowers/plans/2026-11-27-wave-2-plan-4-timeline-contact-ui.md) found two gaps. A contact version recovered after a failed write could leave another editor showing stale data. ContactDetail, ContactAttributes, ContactForm and recordPanels now refresh displayed tag membership and mounted field values before adopting the recovered version. A sibling read failure keeps writes locked and preserves drafts. ContactTimeline now permits reads of subsequent pages after a denied write. The changed tests are in ContactTimeline.test.tsx. No contract, dependency or migration changed.

The focused Vitest filter `cross-panel|denies a note` failed RED 2/2 then passed GREEN 2/2. The seven contact test files passed 64/64, including a failed sibling read followed by explicit recovery. CRM web typecheck, focused ESLint and staged whitespace checks passed. Detailed results are in the ignored Task 5 SDD report. The changes are in the commit titled “Refresh sibling attributes before accepting a recovered contact version”. The separate branch handoff edit is excluded. Next is independent re-review, followed by Task 6 browser E2E and hosted wave-exit CI. No full suite or browser tests ran during this correction.

## Task 6 focused real-stack verification

On `wave-2` at `e2fdb3a`, based on `5508826`, [Plan 4](../superpowers/plans/2026-11-27-wave-2-plan-4-timeline-contact-ui.md) adds a Chromium journey in `apps/crm-web/tests/e2e/contacts.spec.ts` and extends `realOidcFixture.ts`. Two separate browser contexts sign in with the same disposable owner. The fixture adds that owner to both tenants only for the contacts flow. A disposable, runtime-generated cursor key enables real list and timeline reads. The unrelated branch handoff is excluded.

**Reproduction.** Use Linux with readable `/proc`, Docker Engine and Compose, available pinned images/build dependencies, Python 3, the repository's locked npm workspaces, Node 24.22.2/npm 11.19.1, Playwright Chromium and its system libraries. Leave loopback port 5173 free. Run the following command from the repository root.

```sh
CRM_REAL_OIDC_TEST=1 npm exec --workspace @crm-centric/crm-web -- playwright test tests/e2e/contacts.spec.ts
```

The command starts its own uniquely labelled Compose project via `scripts/dev-up.sh`, migrates PostgreSQL, provisions synthetic Keycloak identities, seeds staff memberships and launches Vite on 5173. API and issuer ports are allocated on loopback. Fixture teardown closes browser contexts, stops its Vite process group and removes its containers, network, volumes, locally tagged images and temporary state. Omitting the opt-in skips the real-provider case. The local runner used the repository-local .NET 10.0.403 installation and extracted browser libraries.

| Focused command | Result |
| --- | --- |
| `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~TimelineSchemaTests\|FullyQualifiedName~TimelineEndpointsTests\|FullyQualifiedName~TimelineConcurrencyTests'` | 19 passed, 0 failed, 0 skipped on PostgreSQL/Testcontainers |
| `python3 -m unittest discover -s tests/Contract -p 'test_contact_timeline.py' -v` | 1 passed, 0 failed, 0 skipped |
| `npm exec --workspace @crm-centric/crm-web -- vitest run` with the seven contact test files, contactsProxy, devServer, tenantProxy, realOidcFixture and stackProvenance | 90 passed in 12 files, 0 failed, 0 skipped |
| `npm run typecheck --workspace @crm-centric/crm-web` and ESLint on both E2E files | Passed |
| `python3 scripts/contracts/compatibility.py --base-ref origin/main --mvp-reviewed-policy` | Closed with CONTRACT_POLICY_REVIEW_REQUIRED on actual base db04aea, correlation 20787911-15e3-4ba0-bef2-fd434edb45ae |

The contact list opens a dedicated record with profile and attributes beside the activity panel. Source review against the prototype found the same background, typography, navigation placement, accent colors and card treatment. This is a structural and style review. The timeline migration continues to require a forward fix after data is stored. [Timeline migration and rollback](../operations/10-TIMELINE-MIGRATION.md)

The browser fixture is the only shared test support changed here. Task 6 adds no migration or public contract change. Plan 4's documented strong ETag and optional generated client header still require candidate-specific actual-base governance review. Security remains FAIL/deferred. Plan 5 owns full GitHub CI and the 50,000-contact/50-concurrent-user latency benchmark. External ingest/replay RT-EVT-01, remote DemoModule, duplicate delivery, outage/recovery, restore drills, Wave 2 and P0 remain OPEN. No full local suite, push or deployment.

The new browser test failed RED 1/1 because the fixture lacked `withContactSessions`. An intermediate stack authenticated and created a contact but could not list it because the cursor key was unset. Its browser was stopped during diagnosis, and disconnected-context teardown failed. The runner's owned resources were removed. The fixture now supplies an ephemeral key and skips already-disconnected contexts during cleanup. The contacts browser command then passed 1/1 in 1.4 minutes, zero skips. It verified persistence after reload and Keycloak SSO, tag membership, an exact 29-digit field value, literal markup in a note, stale note HTTP 412 and draft retention, 390-pixel viewport fit, keyboard focus, and an empty tenant B list after delaying a successful tenant A API response. Neither credentials nor request headers were printed. Local evidence is in `.local/contacts-browser-{red,green,final}.log` and `.local/contacts-final-*.log`. The middle browser log records the interrupted attempt. The staged whitespace check passed.

The current hosted real-browser job selects the existing OIDC test by name. Plan 5 must include the contacts spec with its real-stack opt-in and assert an executed passing result in the hosted evidence. Independent Task 6 review precedes the wave-exit run.

The shared-fixture regression `CRM_REAL_OIDC_TEST=1 npm exec --workspace @crm-centric/crm-web -- playwright test tests/e2e/browser-oidc.spec.ts --project=chromium --grep 'real provider signs'` passed 1/1 in 1.6 minutes with zero skips. Local result is in `.local/contacts-browser-regression.log`.
