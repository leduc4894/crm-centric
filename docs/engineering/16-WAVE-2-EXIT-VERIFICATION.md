# Wave 2 exit verification — preliminary inventory

Wave 2 and P0 remain **OPEN**. This is an inventory, not an exit verdict. The
Plan 9 implementation, final candidate and its required evidence remain open.
See [Plan 9](../superpowers/plans/2026-11-27-wave-2-plan-9-exit.md) and
[handoff](../handoff/2026-11-27-wave-2-plan-9-handoff.md).

Preliminary PR [#4](https://github.com/leduc4894/crm-centric/pull/4), head
`e3876b57c8669d5bfeae302575fe287a0eb122f7`, base
`db04aeaecd7f4b6d083335a71418788d3c4561ca`, GitHub-reported merge
`71bcb4b04341a6627ac174af41013f05a9ca7e10`: [run 46022837693](https://github.com/leduc4894/crm-centric/actions/runs/46022837693)
was in progress at inventory time. This preliminary run is not evidence for a
later candidate. Actual checkout identity, job results, counts and artifacts
must be verified from the run when complete.

| Scope | Representative test IDs in source (not evidence of execution on this candidate) | Status |
|---|---|---|
| US-03 CRUD/upsert/ETag and permissions | `Integration.ContactEndpointsTests.Existing_email_requires_matching_strong_etag_and_updates_only_supplied_fields`; `Integration.ContactConcurrencyTests.Concurrent_mutations_of_same_version_have_one_winner` | OPEN |
| US-03 search, tenant and paging | `Integration.ContactListTests.Literal_search_tag_and_timestamp_combine_without_cross_tenant_joins`; `Integration.ContactListTests.Cursor_survives_anchor_deletion_and_binds_tenant_and_filter` | OPEN |
| US-04 tags and typed fields | `Integration.TagEndpointsTests.Assignments_are_versioned_filtered_paginated_and_tenant_scoped`; `Integration.CustomFieldEndpointsTests.Value_access_enforces_tenant_permissions_preconditions_and_required_null` | OPEN |
| US-05 internal timeline | `Integration.TimelineEndpointsTests.Notes_round_trip_contract_text_and_advance_strong_contact_etag`; `Integration.TimelineEndpointsTests.Failures_rollback_entry_version_and_journal` | OPEN |
| API and SQL tenant isolation, audit/outbox | `Integration.ContactEndpointsTests.Another_tenant_contact_is_not_visible`; `Integration.ContactConcurrencyTests.Outbox_failure_rolls_back_patch_and_delete`; `Integration.TagEndpointsTests.Application_database_mutation_enforces_version_and_commits_both_journals` | OPEN; worker/replay belongs to later waves |
| Browser and real OIDC | `contacts.spec.ts::contact persists across sessions, rejects stale notes and isolates delayed tenant reads::chromium`; `browser-oidc.spec.ts::real provider signs in isolated tenants and terminates provider session::chromium` | OPEN for this candidate |
| Migration | `Integration.MigrationTests.Down_then_up_preserves_unowned_objects` | OPEN; does not establish disaster recovery |
| RT-PERF-01 | 50,000 active contacts, 50 independent authenticated users; proposed default list workload | NOT RUN |
| RT-EVT-01 | external ingest, duplicate/reorder and crash recovery | OPEN; timeline schema fixtures do not establish this |

`jobs.py` invokes integration tests using `Category!=Compose` / `Category=Compose`,
`tests/Local`, `tests/Contract`, SDK and compatibility checks, Vitest and separate
regular/real-provider/contacts Playwright runs. The workflow has no invocation of
`tests/CI`, and the current `coverage.json` required inventory does not explicitly
name the Wave 2 contact integration classes or the boolean form test. Minimum
counts alone do not demonstrate their discovery. `merge_browser_evidence` explicitly
requires the real-provider and contacts cases. No policy or gate has been relaxed;
the CI inventory and enforcement need a focused test-first change and independent
review before the final run.

Plan 8 recorded an initial boolean field test failure under concurrent load and a
later pass; the cause remains unknown. Neither the rerun nor this inventory resolves
it. Last recorded security result was FAIL/deferred; no new result is assumed. The
actual-base compatibility verdict and independent review for the final candidate,
benchmark environment and measurement, hosted suite, and deployed SPA fallback all
remain open. P0 additionally requires the remote DemoModule, concurrent duplicate,
tenant/worker/replay isolation, outage/recovery and restore drills.

## Plan 9 benchmark and CI checkpoint — 2026-11-28

The benchmark was run against local candidate
`f5a34ed75fedf83a4651742e13e6aed5d0f8fb14`; documentation added after that
run does not transfer its verdict to another SHA. The measured API image was
rebuilt from that candidate. Both scenarios used a disposable real PostgreSQL,
Keycloak and API with 50,000 synthetic active contacts, 10 tags, 50,000 tag
assignments and 50 distinct authenticated users. Application-role checks
confirmed tenant-scoped counts and no superuser/BYPASSRLS; all 50 users passed
preflight, wrong tenant was 403 and invalid bearer was 401. The host had five
visible vCPUs and 7.75 GiB RAM; full sanitized configuration and artifacts are
in [benchmark evidence](../evidence/wave2-plan9-benchmark/).

| Workload | Result | Valid samples | p50 / p95 / p99 | Errors |
|---|---|---:|---|---:|
| Default list, proposed RT-PERF-01 exit workload | PASS | 247,518 | 56.3 / 105.3 / 145.0 ms | 0 |
| `q=segment-07`, 500 matches, diagnostic only | FAIL against 400 ms | 11,910 | 1303.1 / 1979.5 / 2265.2 ms | 0 |

Each scenario ran with 60 seconds of warmup and 300 seconds of measurement,
50 workers and a 10-second timeout. These results do not establish that all
filters meet the default-list threshold. For search, the recorded plan uses a
sequential scan and PostgreSQL averaged approximately 426% CPU across 24 samples
(one CPU is 100%); optimization is a follow-up, not a changed exit criterion.
The first two runs on `ded5bfc34ae63545b6f060f612c1bb7f0780fd41` remain in the
artifact directory: the measured Python client rebuilt its TLS opener on every
request, inflating latency. A focused regression and independent short curl
comparison established that issue; the harness was corrected before the full
runs above. Both original failures remain recorded, not replaced.

The new `ci-tests` job and 198 CI/18 benchmark required test identities are in
candidate `f5a34ed`; focused tests passed locally, but **the job has not run on
GitHub**. The actual-base compatibility probe returned
`CONTRACT_POLICY_REVIEW_REQUIRED`, correlation
`fdabbfbf-d411-43ac-8243-67b320b487e5`; the earlier candidate-owned approval does
not cover this CI/inventory change. [Review package](17-WAVE-2-CANDIDATE-REVIEW-PACKAGE.md).
Latest hosted non-security results still belong to
[run 46056853386](https://github.com/leduc4894/crm-centric/actions/runs/46056853386),
not this candidate. Security is deferred at user request; its last observed
verdict and aggregate were FAIL. No new security scan has been run. Wave 2
remains OPEN until candidate review and final hosted evidence are complete; P0
also remains OPEN for the separately required integration and recovery drills.

After this measurement, commit `b19189798c79230baf1f77c39ecc136977941d43`
changed only the historical contract-review test fixture. The fixture now uses
its immutable prior snapshot rather than the new live CI inventory; its focused
suite went from six failures/two errors to 11/11 passing. That test change makes
`b191897` a new candidate. The benchmark above remains evidence for `f5a34ed`
only; repeat affected candidate-bound evidence after the final scope is settled.
The actual-base review decision and hosted run for `b191897` remain outstanding.
