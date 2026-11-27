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
