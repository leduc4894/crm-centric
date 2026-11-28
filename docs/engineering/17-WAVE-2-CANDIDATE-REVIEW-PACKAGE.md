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

## Additional historical CI fixture delta — review required

After the user approved the exact preceding scope, focused execution of the
new hosted `tests/CI` inventory found three failures: scanner regression tests
were interpreting the evolving checkout as an immutable historical baseline.
One scanner expected its historical candidate to be `HEAD`, another expected
all current findings to be covered by an old 68-record decision, and another
used current documents against old evidence. The corrected tests use the
previously reviewed immutable commit `788f7605ee962071b6ecdbd035a6a2e6351fda6d`;
the shared Git clone preserves the public origin because Gitleaks history IDs
include repository links. No security policy, finding or scanner suppression
was changed. Three targeted failures were reproduced before the changes; the
24 tests in the affected scanner modules passed afterward.

Because these are additional test-file changes outside the exact previously
authorized scope, an **uncommitted draft extension** pins both fixture files by
SHA-256 within the existing `bc147e4` scope and adds a fifth nested regression
that requires each exact file and rejects tampering. The previous scope's base,
expiry and all other file checks remain unchanged. Five new-scope regressions
and the combined Plan 3 suite (11/11, including eight old and five new nested
checks) passed. The exact complete revised draft is:

| Draft file | SHA-256 |
|---|---|
| `scripts/contracts/compatibility.py` | `2c099cb75dac092fcae3008f98ee6eee98f8827dc5f88af0fbddad1b6f7d4b4f` |
| `tests/Contract/test_wave2_plan3_review.py` | `526861dc448cf0b9bb189ff3664732d74514ad2a4f0ef64a149fff3051997ef6` |
| `tests/Contract/plan9_next_review_cases.py` | `7bdd7e0c856366449f264590ac2490339628113f559321dda41be28823c408e0` |
| `tests/CI/test_mvp_active_policy.py` | `fb0e41c3350d72016c378253316857bc7df56141210fd1be610662b26a65494f` |
| `tests/CI/test_mvp_history_review.py` | `be45b8043b75201df062610d8d9ef85066c99bf7333b59049d482571e41ad5a2` |

The previous approval does not automatically approve these additional files.
An authorized reviewer must approve or reject this revised exact delta before
it is committed or used by CI. Candidate-owned evaluation remains distinct from
independent enforcement and the security deferral remains unchanged.

A local dry run of the revised draft reached `CONTRACT_BREAKING_REVIEWED`,
consumer checks passed, and the SDK break remains reported; correlation
`59d0fca9-2717-4dbe-9230-96d2880034d6`. This remains a dry run of the
unapproved extension, not independent approval or hosted evidence. One
additional focused policy/toolchain group remains in progress at this writing.

The focused policy/toolchain group completed 17/17 with the pinned Node/.NET
environment. Its first invocation without that environment had two tool-related
failures; those were retained and the correct-environment run passed. No full
local suite or GitHub CI run was performed.

On 2026-11-28 the user approved proceeding with the additional exact historical
fixture delta above. This is authorization of that bounded candidate-owned scope,
not independent enforcement or a security waiver.

## Grouped CI follow-up draft — 2026-11-28

The user approved the preceding exact five-file extension; it was committed and
pushed as `d282ed3601df2342ef948e2de3a301b272bdf805`. GitHub tested merge
`5689d54fca0b112604f6c491e819380a7d394d5a` has the same tree as that head.
[Run 46106584020](https://github.com/leduc4894/crm-centric/actions/runs/46106584020)
completed: backend PASS 424/424, Compose PASS 18/18, contracts-sdk PASS 137/137;
all zero skips. The new `ci-tests` job failed one of 198 tests,
`test_policy_transition.TransitionTests.test_real_plan3_semantics_under_both_immutable_policies`,
while its 18/18 benchmark tests passed. Web-browser failed the `browser-contacts`
command; its report did not preserve a narrower cause. Security FAIL/deferred:
105 blocked Gitleaks, one blocked Trivy, and eight passing Trivy findings.
Aggregate FAIL `CI_REPORT_INVALID`, correlation
`37955b37-3158-4662-bd4e-b513e19491a5`.

The CI policy test executes `npm ci --offline` in isolated semantic workers.
A controlled clean-cache reproduction failed offline, then succeeded offline
once the exact lockfile was installed online. The proposed `ci-tests` runner
now runs locked `npm ci --ignore-scripts` and pulls the pinned .NET SDK image
before invoking the two test suites. A test failed before this change and
passed after it. This is a supported cause of the CI failure, but the precise
hosted worker exception was not emitted; it must be verified by the next run.

The contacts browser test passed 1/1 locally, and passed 1/1 again immediately
after the real-provider test also passed 1/1. This did **not** reproduce the
hosted failure; its root cause remains unknown. The draft adds bounded browser
failure diagnostics: only fixed allowlisted fixture reason tokens and locations
in `contacts.spec.ts` or `realOidcFixture.ts`, fixed command names and test
status categories can be emitted, with no response,
credential, payload, arbitrary path or assertion text. Tests cover redaction,
structured locations and symlink rejection. This improves diagnosis if CI fails
again; it is not proof that browser contacts is fixed. The CI inventory now
requires 200 CI identities and 18 benchmark identities. The grouped focused
suite passed 53/53, including the new scope and diagnostic regressions.

Security comparison against run 46056853386: all 15 changed Gitleaks opaque IDs
matched previous findings one-to-one by rule, path and value digest computed
only in memory; zero unmatched/ambiguous findings. All nine Trivy finding
IDs/verdicts were unchanged. This is no-new-finding evidence for that exact
comparison, not security clearance; raw values and fingerprints were not saved.
The default-list benchmark on `d282ed3` passed p95 109.16 ms with 241,163 valid
samples and zero errors. Search diagnostic was 1598.77 ms with 13,699 valid
samples and zero errors. Reports remain bound to that SHA.

The following **uncommitted draft** adds exact hash-pinned overrides to the
previously authorized candidate-owned scope; no scanner policy, waiver,
security verdict, base or expiry is changed. It is a new review decision; the
prior approval cannot automatically authorize this CI delta. Current SHA-256:

| Draft file | SHA-256 |
|---|---|
| `scripts/contracts/compatibility.py` | `4c2325df898207b2aaff515dd1ba63d03f885c076e729a4bf1e2f148612ae361` |
| `scripts/ci/jobs.py` | `e7ad13c71d13639fc537c99705896ce6868ed87b114b6d1600ace0723da74f50` |
| `tests/CI/test_job_reports.py` | `6eec26648b1f452e29b83d9ad7353705dc5bc641e07aa7f283af66c22d2da4a2` |
| `tests/CI/test_job_processes.py` | `edc98bfe08534f45446cb32e1ac956678911726558dd902ca95b783c1b17208a` |
| `tests/Contract/plan9_next_review_cases.py` | `4ff7643404a391c9f5f18e39b069e1bf5dec45865b7d558512f494d4ba7cfb5c` |
| `tests/Contract/test_wave2_plan3_review.py` | `cecc640728a159a2226e71965b25839ef430c28e8ec8be825dd4e891675edcb6` |
| `tools/ci/coverage.json` | `3423d85a223e0089d5564bf7ab98ac27a79a620f317b83a9407d06f205116367` |

An authorized reviewer should decide on this exact draft before commit/push;
then freeze the new SHA, rerun affected benchmark evidence, push once and
monitor the sole CI run. Candidate-owned approval is not independent enforcement.

The final grouped uncommitted draft was tested against the actual base with the
pinned local toolchain: `CONTRACT_BREAKING_REVIEWED`, consumer checks passed,
SDK break explicit, correlation `97f89f14-775c-4955-bf06-4a02f5596faa`.
This is a local execution of the unapproved candidate-owned scope, not a new
approval, independent attestation or hosted result.

On 2026-11-28 the user approved proceeding with the exact grouped CI follow-up
scope above. This authorizes only that bounded candidate-owned scope; it is not
independent enforcement or a security waiver.

## Web readiness follow-up — exact draft awaiting decision

The user approved the preceding CI scope, committed and pushed as
`4d0ab900dfeef2375504bda3de806b6f417d793a`. In
[run 46109754682](https://github.com/leduc4894/crm-centric/actions/runs/46109754682),
`ci-tests` passed 218/218 (200 CI, 18 benchmark), zero skips; web-browser
failed in Plan 3 `frontend-test` at `src/contacts/ContactAttributes.test.tsx`.
No specific case was published. Five isolated local runs each passed 36/36, so
the exact hosted failure is still unknown. Backend and Compose passed;
contracts-sdk was running when this section was prepared; security FAIL/deferred.

The existing test helper could dispatch a change or click while a control was
still disabled. A deterministic delayed-initial-read regression demonstrated
this behavior (RED). The draft makes the test helper wait for the control or
button to be enabled before dispatching, matching real user interaction, and
retains an explicit delayed-read regression (GREEN). This changes test support
only, not product UI. Three affected frontend test files passed 60/60, zero
skips; targeted typecheck and lint passed. This proves the isolated readiness
issue and fix but does **not** establish that it caused the hosted failure.

A bounded diagnostic now identifies only source-reviewed ContactAttributes
test titles if Plan 3 fails again. CI coverage explicitly requires the boolean
scalar case and the delayed-read regression, plus the new diagnostic test.
The grouped CI/contract focused suite passed 55/55; tests reject tampering of
each exact UI fixture, CI job, coverage inventory and review helper. This
section's changes are a new uncommitted candidate-owned scope draft; they do
not waive security or alter the contract, and the earlier exact approval does
not automatically authorize these additional changes.

Benchmark on `4d0ab90`: default PASS p95 141.28 ms, 194,437 valid responses,
zero errors; search diagnostic p95 1913.41 ms, 12,310 valid responses, zero
errors. Both used 50 users/50,000 contacts, warmup 60s and measurement 300s.
The GitHub merge and head had the same tree. Security comparison against run
46106584020 found 15 changed opaque Gitleaks IDs matched one-to-one to prior
findings by rule/path/value digest in memory, zero unmatched; all Trivy IDs and
verdicts unchanged. No secret values or digests were saved; security remains
FAIL/deferred. Reports and environment are retained in `.local/plan9-final3-runtime/`.

Exact SHA-256 of the proposed changed files:

| Draft file | SHA-256 |
|---|---|
| `apps/crm-web/src/contacts/ContactAttributes.test.tsx` | `63c3236ec4ef6a36c456b0096bddb38845507e62c63262b916e1281914850f93` |
| `apps/crm-web/src/contacts/recordTestSupport.tsx` | `7c3a67e972b2d2c53a60fa6a34b9eb32c923be8b9357c3cea09fc5e305b7a173` |
| `scripts/ci/jobs.py` | `80b4d6a0c5e703389cfd3a89e2cf985bb1a4d5dd10f2efcdba0f075edce43022` |
| `scripts/contracts/compatibility.py` | `3edc388675cc923d66e309da626108e6349a4401d77f05642f56a133a6c7bd15` |
| `tests/CI/test_job_processes.py` | `d31ffd3f0d1595a9f249de603a59e35ff795c8cf4b0eed34a06deaab19f5f012` |
| `tests/Contract/plan9_next_review_cases.py` | `e8ed52733e12cbcb530e1f8d064d7186da6eb10369b4e5706ddf873c89b352f0` |
| `tests/Contract/test_wave2_plan3_review.py` | `010ebc1cfe359363459f2760f642e7f3d58e6dc7088b64d2a548014f667716c1` |
| `tools/ci/coverage.json` | `18c5a2aac67e04fd2b173386d8c7860345f532586345ba4757a7acd97eb25974` |

An authorized reviewer should approve or reject this exact new scope before commit or push; candidate-owned enforcement is not independent.

Run 46109754682 subsequently completed: backend PASS 424/424, Compose PASS
18/18, contracts-sdk PASS 137/137, `ci-tests` PASS 218/218, all zero skips;
web-browser FAIL at Plan 3 frontend-test in `ContactAttributes.test.tsx` and
security FAIL/deferred. Aggregate FAIL `CI_REPORT_INVALID`, correlation
`4ee0f383-9603-4406-9379-b6ef7d1f359b`. No additional hosted job remains active.

The final draft's actual-base local gate completed as
`CONTRACT_BREAKING_REVIEWED`; consumer checks passed and the SDK break remained
explicit (correlation `a7823b80-9e2c-407a-aa33-92b4e4def930`). This is a
candidate-owned draft evaluation, not authorization or independent enforcement.

On 2026-11-28 the user instructed proceeding until non-security CI is green,
with security deferred, in response to the exact web-readiness draft above. The
user's decision authorizes proceeding with this bounded candidate-owned scope;
it is not independent enforcement or a security waiver.
