# Wave 0 remote gate debugging — 2026-11-14

Objective: diagnose PR1 run43456337272 after immutable revision proof passed.
Branch `feat/wave-0-plan-4-ci`, starting HEAD `816fd3e`.
User authorized iterative branch publication/CI through parent; this coding
session does not push, merge, change settings, deploy, or send external messages.
Parent explicitly approved exact duplicate merge metadata recognition and bounded
command-stage diagnostics. Relevant plan: [CI wave exit](../superpowers/plans/2026-11-11-wave-0-plan-4-ci-wave-exit.md).
Prior context: [revision proof](2026-11-13-wave-0-remote-revision-handoff.md),
[exact64 review](../engineering/evidence/2026-11-13-mvp-secret-review-activation.md).

Remote read-only evidence: downloaded safe summary artifacts and Actions logs via
`gh api .../actions/runs/43456337272/logs` (ordinary `gh run view --log` returned
zero bytes). All displayed diagnostics are bounded tokens/counts/test identities;
raw subprocess/scanner payloads remain private temporary files.
- Compose PASS14/14.
- Contracts FAIL87 total:84 passed,3 failed. Failing identities:
  `test_baseline_consumer_compile_failure_is_incompatible`,
  `test_independent_packaged_consumers`,
  `test_additive_members_pass_but_removed_or_changed_members_fail` (.NET surface).
- Backend/browser gate logs show CI_COMMAND_FAILED. Completion rejects their
  incomplete suite inventories and emits CI_ACTION_FAILED; this does not identify
  the failing command. No claimed backend/browser root cause.
- Security CI_SCAN_FINDINGS:64 blocked secret findings;132 accepted metadata
  identities;6 dependency findings classified pass; existing dependency coverage
  gap remains disclosed. No new vulnerability exception or policy change.

Confirmed security root: fetched exact run merge object read-only. Merge has two
parents and its tree equals the PR head tree. Real pinned Gitleaks full-history
separate-merge-diff scan yields128 history records:64 approved original records
plus64 new observations attributed to the synthetic merge. They describe exactly
the same approved metadata blobs. Running strict adjudication and the new helper
on these actual native records recognizes64 originals and64 duplicates, leaving
zero blocked history records. This is bounded local evidence, not remote PASS.

Implementation: history scanning remains unchanged and all merge diffs are still
scanned. Only a unique observation at the exact run merge may fold into an already
verified original when exact ordered base/head structure and identical head tree
are proved, full scanner content matches through canonical JSON (type strict),
and Git blob OID plus bytes match. Commit author/email/date/message must equal
actual Git metadata; fingerprint must match native syntax; link must match the
approved origin link rewritten to the merge. Unapproved, changed, ambiguous,
duplicate, wrong-attribution and merge-introduced content remains blocked. No new
approved identity, suppression, scanner policy, contract base, schema, migration
or dependency waiver. Rollback is a scoped revert.

Additional diagnostic: jobs.execute emits only CI_COMMAND_FAILED and one fixed
allowlisted command family on nonzero exit; unknown commands map to `other`.
It never emits arguments, executable-controlled strings, paths or child output.
Failure verdicts remain unchanged. Next remote iteration must establish the
backend/browser failing stages before any speculative implementation fix.

Changed files: scripts/ci/scan.py, scripts/ci/jobs.py,
tests/CI/test_mvp_history_review.py, tests/CI/test_job_processes.py, this handoff.
Unrelated untracked Plan3 resumption handoff preserved.

Verification (completed):
- Focused duplicate helper RED: missing helper. GREEN1 test.
- Attribution RED: missing metadata API; GREEN1 test after exact metadata checks.
- Type RED: StartColumn true accepted as integer1; GREEN1 after canonical JSON.
- Diagnostic RED: no JSON diagnostic; GREEN1 after bounded stage output.
- Real synthetic merge scanner in temporary bare shared Git clone: initial
  PASS1 in20.422s; final prepared-runtime history/process suite PASS13 in51.872s
  after all attribution/type/diagnostic changes. Includes merge-introduced secret
  and expired/changed review/source negatives.
- Local .NET surface focused test: PASS1, no errors/skips. Minimal equivalent
  pinned-image offline .NET build: exit0, no compiler/NuGet error codes. Remote
  contract failure is not reproduced by those probes.
- `git diff --check`: PASS after implementation and handoff creation.

Independent parent-coordinated scoped review: no remaining Critical/Important
findings; reviewer focused2 checks passed. Parent performs commit/publication.
All scoped files remain uncommitted at this handoff; no push by this session.

Additional completed probes: prepared-runtime host locked .NET restore PASSexit0;
Release build PASSexit0; no compiler/NuGet error codes. Backend integration probe
pending at handoff.

Running/pending process sessions (safe projection already wrapped):
- Full prepared-runtime CI suite: session7559, output /tmp/crm-ci-tests-private.txt.
  Started before final type/attribution/diagnostic refinements; focused13 covers
  final changed paths. No final result yet; do not claim full suite passed.
- Full prepared-runtime Contract suite: session90811; normalized final report
  /tmp/crm-local-contract.json; no final result yet.
- Backend non-compose real integration probe: session74172; private native TRX
  /tmp/crm-backend-local/results.trx; no final result yet.
Parent can poll these existing sessions; never display raw diagnostic files.
Concrete next: commit/review/publication through parent under existing authority;
inspect new remote safe stage tokens; diagnose backend/browser and three remote
contract failures from evidence before fixes. Record pending suite outcomes when
available; do not broaden the exact metadata policy or change immutable base.
No remote PASS, Wave0/P0 completion or acceptance drill claim.
