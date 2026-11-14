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

Follow-up cold-cache remediation is currently in the working tree for the next
scoped commit/publication: the SDK surface compatibility probe and standalone
consumer harness now use the shared lockfile runtime-closure resolver at
`scripts/contracts/runtime_packages.py`; the hosted UID-1001 .NET fixture sets
`HOME=/tmp` alongside `DOTNET_CLI_HOME=/tmp`. The exact reviewed MVP delta was
updated only for these two policy files plus the existing pinned surface lock;
default and hardening modes remain fail-closed. Focused contract-review and
closure tests pass locally; the host lacks a runnable Node binary (`libatomic`)
for the full TypeScript suite, so no full local CI claim is made.
No remote PASS, Wave0/P0 completion or acceptance drill claim.

## Nested Plan3 failure diagnostics

Latest actual run43539810563: security PASS, browser FAIL. Read-only completed
browser job logs show CI_COMMAND_FAILED at the fixed `plan3` command stage,
CI_ACTION_FAILED completion, and no nested Plan3 tokens. Browser gate lasted41s.
Backend/contracts/compose were still running at the inspected jobs API snapshot.
Full-run logs endpoint was not available while that run remained active; completed
job logs endpoint succeeded. No asserted nested-step root cause from this evidence.

Parent authorized another bounded diagnostic iteration. Changed jobs.execute to
retain private stderr for projection on failed Plan3 commands only. It projects
an exact aggregate failure summary into fixed `plan3Step` and `plan3Reason` enums;
for failures before a step, a single recognized stderr JSON reason may provide
the reason. No detail, command, argument, native payload, arbitrary reason value
or child output is copied. Either private stream exceeding1MiB makes both fields `other`; complete bounded
streams are parsed, invalid UTF8 is replaced privately, and unknown or ambiguous values map
to the literal `other`. These are diagnostic hints only; they neither authenticate
child output nor affect failure verdicts, report acceptance or policy decisions.

Additional changed files: scripts/ci/jobs.py, tests/CI/test_job_processes.py,
this handoff. Scoped edits are uncommitted for parent review/publication. No push,
merge, deployment or settings changes by this coding session.

Verification: RED regression expected bounded nested fields but observed only
outer stage. GREEN focused test_job_processes PASS7 in3.871s; covers private
sentinels, stderr details, malformed UTF8, unknown step/reason and conflicting
summaries. Combined test_job_processes/test_job_reports/test_completion PASS22 in4.369s.
`git diff --check`: PASS. Starting commit343f4e6; unrelated Plan3 handoff
preserved. Next: parent review/publish, then inspect fixed nested diagnostic on
actual remote CI before implementing any Plan3 behavioral fix.


Review correction: rejected duplicate JSON object keys rather than accepting the
last reason field. Removed tail truncation, which could hide an earlier conflicting
failure; oversized stdout or stderr now returns both fields as `other` before
parsing. RED regression reproduced duplicate-key acceptance; GREEN combined
process/report/completion suite PASS23 in4.308s, including duplicate reason keys
and >1MiB stdout/stderr conflict cases. `git diff --check`: PASS. Parent authorized
scoped commit after focused checks; this session commits the three diagnostic
files, performs no push, and preserves the unrelated untracked Plan3 handoff.

## Hosted consumer cache reproduction and fix

Run43540642955 browser diagnostic identifies `sdk-consumers` /
PLAN3_SDK_CONSUMERS_FAILED; security PASS. Subsequent read-only jobs/logs snapshot
shows backend FAIL at `dotnet-test`; contracts/compose still running. No claimed
backend test root cause from this static stage alone.

Confirmed local cause for the consumer failure: exported exact committed tree to
a temporary directory, used the prepared pinned runtimes, fresh npm cache and
CI=true/GITHUB_ACTIONS=true. `npm ci --ignore-scripts --no-audit --no-fund`
PASSexit0, then unchanged consumer script FAILexit1 with ENOTCACHED. Historical
local cache had masked missing registry packuments: npm ci guarantees downloaded
locked tarballs, not the metadata required by a separate unlocked offline install.
This reproduces a hosted-cache condition locally; it does not claim full parity
with the official hosted runner OS/image or proof of private remote child output.

Fix: resolve complete installed runtime dependency closure from package-lock;
check installed manifest versions/dependencies, reject missing packages, ambiguous
multiple installed versions or currently unsupported optional/peer/bundled edges
rather than silently omit them. Pack those exact locked local runtime packages
(currently2) alongside freshly built SDK tarballs. Independent temporary consumer
still installs actual packages with --offline, typechecks, exercises wire/negative
checks, then runs the unchanged .NET package/consumer and boundary audit. No
registry metadata fetch, network fallback for npm, node_modules link in consumer,
changed dependency version/lock, generated SDK/contract change, base substitution
or weaker consumer assertion. Existing .NET restore networking is unchanged.
Rollback: revert the scoped consumer harness/helper changes; no migration.

Changed: tests/ClientConsumers/run.sh, new runtime_packages.py;
tests/Contract/test_sdk_consumers.py now always uses a fresh npm cache;
new tests/Contract/test_runtime_packages.py checks exact transitive closure and
missing transitive package failure. This handoff updated. Unrelated untracked
Plan3 handoff preserved; no push by coding session.

Verification:
- RED existing independent packaged-consumer test with empty cache: FAIL, no skip.
- RED closure unit: missing helper. GREEN closure/missing-package unit PASS1.
- Prepared-runtime independent packaged-consumer + closure tests PASS2 in62.412s;
  includes real .NET packaging/consumer, positive/negative TS/.NET wire tests and
  artifact boundary audit.
- Clean exported hosted-environment cache simulation with fix overlay: npm ci
  PASSexit0; full consumers PASSexit0; ENOTCACHED absent; PACKAGED_CONSUMERS_OK
  present. The simulation uses actual pinned runtime/image and clean npm cache,
  not a claimed identical official hosted OS image.
- Additional RED: wrong installed package name was accepted. GREEN strict name,
  version and missing-transitive negatives PASS1 in0.013s.
- Independent scoped reviewer: no Important findings; helper unit/shell checks
  passed. `git diff --check`: PASS. Parent authorized scoped commit/publication;
  coding session commits, parent pushes.

Latest read-only remote snapshot: contracts FAIL84/87, no skips, same three
identities (baseline consumer compile incompatibility, packaged consumer, .NET
surface). Backend FAIL at dotnet-test. Compose ongoing; security PASS. Consumer
cache fix does not claim to explain the two other contract failures or backend
failures. Next investigate .NET surface separately with bounded diagnostics.

## Hosted backend UID mismatch — final remediation iteration

Latest diagnostic run `43594740956` at candidate `ac5095fdee121318fd41089d70dca1d93ddeab29`
completed cancelled after backend failure: backend FAIL, security PASS,
web-browser PASS, Compose/contracts cancelled, aggregate FAIL. The safe backend
summary named exactly five failed methods: API private-secret runtime, Keycloak
private-secret runtime, and the three Keycloak cold/warm/host-port tests.

Root cause is the test image identity, not application authorization or a relaxed
secret policy. GitHub's hosted process creates bind-mounted secret files as UID
1001 with mode `0600`; `ContainerImages` built API/migrator/Keycloak with the
Dockerfile default UID/GID 1000. Therefore those non-root containers could not
read the fixtures. Compose already passes `LOCAL_UID`/`LOCAL_GID` and did not
contain this mismatch. Tests that fail before reading secrets or use the
privilege-dropping PostgreSQL wrapper were unaffected, matching the observed
failure set.

Scoped fix: `ContainerImages` validates the current Linux UID/GID as positive,
passes them as `LOCAL_UID`/`LOCAL_GID` for Platform and Keycloak test images, and
asserts the resulting process identity dynamically. Secret files remain mode
`0600`, containers remain non-root, and Dockerfiles retain default UID/GID 1000
when no local test override is provided. No production source, public contract,
schema, migration, dependency, workflow permission, scanner policy, approval or
security exception changed. Rollback is a scoped revert of the fixture changes.

Changed files in this iteration:
- `tests/Integration/Fixtures/ContainerImages.cs`
- `tests/Integration/ContainerRuntimeTests.cs`
- this handoff

Actual verification:
- RED evidence is hosted run `43594740956`: the five named methods all failed
  with the old fixed-1000 images.
- Reproduction harness under UID/GID 1001, host networking and real Docker:
  PASS 5/5, 0 failed, total 4m12s. Cold import 53s; warm restart/drift 1m23s;
  host-port discovery 51s; API private secret 10s; Keycloak private secret 46s.
- The first two harness attempts failed before exercising the patch because the
  nested apt Docker client lacked BuildKit, then because a shared host `/tmp`
  timing file belonged to UID1000. After using the host Buildx client and an
  isolated `TMPDIR`, tests reached the real services. Host networking was needed
  because the nested test process otherwise cannot reach ports published by the
  host Docker daemon. These harness-only failures are not represented as product
  or hosted-run failures.
- System-Python full CI discovery: FAIL 3/173 after 170 passes. One `/proc` race
  in timeout cleanup and two prepared-tool dependencies (real .NET semantic
  evaluator and Trivy requirement) failed in the unprepared host environment;
  none touches the two changed C# files. They were then rerun with the prepared
  pinned toolchain as recorded below.
- Prepared pinned-tool rerun: timeout cleanup PASS individually; real Plan3
  semantic transition plus MVP scanner-requirement tests PASS 2/2 in 273.426s.
  This resolves the three host-environment failures for their intended toolchain;
  it is not a replacement for a fresh full hosted workflow.
- C# compilation occurred inside the UID1001 focused run and succeeded with two
  pre-existing CA1416 warnings in `ImageEvidenceSecretTests.cs`.

Workspace remains on `feat/wave-0-plan-4-ci`; the scoped commit titled
`fix(backend): align test image uid with runner` contains the fixture, assertions
and this handoff update. `git diff --check` passed before commit. The unrelated untracked
`2026-11-12-wave-0-plan-3-resumption-handoff.md` remains preserved. Next: push
the scoped fix under the existing user authorization and require a new full
hosted run on the resulting exact SHA before claiming backend or Wave 0 green.
No merge, repository-setting change or deployment is authorized/performed.
