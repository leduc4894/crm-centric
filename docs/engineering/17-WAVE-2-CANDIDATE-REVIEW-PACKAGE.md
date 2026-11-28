# Wave 2 candidate compatibility review package

Status: **review required; no approval or compatible verdict**. Candidate PR head
`71959b35c08738d16a03e1f4f54e4f0b21075ce0`, actual base
`db04aeaecd7f4b6d083335a71418788d3c4561ca`, GitHub-reported merge
`7dee3cfa002e64d75f00e0d88aee292938462d43` (verify run identity before use).
[PR #4](https://github.com/leduc4894/crm-centric/pull/4),
[run 46031836021](https://github.com/leduc4894/crm-centric/actions/runs/46031836021).
This document is a review aid; a later product, test, contract or policy change
requires a fresh scope and does not inherit any decision.

Before the latest fixes, the actual-base command on the `1cdf1c8` worktree returned `CONTRACT_POLICY_REVIEW_REQUIRED`
(correlation `65620b5e-5844-4377-89f3-8fce76e26b08`) at policy identity
preflight, before semantic comparison. It does not establish either compatibility
or a reviewed break. [Prior Plan 3 review](15-WAVE-2-PLAN-3-CONTRACT-REVIEW.md)
covered candidate `8e62f5282aa14cdfca746f2aa78ec05b59ffcb86`, not this candidate.
The candidate-owned MVP evaluator and its existing scope/expiry must not be
rebaselined merely to make CI green; any new approval is an explicit maintainer
decision, not independent enforcement.

Review the complete actual-base diff and consumer impact, in particular:

- [ADR-020](../architecture/10-ADR-020-CONTACT-EMAIL-UPSERT.md),
  [ADR-021](../architecture/11-ADR-021-CONTACT-UPSERT-IDENTITY-DRAFT.md),
  [SDK consumer migration and rollback](../operations/07-CONTACT-SDK-MIGRATION.md),
  and the existing [contact review](13-WAVE-2-CONTACT-CONTRACT-REVIEW.md).
  Upsert 200 versus 201 and identity preconditions have documented behavioral and
  SDK impact; this cannot be classified as compatible merely because a schema
  change appears additive. Review the actual ADR state and approvals.
- Since the prior reviewed candidate, the OpenAPI adds the successful timeline
  note response ETag; timeline endpoints, cursor/store, two migrations, role
  grants and packaged consumer evidence also changed. Inspect the full diff:
  `git diff 8e62f5282aa14cdfca746f2aa78ec05b59ffcb86..71959b35c08738d16a03e1f4f54e4f0b21075ce0 -- docs/contracts scripts/contracts src/Platform.Api src/Platform.Infrastructure tests/ClientConsumers tools/contracts`.
- Compare full actual base using `git diff db04aeaecd7f4b6d083335a71418788d3c4561ca..71959b35c08738d16a03e1f4f54e4f0b21075ce0 -- docs/contracts scripts/contracts sdks tests/ClientConsumers tools/contracts`; include the SDK surface evaluator policy delta, complete input inventory and old/new historical consumer checks. The most recent completed hosted contract suite passed 137/137 on the earlier head `1cdf1c8`; this is not evidence for the new head.
- Require a candidate-specific, authorized review path and real actual-base
  OpenAPI/schema and old/new SDK/consumer results. Preserve a reviewed-breaking
  classification if applicable, fail closed on any unexpected difference, and
  report whether enforcement is independent. Do not claim a pass until those
  checks actually run. Security findings and the full Wave 2/P0 exit are separate.

## Provisional evaluation, not an approval

After the user authorized preparation, a disposable shared clone at exact head
`71959b35c08738d16a03e1f4f54e4f0b21075ce0` was used to simulate the existing
scoped MVP evaluator with its candidate/tree constants changed **only in that
clone**. The real gate, policy, trusted baseline and running CI were unchanged.
The simulated scope is an assumption for obtaining evidence, not a maintainer
review or an accepted gate result. `--base-ref db04aeaecd7f4b6d083335a71418788d3c4561ca
--mvp-reviewed-policy` returned the evaluator's reviewed-breaking branch with
correlation `e3f69d3e-e311-476d-8b37-f1a95c4588c3`: TypeScript and .NET SDK
surfaces incompatible, both historical consumers passed, packaged contact
consumer checks passed. Raw safe aggregate is local at
`.local/plan9-provisional-evaluation.json`. The evaluator calls its simulated
branch reviewed; that label does not mean this candidate has been reviewed or
approved. Confirm exact diffs, semantic outputs and review scope independently
before choosing an activation path; do not carry this simulated result to another
candidate. No deploy, consumer rollout, security waiver or acceptance approval.

## Candidate-scoped implementation in progress

Following the request to continue, a candidate-owned MVP scope is being prepared
for the exact head/base above, retaining the Plan 3 scope separately. It is not
independent enforcement. Scope tests exercise base, target, mode, expiry, changed
and untracked product files, coverage inventory, symlinks and snapshot tampering.
A real actual-base comparison and final hosted candidate run must still be
recorded below before any success claim. No security finding is waived.

Focused implementation check: the exact-base gate on the prepared worktree
returned `CONTRACT_BREAKING_REVIEWED` (correlation
`670a3090-61b4-471b-a248-22be8a86b132`): TypeScript and .NET SDK surfaces
incompatible, both historical consumer checks and packaged contact consumer
checks passed. This is a local result of candidate-owned code, not independent
enforcement, deployed compatibility, or evidence for a later product tree.
Scope/preflight tests for the new tree passed 6/6; the previous scope and new
scope tests together passed 16/16 before the final additional preflight case.
Hosted CI must still evaluate the committed candidate. The user asked to proceed
with this bounded implementation after the candidate review package; scope is
limited to this exact product tree, base, MVP mode, expiry and unchanged
inventory. Full branch review and P0 acceptance remain open.

The later diagnostic-only CI changes to `scripts/ci/jobs.py` and
`tests/CI/test_job_processes.py` are permitted only at their exact pinned hashes;
all other product files stay bound to the reviewed tree. The Plan 9 scope
regressions are executed inside an existing required Plan 3 review test, with
its helper pinned by hash. This preserves the existing named prerequisite skip
in the intentionally Git-less CI simulation; the real suite must execute those
regressions and rejects unexpected skips. These are candidate-owned checks, not
an independent review or a security waiver.

A subsequent CI run identified `contactQueries.test.tsx` as a frontend test
fixture failure: it rendered a routed component without RouterProvider. The
fixture now provides a real router; its exact test-file bytes are also separately
hash-pinned by the scope. No application contract or runtime implementation was
changed. Scope tests reject changes to each pinned exception. This additional
test-only delta remains candidate-owned and is not independent enforcement.

The next CI run exposed a frontend leak scan failure: Vite inserted an absolute
checkout path in a development style element. The two exact frontend changes
move the existing stylesheet link into `index.html` and remove its JS import;
they are pinned separately in the candidate-owned scope. This changes frontend
asset loading and must be included in review; it does not change the contact API
contract. Local leak scan and browser stylesheet loading passed. The final hosted
candidate must still be tested; this is not an independent review or security
clearance.

## Exact candidate for renewed review — 2026-11-28

**Review required; no new approval has been applied.** Candidate
`b19189798c79230baf1f77c39ecc136977941d43`, actual base
`db04aeaecd7f4b6d083335a71418788d3c4561ca`. Since hosted head
`51a75d48bf1ae32fd46cecc886cf841a53aaea15`, there are no OpenAPI, schema,
SDK, migration or application-runtime changes. The delta adds the benchmark
harness, deterministic seed, a PostgreSQL integration test and runbook; adds
`ci-tests` to the workflow and aggregate; inventories 198 CI and 18 benchmark
test identities; and provisions the pinned scanner required by existing CI
tests. Both Python suites are attempted even if the first fails. The new job
requires zero skips and missing/failed jobs remain aggregate failures. A historical contract-review fixture was subsequently bound to its old
immutable snapshot so the new CI inventory does not break old-scope regression
tests. The
seed test is also included in backend's non-Compose integration selection.

The existing candidate-owned scope pins the earlier tree and coverage hash.
Actual-base evaluation of preceding candidate `f5a34ed` returned
`CONTRACT_POLICY_REVIEW_REQUIRED` (correlation
`fdabbfbf-d411-43ac-8243-67b320b487e5`); no prior approval was extended.
An authorized independent reviewer must assess the exact delta, test discovery,
setup and reporting, contract/consumer evidence and these hashes. Any scope
update must retain the earlier base, expiry, fail-closed drift behavior, both
SDK surface checks and the explicit `CONTRACT_BREAKING_REVIEWED` designation;
it must not treat a reviewed break as compatible or change security outcome.
The candidate-owned mechanism itself is not independent enforcement. This
package does not authorize a self-approval, and later product/CI/test changes
require a fresh scope.

Focused evidence: workflow/job tests 18/18, aggregate/completion/job tests
23/23, benchmark unit tests 18/18 and real PostgreSQL seed test 1/1; the historical
contract-review fixture passed 11/11 after a 6-failure/2-error RED. The
[benchmark report](16-WAVE-2-EXIT-VERIFICATION.md) records the candidate-bound
default PASS and separate slow search diagnostic; the newly added hosted job
has not run. Latest earlier hosted non-security jobs passed, while security and
aggregate failed; security has since been deferred by user request, with no
new scan. Plan 8's prior boolean timing failure remains unexplained despite
subsequent focused and hosted passes.

SHA-256 for the exact changed code, tests and CI policy at this candidate:

| Path | SHA-256 |
|---|---|
| `.github/workflows/wave0.yml` | `10b93b9e445d0775424b2986359b4e8212c043331c7ff9164075e52c03892b5b` |
| `scripts/ci/aggregate.py` | `d204ec6f2cfaacf912eac0fb173ea5f347307c3d44987ac80bcb2499dd388395` |
| `scripts/ci/completion.py` | `ee49fd59986061271919b6798c981c63ab5e730a1f9c605b90e5a7d54612a5e6` |
| `scripts/ci/jobs.py` | `9f3c29d5fc085f1c0b754efd1620e42a6f104589eb95a55875517115368deda0` |
| `scripts/ci/setup.sh` | `68780c167d6269020063184a25a5299c0cf393a3a3d41fc7cb8feb3f18a8a81f` |
| `scripts/ci/workflow.py` | `21cdd9b9b4677b20c0bb3ccc315d8320c1ed58cf386c3bbb95189472019b1257` |
| `tests/CI/test_job_reports.py` | `0a0f1f88799caa656cf1067417fe4f080a79dd8709885351508e0a9f6e034a76` |
| `tests/CI/test_workflow.py` | `8fdabde8901b182d8a73736d2c86f4cedc88649899b79e3652bc37052091e6d2` |
| `tools/ci/coverage.json` | `1d1a7cd007e9f5e58c3c38abaabedeb9f53971d26289c2aabb20bb65a779f978` |
| `scripts/benchmarks/contact_list.py` | `78b948a06b06d8570734bee12cee3df1119ce7f3c5470e2ea0003e03ed52e4a4` |
| `scripts/benchmarks/seed_contacts.sql` | `3e6d58553c9376ca60d55ac9122259eb26036c98f6d10e2131654c4e28a0a4ee` |
| `tests/Benchmarks/test_contact_list.py` | `9e5458f7a542cb1b9226f73a1a0cd5ff4b9d9ba017ac7a2e6565513f8d3957d8` |
| `tests/Integration/BenchmarkSeedTests.cs` | `55abe873166a7ef62834f71d20e1958845bb8a1cc3878bd9a189355955473f3d` |
| `tests/Contract/test_wave2_plan3_review.py` | `b81e90bf6c991ce4bfecada5c72a2ecefcf295d2d8372eb8b52bcd2072d22f55` |

## Draft bounded scope ready for decision

The concrete **uncommitted draft** extends the existing candidate-owned MVP
review to immutable snapshot `bc147e4dae9dbed662bd62a7c721774d34ca1e9f`
(tree `75cecd22475b6f994efd027c3ec1c632923cd707`), while retaining the old
scopes. It binds the new CI inventory by SHA-256, pins a new regression helper,
keeps the existing base and expiry, rejects added/modified files and forged CI
revision identity, and materializes the immutable new snapshot for the real
OpenAPI/SDK/consumer checks. It does not waive security or call a breaking
contract compatible. This is candidate-owned policy: even if authorized, it
is **not independent enforcement**.

| Draft file | SHA-256 |
|---|---|
| `scripts/contracts/compatibility.py` | `5fdb1ea04284192cb968f23d1d86984c3db739e461052b147c3410b4d7689632` |
| `tests/Contract/test_wave2_plan3_review.py` | `c073658ec37b4534f1679a63e7ddcbfdd79eac9bc22b9d4e2a77c5ffd121a61f` |
| `tests/Contract/plan9_next_review_cases.py` | `9c237fc617607e309a64b373c0117365c327adcfd75d3088538f29d322f1e24d` |

Four new bounded-scope regressions failed before the draft and passed after;
the existing Plan 3 suite passed 11/11 including eight old and four new nested
scope cases. A local **simulation using the unapproved draft** reached
`CONTRACT_BREAKING_REVIEWED`, correlation
`89f147c8-c3ec-4131-bf82-2074dde80b2b`; historical SDK and packaged consumer
checks ran and the SDK break remains reported. This is evidence about the draft,
not an approval, hosted result, independent attestation or compatible verdict.
The initial simulation without the Node library path failed with
`SDK_TOOLCHAIN_REVIEW_REQUIRED`; rerunning with the project's pinned toolchain
produced the simulated result above.

Decision requested: an authorized reviewer may approve or reject this exact
draft and snapshot after reviewing the diff, CI changes, tests and evidence.
If approved, commit it, freeze the resulting new candidate, repeat any required
candidate-bound measurement and run a single hosted CI while monitoring; report
security as deferred and the aggregate according to its actual result. No
scope is authorized by this document alone.

On 2026-11-28 the user approved proceeding with the exact draft described above.
This authorizes this bounded candidate-owned MVP scope; it does not provide
independent enforcement, waive security, or approve later product/CI drift.
