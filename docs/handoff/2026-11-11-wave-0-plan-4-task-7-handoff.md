# Wave 0 Plan 4 Task 7 handoff

## Objective, authority and status
Implement required aggregate, real security entrypoint, isolated diagnostics and
negative/local rehearsal evidence. Parent assigned current checkout, local commits,
no worktree/subagents/remote changes. Starting clean at914f322 on branch
`feat/wave-0-plan-4-ci`; exact starting SHA verified in Task7 report. Read AGENTS,
Task7 brief, spec supply-chain/failure/acceptance sections and Task4–6 handoffs.
Using TDD and verification-before-completion skills. No new approvals, exceptions,
suppressions, remote writes, branch protection changes or independent trust.
Implementation is reviewable; Task7/Wave0/P0 acceptance remains BLOCKED.

References: [spec](../superpowers/specs/2026-11-11-wave-0-plan-4-ci-wave-exit-design.md),
[plan](../superpowers/plans/2026-11-11-wave-0-plan-4-ci-wave-exit.md),
[CI limits](../../tools/ci/README.md). Detailed ignored task report:
`.superpowers/sdd/2026-11-11-wave-0-plan-4-ci-wave-exit/task-7-report.md`.

## Changes
- Explicit five same-run named summary downloads; platform needs outcomes remain
  authoritative. Missing/skipped/cancelled/failed jobs and absent/duplicate/wrong
  identity/empty evidence fail. Whole workflow cancellation cannot manufacture a
  scheduled aggregate success; remote cancellation is not yet demonstrated.
- Completion helper captures setup/action/gate outcomes into safe diagnostic
  envelope; strips unknown output/payload, preserves only validated run-bound
  reports, fails if gate absent/skipped. Third-party missing reports never pass.
- Security placeholder replaced with actual repository Semgrep/Gitleaks/Trivy and
  guarded source inventory. Only complete pass scans plus inventory without gaps
  can succeed. Summary validator requires nonempty exact-run scan/source evidence.
- Dedicated pinned Python3.12.12 action for hash-locked Semgrep install, without
  altering pinned Python3.15.2 job runtime. Hosted availability unproven.
- Workflow lint rejects job privileges/environment/secrets overrides, repository
  secrets/PR expression interpolation, caches/unknown actions and broad artifact
  paths/retention. Only normalized reports and guarded source inventory publish.
- Existing unconditional independent trust block remains. Candidate-owned code
  and measured launcher hashes do not create independent authority.

Changed: `.github/workflows/wave0.yml`; scripts/ci/{completion,security,workflow,
summary}.py; tests/CI/{test_completion,test_security_job,test_workflow,
test_aggregate}.py; tools/ci/README.md; this handoff. No public API/schema/database
change. Rollback: revert Task7 commit; no data migration required.

## Actual verification checkpoint
- RED workflow9failures: eight unsafe mutations accepted and missing completion/
  security wiring. Subsequent RED: missing dedicated Semgrep runtime.
- RED completion missing module; later unsafe argument parser stderr.
- RED security missing module; later forged-green summaries with missing/zero/
  wrong-run scanner evidence accepted. Implemented then reran targeted tests.
- Latest focused system Python3.12 checks: workflow9 PASS, aggregate7 PASS,
  completion3 PASS, security3 PASS; all0skips. These are wrapper tests, not service
  acceptance or independent trust verification.
- `python3 -m scripts.ci.workflow`, `.local/ci-tools/actionlint .github/workflows/wave0.yml`,
  py_compile changed modules and `git diff --check`: PASS.
- Full pinned Python3.15.2 CI discovery RUNNING at checkpoint; result pending.
- Exact committed-SHA integration/security rehearsal PLANNED after code checkpoint
  commit, not yet run. Final results appended below; no pass claimed prematurely.

## Carried blockers / next steps
Task4 original backend failed CI_COMMAND_FAILED950.6699s and Compose failed
CI_COMMAND_FAILED2494.0634s, both0collected cases. Separate historical diagnostics
and old browser/contracts greens do not replace those failed jobs or prove this
SHA. Task5 real SAST/dependency/license/secret findings, Task6 images API/migrator/
Keycloak BLOCKED and PostgreSQL INCOMPLETE UNKNOWN remain. Source inventory has
10locks/444components and tool/unlocked coverage gaps; no scan clearance implied.
Fresh hosted scanner retrieval previously failed; hosted runtime/install remains
unproven. Independent trust CI_TRUST_NOT_PROVISIONED remains blocking. Remote PR,
main push, fork scheduler/cancel/setup/timeouts and enforcement NOT RUN. Merge
queue enablement UNKNOWN/OPEN; do not enable until merge_group event/revision
validation is implemented and reviewed. Remote DemoModule, duplicate/isolation,
outage/recovery and restore/P0 remain OPEN.

Next: finish full CI; commit code; rehearse actual scanners and real stack from
exact immutable source; retain reason/count/duration and distinguish failed gates
from diagnostic service checks. Review and fix findings without suppression;
provision independent verifier and conduct separately authorized remote runs.

## Exact b4d70f3 implementation checkpoint and self-review
Implementation commit `b4d70f3e3b8254a8175dee2e679ef7674cf582bd`.
- Full pinned CI107 PASS,0skips,380.284s; began before late completion/security
  test additions. Fresh committed-code full discovery RUNNING, result pending.
- Local20 PASS,0skips,7.394s. Real security12 PASS,0skips,26.809s.
- Actual security entrypoint on b4d70f3 FAIL CI_SCAN_INCOMPLETE16.790s; guarded
  source INCOMPLETE10locks/444components with tool/unlocked gaps. Per-scanner
  exact-SHA diagnostics: Semgrep PASS1017/1017files; Trivy BLOCKED10locks with
  271license-review findings plus coverage gaps; Gitleaks BLOCKED4secret findings.
  Current source Semgrep pass does not replace positive SAST fixtures or clear
  dependency/license/secret/image/trust gates. No raw findings exported.
- Clean Git archive stack build3.790s; real services readiness92.804s then FAIL
  CI_IMAGE_EVIDENCE_INCOMPLETE, first API scan incomplete0packages. Actual-image
  retry: API BLOCKED106packages/107SBOMcomponents/5blocking; migrator BLOCKED120/
  121/5; PostgreSQL INCOMPLETE151/152/135blocking+12unknown; Keycloak BLOCKED559/
  560/6. All identities are local config IDs with null manifestDigest.
- Exact b4d70f3 real Testcontainers migration probe FAIL1/1,0skips,23.828s at
  mandatory image-evidence gate, initial PostgreSQL scan incomplete0packages.
  This negative check does not supersede Task4 failed full backend/Compose jobs.
- Clean archive npm ci PASS254packages10s. Initial browser attempt FAIL4/4,
 0skips because host lacked libatk. Prepared existing native runtime library path
  corrected the environment: actual Playwright Chromium4 PASS,0skips,10.163s,
  including owned API/PostgreSQL requests. Diagnostic service evidence only:
  scanner-rejected stack remains rejected. Stack stopped and only owner-labeled
  resources cleaned. No raw browser reports uploaded.
- Aggregate CLI with actual failed security report and non-success needs returned
  CI_JOB_NON_SUCCESS/nonzero and safe correlation ID. No forged successful status.

Whole-branch self-review found nested format/join expressions could evade the
initial deny regex. Added mutations, observed RED2, replaced with exact allowed
expression inventory. Focused workflow9 PASS0skips0.288s, actionlint/workflow/diff
checks PASS. This changes lint/tests only; it does not resolve any acceptance
blocker. New code commit and final exact-SHA rehearsal results recorded below.

## Final implementation SHA7a6464b rehearsal
Code correction commit `7a6464b16a1034f4326b9b10da48438c393459f0` changes only
workflow expression lint and its tests (plus earlier evidence handoff). Extracted
fresh exact Git archive, unique project/state/owner, rebuilt and actually started
all services. Build3.205s, real readiness96.729s, then startup FAIL
CI_IMAGE_EVIDENCE_INCOMPLETE. No image gate was disabled. Current selective real
backend/Testcontainers probe FAIL1/1,0skips,18.241s, mandatory scan rejection;
original full backend/Compose outcomes remain unresolved. Actual repository gate
FAIL CI_SCAN_INCOMPLETE23.372s; guarded source INCOMPLETE10locks/444components,
CI_TOOL_DEPENDENCY_COVERAGE + CI_UNLOCKED_DEPENDENCIES. No finding suppression.

Fresh archive npm ci PASS254packages7s. Actual Chromium/Playwright with prepared
native host libraries PASS4/4,0skips,11.563s including owned real API/PostgreSQL
requests. This remains diagnostic service evidence after security rejection,
NOT web-browser job success. Initial b4 launch failure is retained above.
Latest pinned focused workflow9 PASS0skips0.410s, completion3 PASS0.335s,
security3 PASS0.186s. Workflow contract, actionlint, py_compile and diff-check PASS.
Final full CI, independent image retry and scoped cleanup results appended below.

Final verification checkpoint:
- Full pinned CI109 PASS,0skips,388.397s. Discovery began on b4d70f3 before the
  lint-only nested-expression correction was committed; final7a6464b focused9
  independently covers that correction. Do not call the entire run an immutable
  final-SHA full-service acceptance. No original backend/Compose pass is inferred.
- Final exact7a6464b four actual images: API BLOCKED106packages/107components,
  migrator BLOCKED120/121, PostgreSQL INCOMPLETE151/152, Keycloak BLOCKED559/560;
  blocking/unknown counts unchanged5,5,135+12unknown,6. Initial stack and backend
  scan failures remain recorded, not overwritten by independent retries.
- Final stack down PASS; exact owner-label cleanup and reinspection:0containers,
  0networks,0volumes. Existing foreign resources untouched. No verification/job
  process intentionally left running. Final workflow/actionlint/diff-check PASS.
- Final tracked modification after implementation7a6464b is this evidence-only
  handoff. It changes no workflow, policy, source, lock, test or acceptance rule;
  reuse of implementation7a evidence is explicitly limited to that identical
  code. Commit subject `docs: record Task 7 exact revision rehearsal`.

All blockers above remain. No Task7/Wave0/P0 acceptance, remote-green or enforcement
claim. No remote changes. Concrete next step is review/remediation, not waiver.


## Review fix round 1: completion publication boundary
Resumed on `feat/wave-0-plan-4-ci` at `5694296`, preserving unfinished completion,
publication and sentinel-test changes from the previous agent. Verified reviewer
finding: parser acceptance previously preserved unknown fields and raw native
and nested diagnostics in both successful and failed artifacts. No remote action,
worktree or subagent authorized/performed during this fix.

Completed: reconstruct an explicit publication envelope, project native tests to
identity/outcome, rebuild nested image scans/source SBOM/security findings,
regenerate bounded reason/correlation values, and revalidate reconstructed reports.
Original upload path is removed before processing; interruption leaves no original
artifact to always-upload. Unknown fields and arbitrary artifact paths are dropped.
Producer secret scanning and known-value guards remain required: projection alone
cannot recognize secrets disguised as syntactically valid package/test identities.
Changed files: `scripts/ci/completion.py`, new `scripts/ci/publication.py`,
`tests/CI/test_completion.py`, `tools/ci/README.md`, this handoff. No public contract,
database migration or trust-policy change. Rollback: revert this fix commit.

Actual verification:
- RED: temporarily tested against HEAD completion helper, then restored unfinished
  fix safely. Completion6 ran, four sentinel-leakage assertion failures plus one
  missing-new-hook error; exit1. Success/failure and nested source/native diagnostic
  cases reproduce the Important finding. Added failed-image nested SBOM extensions.
- System `python3 -m unittest discover -s tests/CI -p test_completion.py`:6 PASS,
 0skips0.312s; aggregate7 PASS0.322s; security_job3 PASS0.129s.
- Initial pinned Python attempts failed to launch (missing libpython shared-library
  search path), exit127. Repeated with existing library directory configured:
  `LD_LIBRARY_PATH="$PWD/.local/ci-python315/lib" .local/ci-venv315/bin/python -m
  unittest discover -s tests/CI -p '<pattern>'`: completion6 PASS0.317s,
  aggregate7 PASS0.337s, security_job3 PASS0.139s, summary10 PASS0.032s,
  image_identity22 PASS0.265s; all0skips.
- Full CI/service/remote acceptance NOT RUN for this review fix; earlier failures
  and all Task7/Wave0/P0 blockers remain unresolved. This fixes artifact publication
  only. Final syntax/diff checks and commit recorded by this session below.

Next: review this fix, continue original security/coverage/image/trust remediation
and separately authorized remote acceptance. No acceptance waiver or green claim.

Final `python3 -m py_compile scripts/ci/completion.py scripts/ci/publication.py tests/CI/test_completion.py` and `git diff --check`: PASS.
Fix commit subject: `fix(ci): reconstruct completion publication envelopes`.
All five listed implementation/test/doc files are staged together; ignored Task7
report is updated locally. No other uncommitted work expected after commit.


## Review fix round 2: initial-read SIGTERM
Resumed clean at `fadc2a96cbc78e86bb8db4718c07daee537c7fbf` on the same branch;
reviewer identified raw-file exposure if SIGTERM arrives during the initial read.
Round1 interruption coverage occurred after unlink and did not cover this window.
Completed same-filesystem atomic rename into an owner-only adjacent temporary
quarantine directory BEFORE stat/read, outside the exact workflow upload paths.
No signal handler/finally cleanup is relied on for this guarantee. Abrupt termination
can leave private quarantined bytes; workspace cleanup is still necessary. A helper
that never starts or terminates before rename cannot protect the producer artifact.
README now states these limits rather than a blanket interruption guarantee.

New subprocess test sends real SIGTERM from the first payload read, checks exit-15,
empty stdout/stderr and absent original upload path. RED against round1: completion
7 ran,1 assertion failure0errors0skips0.571s (original present). Initial edit test run
failed with IndentationError; corrected before final verification. Final pinned
commands, both prefixed `LD_LIBRARY_PATH="$PWD/.local/ci-python315/lib"`:
- `.local/ci-venv315/bin/python -m unittest discover -s tests/CI -p test_completion.py`:
 7 PASS,0skips0.736s.
- `.local/ci-venv315/bin/python -m unittest discover -s tests/CI -p test_aggregate.py`:
 7 PASS,0skips0.447s.
Changed: completion helper, completion tests, CI README, this handoff; ignored local
Task7 report updated. No public contract/migration/trust-policy changes. No remote
changes, subagents or service acceptance reruns; original blockers remain OPEN.
Next: review fix, continue security/image/trust remediation and authorized remote
acceptance. Rollback: revert this round2 fix commit.

Final syntax compilation and `git diff --check`: PASS. Commit subject:
`fix(ci): quarantine raw completion reports before reading`. All round2 tracked
changes staged together; ignored local report retained.
