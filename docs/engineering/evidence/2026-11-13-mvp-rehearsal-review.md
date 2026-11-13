# Exact-candidate MVP rehearsal and pending reviews

Candidate `9706334d7778ebcc4f5704d5bcbfbc8ec68ef969`, branch `feat/wave-0-plan-4-ci`. Local `main` and its actual merge-base with candidate are `c4a5ef40a34daded023d5e5121c0462e94cc1487`; local `origin/main` remains `df993ae`. This rehearsal models a non-forced local push from local main, validated by `scripts.ci.revision`; it is not a remote event or merge candidate. Earlier `601dde8` evidence used a later base and cannot substitute for this feature-range result. No fetch or remote action occurred.

[Safe machine evidence](2026-11-13-mvp-rehearsal-9706334.json) retains exact identities, counts, correlation IDs, two policy changes and all sixty history-to-reviewed-tree mappings. Native scanner/subprocess output was captured privately or only in memory, never published.

| Full job | Actual result | Passed/failed/skipped | Seconds |
| --- | --- | --- | --- |
| backend | PASS `CI_JOB_COMPLETE` | 96/0/0 | 449.680 |
| compose | PASS `CI_JOB_COMPLETE` | 14/0/0 | 1401.193 |
| contracts-sdk | FAIL `CI_COMMAND_FAILED` | 87/0/0 collected | 1184.342 |
| web-browser | FAIL `CI_COMMAND_FAILED` | 0/0/0 collected | 96.541 |
| security | FAIL `CI_SCAN_FINDINGS` | 0/1/0 | 16.550 |

These are complete job attempts, not a claim that every step ran: fail-fast jobs stop before later checks. Original failed reports remain in ignored `.local/mvp-rehearsal-9706334/`. Actual aggregate CLI exits1, `success=false`, `CI_DISCOVERY_INCOMPLETE`: browser discovery is absent. No successful aggregate was manufactured. Direct compatibility CLI exits1 `CONTRACT_POLICY_REVIEW_REQUIRED`. A diagnostic rerun of the actual Plan3 browser entrypoint exits1 in85.742s, reporting `SDK_GENERATED_CURRENT`, `SDK_PACKAGE_BOUNDARY_OK`, then `CONTRACT_POLICY_REVIEW_REQUIRED`; browser tests do not execute. Original generic job reports do not identify their failing subprocess, so this diagnostic is separate evidence.

## Sixty history records: review evidence, no new approval

Required Gitleaks scanned both committed tree and full actual base-to-candidate history. Tree64 reviews remain accepted (60 reviewed JAR snapshots, four recomputed contract hashes). History yields sixty blocked records, all `generic-api-key`, commit `5b19690a08f9c0bcf7c3edbf3bae1f15817c312e`, file `docs/ci/candidates/keycloak26/diagnostic-runtime-jars.json`. Two real pinned scans produce identical full native-record ID multisets. Each history record maps to exactly one already approved tree ID by exact full-document SHA256, path, start/end line and rule. The evidence records actual Git blob OID and commit for each mapping. This proves the historical document bytes and finding locations equal reviewed metadata; it does not turn tree approval into history approval. No credential payload or match is needed to review the mapping.

The current adapter intentionally never adjudicates history. History record IDs differ from tree IDs because complete native records include history context. A possible future proposal must bind exact history IDs, commit/blob/document/location/rule/multiplicity, scanner binary/config and expiry; retain all other history/new/deleted-secret findings blocked. Such a change requires explicit user review and implementation/testing. No such approval or exclusion exists in this rehearsal.

Trivy PASS covers eleven normalized lock targets with six nonblocking vulnerability records; one real Surface empty-restore receipt is present. Deferred tool/license/image/SAST coverage remains OPEN. The security failure is sixty history records, not a failure of the existing64 tree reviews.

## Exact policy transition and concrete options

The unchanged preflight identifies exactly two differences from the validated base:

- `scripts/contracts/sdk_surface.py`: SHA256 `f0510e541edcf6a567801362c91045fc8025b6164ac7bf2487a57e4e45599439` → `f764d453b9f01c528936b843b73342186219e7b7b2b629a78962062c52120d39`. `prepare` also copies `tests/ClientConsumers` and checks each source directory exists. This supports historical consumer execution in materialized semantic snapshots; it does not change API contracts.
- `tools/contracts/surface/dotnet/packages.lock.json`: added, SHA256 `03eeadc5ef377c17f787ab65f41fb4c8a9c936bb7f7f4171111fdeec8a81cb46`. Authentic empty Surface graph; actual locked restore validation is separately implemented and tested. Its presence extends the preflight's Surface-policy byte inventory.

Current `compatibility.py` exposes no passing approval option. `verify_transition` requires an externally provided verification capability; JSON approval labels or synthetic test signers cannot provision it. `Plan3SnapshotEvaluator` can run immutable snapshots in an unprivileged process, but exact old c4a5ef4 compatibility source predates `evaluate_contract_snapshots`; candidate source has that API. Existing trust documentation explicitly says this old-policy case needs a separately reviewed bootstrap and must fail closed rather than rewrite the old snapshot.

Options for a separately authorized design:

1. Maintainer-reviewed MVP transition plus legacy bootstrap: approve exact old/new files, revision/tree, reviewer/expiry and real consumer evidence; add a narrowly scoped candidate-owned MVP adapter, retaining explicit absence of independent enforcement. For old semantics, a reviewed bootstrap can materialize the exact old repository and invoke its original CLI against the same immutable contracts, with no policy mutation. This avoids pretending the old source has a newer API, but needs careful input/base reconstruction and negative tests proving both evaluators see identical bytes. Maintainer scope must be explicitly extended beyond the64 secret approvals. The ordinary gate must remain blocking until that design and exact transition are approved.
2. Base-controlled bootstrap release: review and land a bootstrap/semantic API migration separately through the repository's authorized integration process, then refresh the candidate/base and run both immutable API-capable policies. This gives cleaner existing-runner reuse but needs separately authorized remote/integration work and fresh evidence; current exact-candidate results cannot transfer.
3. Provision the independent coordinator/signing path described in [trust proposal](../../ci/policy-transition-trust.md), together with a reviewed legacy bootstrap. This meets the intended hardening boundary but requires operational provisioning currently deferred by MVP scope; it still does not repair the old-policy API automatically.

Scope09 defers independent enforcement but explicitly forbids automatic policy bypass. None of these options was implemented or approved here. No base substitution, policy hash erasure, fake capability or synthetic success is proposed. Wave0/P0/RT-* remain OPEN; remote CI/enforcement and remote E2E/duplicate/tenant/outage/restore acceptance are NOT RUN.
