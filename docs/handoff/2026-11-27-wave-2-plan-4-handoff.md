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
