# Final whole-branch review — Wave 0 Plan 2

Date: 2026-11-10. Reviewer: `/root/final_review`.

Reviewed branch `feat/wave-0-plan-2-local-stack-observability`, base
`98710c307f8e8168bb01b5a085c7234928afc9df`, head
`d55a30b586bf90919d4598d88b3b3facda082c71`.
Requirements: supplied AGENTS instructions, Plan 2 design and implementation plan,
and the requesting-code-review reviewer instructions. The review used bounded
passes through the supplied full-context diff, with targeted reads of the existing
health/readiness/grant implementation to assess Plan 1 compatibility. No source,
index, branch, containers or test state was changed. This report is the only write.

## Strengths

- The six-service topology preserves the intended boundaries. CRM and IdP have
  separate databases, volumes and networks; API does not depend on Keycloak.
  API gets only its application connection file. Bootstrap and migration remain
  separate one-shots, with no API startup migration or external-module reference.
- Persistent state publication is deliberately conservative: random credentials,
  owner-only directory/files, exact manifest validation, no-replace atomic
  publication and fsync, rejection of symlinks/foreign state, and no credential
  regeneration when project volumes exist. The sibling lifecycle metadata also
  preserves checkout/state/port identity across down/up.
- CRM bootstrap checks actual authentication and database privileges rather than
  trusting a host checksum. Its durable private marker distinguishes controlled
  incomplete provisioning from unknown state, and completed provisioning does not
  silently repair credentials or ACLs. App relation/schema ownership and managed
  platform ownership drift are covered by explicit SQL validation and real tests.
- Each up removes the previous API and reruns current bootstrap/migration images.
  The lifecycle holds a nonblocking project lock, enforces finite process/probe
  deadlines, checks project/checkout labels, exact secret sources/destinations and
  read-only mounts, and checks both container-local and published endpoints.
  Down retains named volumes and credentials. Real tests cover contention, kill
  and retry, shutdown, foreign listeners, and independent projects.
- Realm inspection checks live discovery/JWKS and persisted Admin API client
  metadata. It does not mistake startup import for a realm migration. Its HTTP
  implementation bounds status, headers and body by one deadline, uses numeric
  loopback without proxies/redirects, and keeps the management token in memory.
- Diagnostics retain the original CLI root fields and HTTP Problem/correlation
  contract. Actual Activity IDs are used; raw provider exceptions and query-bearing
  framework messages are filtered while safe operational/framework events remain.
  The unchanged readiness service still has a three-second budget, and liveness
  remains independent of the database.
- The tests exercise real PostgreSQL and actual images/Compose. Secret audits
  include successful and failed startup paths, all service logs/configuration,
  image histories and decompressed published layers. Positive evidence is required
  alongside absence-of-secret assertions. The fake issuer uses the standard JWT
  library, public-only RSA exports and locked key snapshots in a test-only assembly.
- Documentation distinguishes local acceptance, reused test evidence, clean-source
  execution, cached builds and production guidance. Wave 0/P0, authentication,
  tenant isolation, remote DemoModule and restore gates remain explicitly open.

## Issues

### Critical (must fix)

None found.

### Important (should fix before approval)

None found.

### Minor (optional follow-up)

1. **Make the negative signature test unambiguous.**
   - Reference: `tests/Integration/FakeOidcIssuerTests.cs:59`.
   - The negative test changes both the public key and its `kid`, then checks only
     `IsValid == false`. It does not independently pin a same-`kid` bad signature
     case or a changed signature on an otherwise valid token.
   - Effect: weaker diagnostic/regression precision if key-resolution behavior
     changes. This is not evidence of faulty signing: the current positive test
     verifies a token using independently fetched public JWKS, and the fixture
     signs through the standard library.
   - Optional improvement: change a signature byte while preserving the token's
     header/payload and key set, or supply a different public key with the same
     `kid`; assert signature rejection. Do not add production authentication to
     address this test-only improvement.

2. **Pin the fixture's disposal contract in a small test.**
   - Reference: `tests/Integration/Fixtures/FakeOidcIssuer.cs:117`, with guards at
     lines 77 and 96; coverage file `tests/Integration/FakeOidcIssuerTests.cs`.
   - Sequential repeated disposal and signing/rotation after disposal are not
     explicitly tested. The implementation already has an idempotence guard,
     disposes RSA keys and the application, and throws on guarded post-disposal
     operations.
   - Effect: an optional regression gap in a reusable Wave 1 test utility, not a
     demonstrated current resource leak or runtime defect.
   - Optional improvement: test two awaited disposals and expected
     `ObjectDisposedException` for signing and rotation afterward.

The deferred actual-image foreign-owner case is triaged below, not promoted to a
blocking requirement or asserted to reveal a current defect.

## Verification and evidence assessment

This reviewer did not rerun suites, rebuild images, or start a stack. There was no
specific unresolved runtime doubt requiring another full gate.

Read-only checks performed during review:

- HEAD is the requested `d55a30b...`; tracked working tree is clean.
- `git diff --name-only 34342cb... d55a30b...` lists documentation only.
- SHA256 of the current Compose fixture, failure tests and secret tests equals
  each frozen hash in the verification record/current handoff.
- Inspected only safe summary matches from the retained final captures:
  Compose reports `Test Run Successful`, total 14/passed 14, 23.3303 minutes;
  non-Compose reports `Test Run Successful`, total 70/passed 70, 7.0451 minutes.
  All selected tests passed; these are prior executions on the unchanged source,
  not executions by this reviewer.
- Reviewed named test implementations against W0P2-01 through W0P2-12, the current
  handoff, and the Task 8 source/archive and retention record. Fresh locked
  restore/build, contracts 32, Local 20, syntax checks, and exact-archive SDK-free
  up/smoke/down are recorded evidence previously verified in Task 8 review.
  The archive runtime candidate is `34342cb...`, not the later docs commit.

The evidence supports the local Plan 2 claims. Historical RED/prerequisite/harness
failures are distinguished from the final passing gates, and cache reuse is
explicit. No fresh-pull, minimum-version, production-authentication or P0 completion
claim is inferred. Final-review checkboxes/status remain pending intentionally for
the controller to close after this verdict.

## Declined to judge

The following considered behaviors were set aside as requirements for this review;
each needs the controller's explicit scope ruling rather than being silently lost.

- Production authentication, JWT middleware, audiences, browser callback/login and
  server-to-server IdP routing: explicitly Wave 1; current realm/fake issuer prove
  metadata and fixture readiness only.
- Contact/Identity linkage, tenant isolation, RBAC/RLS and tenant business queries:
  no such runtime is introduced; the plan explicitly excludes them and keeps the
  corresponding acceptance open.
- Durable integration delivery, duplicate event handling, remote DemoModule and
  restore/DR: separate P0 gates; local dependency recovery/down-up does not claim
  to satisfy them.
- Production TLS/proxy trust, administrator MFA enforcement, secret-manager
  integration, encrypted storage and deployment: guidance only in this local
  loopback start-dev profile, with no deployment authorized or claimed.
- A full telemetry backend/exporter, dashboards or distributed tracing transport:
  explicitly excluded; JSON diagnostics and genuine local Activity IDs are the
  delivered observability scope.
- macOS/Windows, remote Docker, root-host operation, rootless/user-namespace mapping
  and untested architecture/tool-version combinations: the documented tested path
  is Linux, a local Unix-socket daemon and a non-root account with matching runtime
  UID/GID. Rejection or absent evidence outside that path is not a claimed pass.
- Cross-user/distributed lifecycle locking, or different filesystem aliases for
  the same daemon socket: the implementation promises a per-user lock keyed by
  the configured local daemon endpoint and project. It covers alias context names
  sharing that endpoint and different checkout/state paths; it does not claim a
  distributed lock or canonicalization of every equivalent socket path.
- Protection against a host/Docker administrator, hostile same-account filesystem
  replacement between checks, or deliberately forged ownership labels and images:
  host administrator access is the documented trust boundary. Actual accidental
  wrong credential grants, foreign listeners and mismatched checkout/state are
  checked and were reviewed within scope.
- Continuous policing of manually added networks, privileged Docker overrides or
  arbitrary containers outside the generated Compose model: configuration and
  tests prove the generated two-network boundaries; smoke is not advertised as a
  general Docker security auditor.
- A general warm IdP database privilege/ownership-drift auditor: CRM's controlled
  bootstrap has the explicit warm ACL contract. IdP uses the pinned vendor's
  cold initializer for its separate non-superuser owner and validates credentials,
  health and persisted realm metadata on warm runs. Extending that to audit every
  administrator-made IdP database privilege change is not an implemented claim.
- General crash repair for interrupted vendor initdb/IdP initialization, storage
  corruption or missing database media: controlled CRM bootstrap retry is covered;
  no automatic destructive reset or general recovery/DR guarantee is promised.
- Automated realm migrations, password rotation or destructive reset/reconciliation:
  intentionally not automatic. Drift fails safely and requires deliberate operator
  action; down preserves persistent state.
- Automatically adapting the Plan 2 bootstrap to future business schemas or app
  write privileges: the current platform/history-only contract is deliberate;
  later vertical slices must update those boundaries with their own tests.
- Same-`kid` signature and sequential disposal test enhancements as release blockers:
  retained as optional Minor items above because current standard-library signing,
  public verification, key retirement and disposal guards are present.
- The deferred **readable 0600 foreign-owner** actual-image negative:
  `platform.sh:19` and `keycloak.sh:20` check ownership explicitly; runtime tests
  prove non-root execution and private-file success/failure, and
  `tests/Local/test_secrets.py:87` changes actual file ownership and verifies state
  rejection. Under ordinary non-root DAC a foreign-owned 0600 file is already
  unreadable. Forcing it readable requires a different privilege/ACL scenario;
  duplicating the actual-image negative is optional coverage, not an uncovered
  supported-profile permission bypass.
- Repeating all passing suites solely because Task 8 changed documentation:
  source equality and frozen hashes were verified, retained result totals inspected,
  and the separate exact-source archive runtime exists. A source change would
  invalidate that reuse and require appropriate renewed evidence.
- Pending final-review checkboxes and historical task wording in chronological
  evidence: deliberately awaiting this review or explicitly marked historical;
  controller closure is still required, so this is not a runtime defect.

## Recommendations and assessment

**Ready to merge from a code-review perspective: Yes.**

**Verdict: Approved, with two optional test-coverage follow-ups; zero Critical and
zero Important findings.** The implementation matches the bounded local Plan 2
scope, preserves Plan 1 behavior, and has substantial real-runtime evidence tied
to unchanged source. This verdict does not authorize merge/push/deploy and does
not close Wave 0/P0.

Controller next steps: rule on the declined-to-judge scope list, record the final
review verdict and optional follow-ups in the durable handoff/evidence, and close
the final-review checkbox through the authorized documentation workflow. No source
fix or full-suite rerun is requested by this review.


## Controller adjudication and documentation closure — 2026-11-10

The controller accepts the Approved verdict for reviewed head
`d55a30b586bf90919d4598d88b3b3facda082c71`, base
`98710c307f8e8168bb01b5a085c7234928afc9df`, with **zero Critical and zero Important
findings**. The runtime/test candidate remains
`34342cbb4f77458560759d6d644925ab1cecf702`. The report above is preserved verbatim;
its pending-controller wording describes its original review time and is superseded
by this adjudication. This subsequent closure changes documentation only.

Every individual item in the report's **Declined to judge** section is explicitly
accepted as scoped: excluded future Wave 0/P0 work, the documented tested profile,
the host/Docker administrator trust boundary, an intentionally limited local
validation contract, optional test coverage, or justified unchanged-source evidence
reuse. No item is silently converted to an implemented or tested acceptance claim.
The rationale and cost recorded by the controller are preserved verbatim in the
ledger below: a mistaken exclusion would require additional future coverage or
implementation; it grants no unsupported-profile, production or P0 claim.

In particular, CRM controlled bootstrap explicitly checks warm credentials and
ACL/ownership drift. IdP cold initialization establishes a separate non-superuser
owner; warm operation checks credentials, health and persisted realm/client metadata.
A general warm IdP database privilege/ownership-drift auditor is **not delivered**.
Cross-user/distributed locking, every equivalent filesystem alias of a Docker
socket, hostile same-account/administrator mutation and arbitrary Docker overrides
are also outside the delivered contract, as individually recorded above.

The two Minor findings remain optional future test work, without a source change:

1. Add a same-`kid` wrong-key or tampered-signature negative while preserving the
   remaining valid token/key context, with explicit signature rejection.
2. Test sequential repeated awaited disposal and expected post-disposal signing/
   rotation exceptions on the fake issuer.

The deferred readable-0600 foreign-owner actual-image negative remains optional
coverage under the report's DAC/privilege rationale; it is not evidence of a
supported-profile bypass. These follow-ups are retained rather than discarded,
and require their own tests if taken up in a later slice.

Local **Plan 2 is complete** after this review/adjudication. Wave 0 remains OPEN
for frontend, SDK generation/compilation, breaking-diff and full application/
security/container CI with remote execution. OIDC/tenant/business, remote
DemoModule, concurrent event delivery, durable outage/recovery and restore/DR
acceptance remain P0 OPEN. Production IdP documentation is configuration guidance,
not deployed or authentication-verified evidence.

The finishing-a-development-branch skill uses the already authorized **keep branch
as-is** choice: branch `feat/wave-0-plan-2-local-stack-observability` and workspace
`/home/leduc/crm-centric` are preserved. This is the ordinary repository
(`git-dir == git-common-dir == .git`); no worktree was created. No integration
menu, merge, push, deployment, branch/workspace removal or scratch deletion is
needed or authorized by this documentation closure.

No full suite, image build or runtime stack was rerun for closure. The reviewed
frozen-source Compose 14/non-Compose 70 gates and the separate exact-`34342cb`
archive lifecycle remain valid under the recorded source-equality ruling. Fresh
closure checks verify Markdown-only changes, unchanged runtime/test source blobs,
unstaged/staged whitespace and clean status after the separate closure commit.
Its exact SHA is recorded in the ignored Task 8 control report after commit rather
than attempting a committed self-reference. Scratch cleanup remains with the
controller after durable-record closure.

## Archived controller ledger — verbatim before closure

The ledger below preserves every Ruling line, its recorded rationale/cost, and
chronological execution context. Historical pending/in-progress entries are
superseded by the approved report and adjudication above; they do not reopen work.

```text
# SDD ledger — plan: docs/superpowers/plans/2026-11-09-wave-0-plan-2-local-stack-observability.md

User authorized full sequential SDD implementation, new branch, no worktree on 2026-11-09. Branch feat/wave-0-plan-2-local-stack-observability; clean initial main ahead origin/main by two documentation commits. Spec and relevant handoffs read. Branch creation required escalated git access; approved.

## Preflight
| Tasks | Producer / consumer or internal consistency | Finding / ruling |
|---|---|---|
|1|Logs/tests/CLI compatibility|Consistent; keep root result JSON single-object.|
|2|Fake issuer/public key tests|Consistent; test-only utility.|
|3|Images/secret adapters/runtime tests|Consistent; prerequisites separate from RED.|
|4|Bootstrap/secret state/tests|Consistent; completed state rejects ACL drift, interrupted state may finish controlled grants.|
|5|Realm/admin inspection/tests|Consistent; admin-cli management only.|
|6|Lifecycle/Compose/tests|Consistent; up calls inspection under existing lock.|
|7|Failure gates|Consistent; already-green cases recorded honestly.|
|8|Export/evidence|Consistent; final source changes require re-export.|
|1,3|Published process diagnostics|Preserve host configuration path.|
|1,4|Migrator Program and process fixture|Extend args/env compatibly; no secret args.|
|1,7|Logging/redaction|Keep safe framework logs; audit failure cases.|
|1,8|Log documentation/evidence|Document formatter field placement.|
|2,3|Fixture/publish boundary|No test assembly copied.|
|2,8|Fixture evidence|No CRM authentication claim.|
|3,4|Mounted secrets/bootstrap adapter|Service-specific files; non-root runtime verified.|
|3,5|Keycloak image/adapter|Same pinned image, actual UID and health tool.|
|3,6|Images/Compose|Consume locked refs, no secret interpolation.|
|3,7|Image audit|Capture sentinels without dumping raw output.|
|3,8|Pins/evidence|Verify official metadata and actual runtime.|
|4,5|Secret state/IdP files|Separate credentials and databases.|
|4,6|State/bootstrap/CLI|Shared project identity; volumes inspected before generation.|
|4,7|Bootstrap failures|Validate actual credentials and completed ACLs.|
|4,8|State evidence|No credentials in handoff.|
|5,6|Realm inspection/lifecycle|Canonical public issuer follows selected port.|
|5,7|Warm drift|Inspect persisted metadata, never overwrite.|
|5,8|IdP docs|Local configuration only, production not deployed.|
|6,7|Compose fixture/lifecycle|Owned projects only; lock across state paths.|
|6,8|Clean export|No host SDK or inherited secrets.|
|7,8|Acceptance evidence|Runtime gates mandatory, no config-only substitution.|

Ruling: Use current checkout with new branch, no worktree — explicit user instruction overrides skill isolation default — no additional checkout isolation.
Ruling: Spec/plan historical unapproved wording is superseded by current explicit implementation request — no scope expansion.

## Tasks
1 complete
2 complete
3 complete
4 complete
5 complete
6 complete
7 complete
8 in progress

2026-11-09 resumed after user enabled full access. Docker Server 29.9.3 and Compose v5.7.0 accessible. Task1 agent /root/task1 active; BASE 98710c307f8e8168bb01b5a085c7234928afc9df. No worktree created.

Task1 complete df91367; reviewer /root/review1 spec compliant, quality Approved, no findings. Controller focused5/5, non-Compose29/29, contracts32/32, build/locked restore PASS. BASE Task2 df91367. One handoff punctuation edit retained for next commit.

Task1 controller follow-up gap: owned service labels currently platform-api/platform-migrator, contrary to shared plan contract crm-api/crm-migrate. Task3 runtime composition must correct labels and corresponding assertions/docs with failing exact-label regression before runtime integration; reviewer missed this. Carry into Task3 dispatch.

Task2 regression: initial wrong PATH caused smoke missing host dotnet; rerun with correct PATH exposed existing TcpListener shutdown race in SmokeTests foreignListener=true. Agent authorized to isolate and fix expected cancellation-only teardown exception if reproduced, no changes to product scope. Ruling: include minimal pre-existing smoke fixture race repair in Task2 — mandatory regression gate cannot rely on flaky shutdown — cost if wrong is small test-only diff subject to task review.

Task2 complete 390ae97; reviewer /root/review2 spec compliant, quality Approved. RED9/GREEN9/focused11/non-Compose38/contracts32; build/locked restore PASS. Minor deferred: wrong-key negative uses distinct kid so add same-kid/tampered signature coverage; repeated disposal/after-disposal coverage. Production publish boundary checked Task3. BASE Task3 390ae97.

Task3 complete23d7952; reviewer /root/review3 spec compliant qualityApproved. Full52, contracts32, locked restore/build/shell/diffPASS. Exact service label gap fixed with RED/GREEN. Minor deferred: actual-image readable0600 foreign-owner negative case. Upstream provenance/execution supplied in report. BASE Task4 23d7952. Task4 fixed argv bootstrap mapping; Task6 host nonroot UID/GID build mapping mandatory.

Task4 integration interface for Task6: crm-db POSTGRES_DB=postgres, never precreate crm (bootstrap rejects unowned existing target); crm_admin_connection targets maintenance postgres; _local.bootstrap marker in postgres. Atomic secret publication uses Linux renameat2 NOREPLACE, document tested Linux platform and fail safely unsupported.

Task4 complete19cb472 +02d0962; review found ownership gap, fix round1/5 all1 addressed0open. Scoped rereviewer /root/rereview4 approved; RED5/GREEN19 actual PostgreSQL. Original full60 plus focused SQL fix19, Python7/contracts32/build/locked/syntaxPASS. BASE Task5 02d0962.

Task5 resumed after usage-limit interruption by explicit user continue. Task6 IdP contract: POSTGRES_USER=postgres POSTGRES_DB=postgres; init creates keycloak DB/schema and dedicated role; distinct idp_db_admin_password/idp_db_password. Keycloak adapter accepts only start-dev optionally --import-realm, canonical KC_HOSTNAME env no arbitrary hostname argv.

Task5 complete44e1e7e +369ca5e; review found absolute deadline status/header trickle gap; fix round1/5 all1 addressed0open. Rereviewer /root/rereview5 Approved. Original full70/70, fixLocal11 +realKeycloak5. Build/locked/contracts32/syntaxPASS. BASE Task6 369ca5e. Integration files/secret map in operations05 and report.

Task6 complete007883b+194028d; review found exactsecretgrant gap fixround1/5 all1addressed0open, /root/rereview6 approved. OriginalfullnonCompose70/Compose4; fixLocal20+actualComposeboundary1. Separateowned sibling state.stack.json lifecyclemetadata introduced for ports/provisioning identity across down; exactsecretmanifest unchanged. BASE Task7 194028d.

2026-11-10 user resumed afterusage interruption. HEAD194028d clean before Task7 tests; currently Task7 uncommitted fixture+failure/secret tests. Baseline failure8pass1IdPtestassumptionfail; bounded eventual IdP recovery fix1pass. Secret actualruntime/allfourimagelayersPASS4m26. RealCLI lockproofPASS1m37 (pause ownedAPI; waitbuild_completed heldlock, busyallactions+distinctstate/copiedcheckout, killCLI, lockrelease, retryready history1/data87/samecredentials). DBadvisory blocker strategy failed because legitimatewarmupDBrecreate disconnectedblocker; no productiondefect. SafeDiagnostic fixture nonObjectJSON guard fixed. SOURCEFROZEN; finalallCompose14 then nonCompose70 running sequentially as of latest /root/task7 report. New handoff date2026-11-10 required. Need task7 commit/report then independent review, Task8 cleanarchive exactSHA/evidence/docs, task8 review and wholebranch review gpt-6-astra high. No merge/push/deploy.

Task7 finalallComposeGREEN14/14 nofailed/skips23.3303min exit0. FinalnonCompose70 running /tmp/crm-task7-final-noncompose.txt (agent session1775), sourcefrozenunchanged. Awaitcommit/report thenTask7review.

Task7complete34342cb; /root/review7 speccompliant qualityApproved nofindings. FinalCompose14/nonCompose70/frozenhashmatch/Local20/contracts32/buildlocked0warningsPASS. BASE Task8 34342cb. Ruling: reuse fresh final14/70 verification for Task8 only after committedsource equality check — no sourcechanges sincefrozen gates, avoid redundant23min suite — costifwrong staleproof; cleanarchive exactSHAup/smoke/down remainsmandatory and runtimechanges require rerun.

Task8 completed55a30b docs-only; /root/review8 Approvednofindings, verifiedexactarchivehash/recreatedgitarchivestream/sourcehashbaseheadworkspace; safeevidenceAPI/issuer/UIDretentioncleanup,14/70reused32/20freshbuild0warnings. Wholebranchreviewpending.

Finalwholebranchreview /root/final_review gpt-6-astra Approved d55a30b zeroCriticalImportant; optionalMinor samekid/tamperedsignature and repeated/postdisposaltests; foreignowneractualimage optionalnonblocking. Ruling: accept every enumerated Declinedtojudge scope line in final-review-report.md — each matches explicit localPlan2 exclusions, testedprofile/hosttrustboundary or optionalcoverage ratherthan implementedacceptance — costifwrong additionalfuturecoverage/implementation needed, no unsupportedprofile/P0claim. Persist fullreview+allrulings in docs before scratchcleanup. Finaldocsclosureonly pending; no reruntime required because no sourcefix. Keep branch unmerged/unpushed per plan/user scope.
```
