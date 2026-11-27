# Wave 2 branch handoff — 2026-11-27

Objective: create the `wave-2` branch at the user's request.

Status: created and switched to local branch `wave-2` from `main` at `db04aea`. No Wave 2 implementation was started. See [Wave 1 CI handoff](2026-11-27-wave-1-pr3-ci-handoff.md) for the deferred security failure and outstanding acceptance conditions; creating this branch does not establish acceptance or authorize bypassing those conditions.

Changes: this handoff is uncommitted. No application files changed. No remote branch was created.

Verification: `git switch -c wave-2` PASS. Tests not run (branch creation and documentation only).

Next: assess the outstanding Wave 1 and Wave 2 entry conditions before implementation.

## Wave 2 scope discussion

Reviewed the backlog, acceptance matrix, contact state and data model to answer the user's question. Recommended five implementation/verification plans: contact contract and CRUD; search/filter/pagination; tags and custom fields; timeline and contact UI; exit verification and benchmark. These are a proposed breakdown, not approved or implemented plans. First assess remaining applicable Wave 1 non-security requirements on the appropriate candidate; security remains FAIL/deferred under the user's existing decision. Remote DemoModule and recovery/restore are later P0 acceptance requirements, not automatically Wave 1 exit criteria. No tests run for this documentation-only review.

## Wave 2 design spec

User requested a spec and clarified that plan-specific related tests run locally; the full suite runs only at Wave 2 exit on GitHub CI, not locally. Drafted [Wave 2 design](../superpowers/specs/2026-11-27-wave-2-crm-contacts-design.md), including the prototype as UI source, five proposed plans, contract/security boundaries, testing and exit evidence. Spec awaits user approval; no implementation or plan execution started. Reviewed backlog, architecture, acceptance matrix, CRM OpenAPI, prototype and frontend package. No tests run in this documentation-only session. Next: user review/approval of spec, then write detailed plans; first assess applicable non-security Wave 1 entry conditions. Security remains FAIL/deferred and P0 OPEN.

Documentation verification: initial `git diff --cached --check` reported trailing whitespace in the spec; corrected before rerunning. Test suite not run.

## Plan 1 authoring

User requested Plan 1 after reviewing the design direction. Created [Contact CRUD plan](../superpowers/plans/2026-11-27-wave-2-plan-1-contact-crud.md), covering entry-evidence assessment, contract/event intent, migration and transactional journal, tenant-scoped CRUD, concurrency and focused verification. The plan preserves the user's instruction: related tests local; full suite at Wave 2 exit on GitHub CI. Plan is proposed, not executed; no source code or contract changed. The prior spec commit is `d02f869`; this handoff update and plan are uncommitted. No tests run for this documentation-only session. Next: review the plan; when implementation is authorized, evaluate Wave 1 entry conditions before code. Security remains FAIL/deferred, P0 OPEN.

## Commit request

User requested committing all current changes on `wave-2`: this handoff update and the Plan 1 document. No implementation or tests were run. Verification: `git diff --check` passed before staging; staged whitespace and final status checked as part of commit. The plan remains unexecuted and the Wave 1 entry assessment remains the next prerequisite.

## Plan 1 Task 0 entry assessment

Objective: implement [Wave 2 Plan 1](../superpowers/plans/2026-11-27-wave-2-plan-1-contact-crud.md) using SDD on the current branch. Inspected clean `wave-2` at `0ff54664ef1f753ac211c2b554bb5e3fb5eb085c`; `2c2851b8077a645e1c88b2c4e2dfd687e18ee3c7` is an ancestor. `git diff --name-only 2c2851b HEAD` shows only README, documentation and the standalone prototype; no application, test, contract, CI, or dependency changes. This supports comparing code scope, but does not transfer a hosted verdict to the new SHA.

| Applicable Wave 1 condition | Evidence and result | Remaining status |
| --- | --- | --- |
| Backend, integration, token and SQL/RLS | [PR run 45917879649](https://github.com/leduc4894/crm-centric/actions/runs/45917879649), head `2c2851b`: backend PASS 261/261, per [CI handoff](2026-11-27-wave-1-pr3-ci-handoff.md); hosted job confirmed success. | No completed hosted run on `0ff5466`; evaluate full RT-IAM-01 / RT-OIDC-01 evidence and remaining gaps against [exit traceability](../engineering/11-WAVE-1-EXIT-VERIFICATION.md). |
| Compose / operational tests | Same PR run: Compose PASS 18/18; hosted job confirmed success. | No completed hosted run on current SHA. Production ingress and issuer configuration are not verified. |
| Contracts / SDK | Same PR run: contracts-sdk PASS 93/93; hosted job confirmed success; reviewed ADR-019 break is scoped, not a general compatibility verdict. | Actual issuer/consumer migration and readiness remain OPEN in [exit verification](../engineering/11-WAVE-1-EXIT-VERIFICATION.md#final-candidate-exit-decision--2026-11-26). |
| Web and real-provider browser | Same PR run: web-browser PASS 125/125 including eight browser cases, per CI handoff; hosted job confirmed success. | Production CSP and ingress, and real issuer/consumer migration are not established; RT-OIDC-01 remains OPEN in exit verification. |
| Independent review / non-security acceptance | [Exit verification](../engineering/11-WAVE-1-EXIT-VERIFICATION.md#final-candidate-exit-decision--2026-11-26) records risk-focused review and outstanding issuer/consumer readiness. | No evidence found closing all applicable Wave 1 non-security acceptance requirements on the appropriate candidate; US-01 and US-02 remain OPEN in [matrix](../engineering/03-ACCEPTANCE-MATRIX.md). |
| Aggregate and security | PR run aggregate FAIL `CI_JOB_NON_SUCCESS` because security failed; security FAIL/deferred by existing user decision. | Neither aggregate nor security is PASS. Deferral does not waive other applicable conditions or close P0. |

Read-only `gh run list --branch main --limit 8` showed runs on `5f6dcbc`, `a66cc73`, and `db04aea` cancelled; `gh run view 45917879649` confirmed the four non-security jobs passed and security/aggregate failed. There is no observed passing hosted execution on current `0ff5466`. Tests not run; this assessment does not run the full suite locally. No source, contract, workflow or baseline changed.

**Blocker:** Task 0 explicitly requires stopping before Task 1 if applicable non-security evidence is missing. The documented remaining Wave 1 acceptance and candidate evidence cannot be declared passed from the PR job results alone. Plan 1 implementation has not started. Next: establish the applicable outstanding Wave 1 acceptance and appropriate candidate evidence, then reassess Task 0; future P0 remote module, event duplicate, recovery and restore drills remain separate and are not automatically Wave 1 gates. Branch `wave-2`, pre-assessment HEAD `0ff5466`; only this handoff is uncommitted.

## User decision to proceed

The user confirmed that the commits since the Wave 1 CI candidate only add documentation and that Wave 1 passed apart from security, and explicitly directed implementation to proceed. This supersedes the Task 0 stop above for starting Plan 1. Historical hosted results and the security FAIL/deferred status remain as recorded; this decision does not make the aggregate or security gate pass or close P0. Work continues on `wave-2`.

## Contact email decision while implementing

The user changed the product rule: email is unique per tenant and a create request with an existing email updates that contact, applying only supplied fields. This supersedes the prior duplicate-email assumption, but does not identify a contact with an authenticated identity. Task 1 committed `a3ad0fd`; Task 2 committed `3b11708` and review fix `aa47be1`. Task 1 contract tests 3/3, Task 2 ContactSchemaTests 10/10 and MigrationTests 5/5 passed per SDD reports. Task 3 was interrupted: initial endpoint tests RED 3/3 (routes absent); a subsequent run was cancelled, no GREEN. Uncommitted Task 3 source/tests still embody the old duplicate-email rule and are incomplete; do not treat as working implementation. Handoff and draft [ADR-020](../architecture/10-ADR-020-CONTACT-EMAIL-UPSERT.md) are uncommitted. The existing plan explicitly requires approved ADR, migration plan and consumer tests before a breaking API diff; the new rule changes POST semantics and must be reconciled with ETag concurrency. The user chose serialized upserts: the request processed later overwrites only fields it supplies; PATCH/DELETE ETag semantics remain to be preserved. Next: resolve concurrency, finalize contract/ADR/migration and consumer tests, update spec and plan, then resume TDD and review. Security remains FAIL/deferred; no Wave 2 or P0 exit claimed.


## ETag correction

User subsequently corrected the concurrency choice: updates use ETag. Draft ADR and plan now require a matching strong If-Match for POST when email matches an existing active contact; missing 428, stale 412, malformed 400, and no mutation/journal on rejection. The earlier serialized last-writer-wins description is superseded. New contacts do not need If-Match. Contract and consumer tests must be updated before implementation; preserve Task 3 unfinished work.

Consumer test revision: `PATH="$PWD/.venv/bin:$PATH" python3 -m unittest discover -s tests/Contract -p 'test_contact_upsert_consumer.py' -v` RED 4/4: old POST contract lacks the 200 response and optional conditional If-Match header. This validates expected contract gaps only, not runtime no-write or concurrency. POST needs its own optional parameter because the shared IfMatch parameter is required. Draft ADR and amendment require review/approval per Plan 1 before breaking contract implementation.


## User approval of upsert amendment

User approved proceeding with ADR-020, migration plan and consumer tests after ETag correction. Implement contract-first and retain real compatibility gate outcomes; security remains FAIL/deferred.

## Plan 1 contact CRUD and approved email-upsert amendment

Objective: execute [Plan 1](../superpowers/plans/2026-11-27-wave-2-plan-1-contact-crud.md) and its [approved amendment](../superpowers/plans/2026-11-27-wave-2-plan-1-upsert-amendment.md) on `wave-2` using sequential SDD. User confirmed the post-CI Wave 1 commits were documentation-only, directed proceeding with security deferred, approved active-email uniqueness per tenant and upsert of supplied fields, then required strong ETag for updates. [ADR-020](../architecture/10-ADR-020-CONTACT-EMAIL-UPSERT.md) records the decisions. This supersedes this handoff's earlier Task 0 stop; it does not change security, aggregate or P0 verdicts.

Implementation commits: `a3ad0fd` contract/event intent; `3b11708`, `aa47be1` schema, journal and schema review fix; `f2ecd10`, `509c190`, `34ed74d` upsert contract, versioned SDK and consumer gate; `593e089` email uniqueness migration; `31bd56a`, `e7afd44` create/get/upsert and If-Match fix; `72aeb2b` PATCH/DELETE and concurrency. The API scopes tenant through verified membership and staff, enforces database normalization `lower(btrim(email))` with collation and ordinary-space semantics, requires ETags for mutation, and commits contact/audit/outbox together. There is no delivery consumer. See [migration guidance](../operations/06-CONTACT-MIGRATION.md) and [SDK migration](../operations/07-CONTACT-SDK-MIGRATION.md). Existing concurrent tests launch competing requests but do not force each database lock order.

This documentation task started at HEAD `72aeb2b` with this handoff, ADR and amendment plan uncommitted. It clarifies OpenAPI for a supplied strong If-Match with no active match (412), regenerates CRM artifacts, updates contract test, data model, access/state, approved spec, ADR, amendment plan, migration and acceptance matrix. The final documentation commit is the next commit after `72aeb2b`; inspect `git log` for its SHA. No deployment, push, merge, CI policy or trusted baseline changed.

Verification on `72aeb2b` plus the changes in this documentation task:

- Contract RED: `PATH="$PWD/.venv/bin:$PATH" python3 -m unittest discover -s tests/Contract -p 'test_contact_upsert_consumer.py' -v` failed one of six because the no-match precondition was undocumented. GREEN: `PATH="$PWD/.venv/bin:$PATH" python3 -m unittest discover -s tests/Contract -p 'test_contact*py' -v` PASS 9/9, 0 skips.
- `timeout 210s env PATH="$PWD/.local/ci-dotnet:$PATH" dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~ContactSchemaTests|FullyQualifiedName~ContactEmailSchemaTests|FullyQualifiedName~ContactEndpointsTests' --logger 'console;verbosity=minimal'`: PASS 35/35, 0 skips on PostgreSQL Testcontainers. `ContactEndpointsTests` includes the partial concurrency tests. Existing CA1416 warnings from ImageEvidenceSecretTests appeared during build.
- With pinned Node 24.22.2, `.venv`, `.local/ci-dotnet` and `.local/plan3-runtime` library path: `python3 scripts/contracts/generate.py` PASS `SDK_GENERATED`, correlation `9af1979d-2353-4ec5-b218-e53e7e2a4632`; `python3 scripts/contracts/check.py` PASS `SDK_GENERATED_CURRENT`, correlation `c2e57bcc-a5ad-4a46-9e8c-aa8cf9459fd3`; `timeout 180s bash tests/ClientConsumers/run.sh` PASS `TYPESCRIPT_WIRE_OK`, `DOTNET_WIRE_OK` for both old and new consumers, `NEW_UPSERT_CONSUMER_OK`, `SDK_PACKAGE_BOUNDARY_OK`, `PACKAGED_CONSUMERS_OK`. Package build printed workload and package-readme warnings.
- Same environment `python3 scripts/contracts/compatibility.py --base-ref db04aeaecd7f4b6d083335a71418788d3c4561ca --mvp-reviewed-policy`: FAIL `SCHEMA_COMPATIBILITY_REVIEW_REQUIRED`, correlation `66aba80b-9bbb-440d-95ab-7022f4397c8d`. The added `contact-event-intent.schema.json` is absent at the actual base; gate stops before OpenAPI/SDK checks. User approval of ADR-020 is not an implemented schema-review exception or a compatibility PASS. Do not self-rebaseline, move the schema to hide it or weaken the gate. Actual consumer readiness and release remain unproven.
- Full suite not run locally per user instruction; it belongs to Wave 2 exit on GitHub CI. No benchmark or production/deployment test ran. Security remains FAIL/deferred and aggregate is not PASS; US-03/RT-CRM-01/performance, Wave 2 and P0 remain OPEN.

Next: independent whole-branch review, including consumer migration and exact lock-order/concurrency limits; resolve compatibility governance under explicit authorization without bypassing the real base; then Plan 2 search/filter/paging, Plan 3 tags/fields, Plan 4 timeline/UI and Plan 5 Wave 2 exit/full CI/benchmark. P0 still requires remote DemoModule E2E, concurrent duplicate-event, tenant, outage/recovery and restore drills. Before handoff, check final commit and working-tree status; do not claim unrun evidence.
