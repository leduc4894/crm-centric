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
