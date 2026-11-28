# Wave 2 exit verification

## Verified Wave 2 traceability — candidate 1e78b78

This current table supersedes the preliminary source inventory below. Each
named test was found with outcome `passed` in the sanitized reports for
[run 46114911449](https://github.com/leduc4894/crm-centric/actions/runs/46114911449).
The identifiers are representative evidence, not an assertion that these cases
alone exhaust each acceptance criterion. Tested merge `fdfd10a92ff3255cff750599591f0b8c60f799ce`
has the same product tree as head `1e78b7860dd7d5620556ea5c27becda7afde556d`.

| Scope | Executed test identifier | Outcome |
|---|---|---|
| US-03 upsert/ETag | `Integration.ContactEndpointsTests.Existing_email_requires_matching_strong_etag_and_updates_only_supplied_fields` | PASS |
| US-03 contact version race | `Integration.ContactEndpointsTests.Concurrent_mutations_of_same_version_have_one_winner(first: "patch", second: "delete")` | PASS |
| US-03 tenant-safe filters | `Integration.ContactListTests.Literal_search_tag_and_timestamp_combine_without_cross_tenant_joins` | PASS |
| US-03 cursor isolation | `Integration.ContactListTests.Cursor_survives_anchor_deletion_and_binds_tenant_and_filter` | PASS |
| US-04 tags | `Integration.TagEndpointsTests.Assignments_are_versioned_filtered_paginated_and_tenant_scoped` | PASS |
| US-04 fields and permission | `Integration.CustomFieldEndpointsTests.Value_access_enforces_tenant_permissions_preconditions_and_required_null` | PASS |
| US-05 internal notes | `Integration.TimelineEndpointsTests.Notes_round_trip_contract_text_and_advance_strong_contact_etag` | PASS |
| US-05 transaction rollback | `Integration.TimelineEndpointsTests.Failures_rollback_entry_version_and_journal(failure: "outbox.events")` | PASS |
| Tenant isolation | `Integration.ContactEndpointsTests.Another_tenant_contact_is_not_visible` | PASS |
| Audit/outbox rollback | `Integration.ContactEndpointsTests.Outbox_failure_rolls_back_patch_and_delete` | PASS |
| Tag journaling | `Integration.TagEndpointsTests.Application_database_mutation_enforces_version_and_commits_both_journals` | PASS |
| Migration down/up | `Integration.MigrationTests.Down_then_up_preserves_unowned_objects` | PASS |
| Real identity and tenant browser sessions | `browser-oidc.spec.ts::real provider signs in isolated tenants and terminates provider session::chromium` | PASS |
| Contact browser workflow | `contacts.spec.ts::contact persists across sessions, rejects stale notes and isolates delayed tenant reads::chromium` | PASS |

Default-list RT-PERF-01 produced p95 112.82 ms for 50 authenticated users and
50,000 active contacts; see [benchmark evidence](../evidence/wave2-plan9-benchmark/1e78b78/).
The first benchmark did not capture measured RTT or client CPU; a later run on
the same product candidate records both, and its provisioning scripts are
published with the evidence. Independent reproduction remains outstanding.
Real external ingestion, dedup/reorder and worker/replay are not covered by
internal timeline tests. Migration down/up is not a backup/restore drill.
Security failed and is deferred by explicit user decision; the aggregate failed.
The breaking contract result is scoped and candidate-owned, not an independently
enforced compatibility approval. P0 acceptance remains open under AGENTS.md.

## Historical preliminary inventory

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

## Hosted candidate and benchmark — 2026-11-28

[Run 46112248331](https://github.com/leduc4894/crm-centric/actions/runs/46112248331)
completed on head `fcccd09ba006b53b4960e3613f0ff1f856da6e42`, tested merge
`6a73157c2d8353ca1ce55edb60d2a5986db699b3`, base
`db04aeaecd7f4b6d083335a71418788d3c4561ca`. Head and merge share tree
`a4ab827d1602c49121ac532bc94d3785b345e8db`. All non-security jobs passed:
backend 424/424, Compose 18/18, contracts-sdk 137/137, web-browser 306/306
(297 Vitest and nine browser, including real-provider OIDC and contacts), and
ci-tests 219/219 (201 CI and 18 benchmark). All reported zero skips. The
actual-base contract result was `CONTRACT_BREAKING_REVIEWED`, with a
user-authorized candidate-owned scope and no independent enforcement; it is not
a compatible verdict. Security remains FAIL and is deferred by user decision;
aggregate FAIL `CI_JOB_NON_SUCCESS`, correlation
`e1dd8d6f-96f3-4845-aacf-a259b271e7f7`.

Security comparison with run 46109754682 found 105 blocked Gitleaks findings in
each; all 15 changed opaque IDs matched the prior findings one-to-one by rule,
path and value digest calculated only in memory, with no unmatched or ambiguous
findings. Trivy's nine IDs and verdicts were unchanged (one blocked, eight
passing). No values or digests were printed or saved. No new finding was found
in this exact comparison; this is not security clearance.

The default-list benchmark on `fcccd09` passed its measured threshold with
50,000 contacts and 50 authenticated users: 242,305 valid responses, zero
errors, p95 108.02 ms. The separate search diagnostic returned p95 1861.61 ms
on 12,129 valid responses, zero errors; its plan uses a sequential scan and the
sampled database CPU averaged about 445% of one CPU. Reports, environment,
plans and sampled resource summary are in
[benchmark evidence](../evidence/wave2-plan9-benchmark/fcccd09/). The manifest
records loopback/bridge topology but not a measured network RTT or client CPU
saturation, and provisioning remains an environment-local script rather than a
checked-in repeatable workflow. Those evidence gaps remain open; the result does
not cover arbitrary search filters or transfer to another candidate.

This documentation is recorded after the run and does not transfer the tested
verdict to a documentation-only commit. Wave 2 exit remains OPEN pending the
remaining review/evidence assessment; P0 remains OPEN until the remote
DemoModule E2E, concurrent duplicate, tenant/worker/replay isolation,
outage/recovery and restore drills pass. The security deferral is not a waiver
or clearance. The earlier unexplained Plan 8 boolean failure is not proven to be
the cause of the previous hosted failure, even though this candidate passed.

## Later candidate and review findings — 2026-11-28

Review identified that the preceding coverage policy did not require the Wave 2
integration identities, and the benchmark did not require a full first page or
classify some truncated HTTP responses. The previous `fcccd09` measurements
remain historical and cannot establish that every measured page was complete.
Candidate `1e78b7860dd7d5620556ea5c27becda7afde556d` requires all 163 Wave 2
integration test identities, checks 20 distinct contacts and a nonempty cursor,
and classifies incomplete HTTP responses. Focused regression tests passed.

The new candidate's default workload passed with 232,626 valid responses, zero
errors and p95 112.82 ms. The search diagnostic failed its 400 ms threshold with
13,022 valid responses, zero errors and p95 1643.72 ms; its plan shows a
sequential scan. See [sanitized evidence](../evidence/wave2-plan9-benchmark/1e78b78/).
The earlier RTT, client CPU and reproducible provisioning gaps remain. These
measurements apply only to the recorded candidate. Search performance remains
an open issue even though the plan designates this scenario diagnostic.

[Run 46114911449](https://github.com/leduc4894/crm-centric/actions/runs/46114911449)
tested merge `fdfd10a92ff3255cff750599591f0b8c60f799ce`, whose tree equals
head `1e78b78`. Non-security jobs passed with zero skips: backend 424/424,
Compose 18/18, contracts-sdk 137/137, web-browser 306/306 and ci-tests 222/222.
Security failed and the aggregate failed. Comparing the security reports with
run 46112248331 found 105 blocked Gitleaks findings in each; the 15 changed IDs
matched one-to-one by rule, path and value digest computed only in memory, with
zero unmatched. Trivy's nine IDs and verdicts were unchanged, including one
blocked result. This comparison is not security clearance. The actual-base
contract gate returned `CONTRACT_BREAKING_REVIEWED`; the candidate-owned scope
is not independent enforcement. Remaining Wave 2 and P0 acceptance items above
are not closed by the CI result.

The browser reports cover the real OIDC provider and the contact flow through the
measured local/test stack. They do not prove SPA fallback on a deployed host.
The exact non-security hosted test total was 1,107, with zero skips. The
candidate-owned reviewed-breaking result relies on the user's authorization to
continue the exact CI work; no independent verifier has been provisioned. Under
[the MVP scope decision](09-WAVE-0-CI-MVP-SCOPE.md), an independent verifier is
pre-production hardening, not by itself a separate MVP exit requirement. This
does not certify that an independently enforced review or deployment occurred.

## Default benchmark rerun with network and client CPU evidence

On the same clean product candidate `1e78b78`, a separate isolated rerun gave
235,048 valid responses, zero errors and p95 110.36 ms after 60 seconds warmup
and 300 seconds measurement, correlation `a0234a46-6fc9-44b4-a3fe-c59548278e51`.
Before the workload, ICMP RTT averaged 0.069 ms on client loopback and 0.112 ms
from the API network namespace to PostgreSQL. The client process averaged
86.78% of one CPU and peaked at 242.36% across 72 five-second samples over
warmup and measurement; this does not by itself prove absence of every client
bottleneck. [Reports, environment and scripts](../evidence/wave2-plan9-benchmark/1e78b78-rerun/)
include 32 container CPU/memory samples summarized without private data. The
search result above comes from the earlier separate run and its performance
issue remains open. The provisioning scripts are recorded for reproduction but
have not received a separate independent repeatability review. The test-owned
stack, two volumes, four images, worktree and private state were removed.
