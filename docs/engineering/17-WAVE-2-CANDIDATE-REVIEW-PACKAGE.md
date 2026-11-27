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
