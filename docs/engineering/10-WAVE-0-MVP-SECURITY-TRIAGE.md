# Wave 0 MVP security evidence and review

Scope authority: [CI MVP decision](09-WAVE-0-CI-MVP-SCOPE.md) and [implementation plan](../superpowers/plans/2026-11-12-wave-0-ci-mvp-adjustment.md). This document records local diagnostic evidence, not independent attestation or remote acceptance. Wave 0/P0/RT-* remain OPEN.

## Required execution

`python -m scripts.ci.security --mode mvp --revision revision.json --output .local/ci/security.json` requires pinned Gitleaks and Trivy. Gitleaks scans the exact committed candidate tree and complete base-to-candidate history, including added-then-deleted and merge-only secrets. Trivy scans every recognized dependency lock and must discover the exact transitive package inventory. Missing history, missing/malformed scanners, failed feed updates, stale database metadata, missing packages, unlocked projects and unsupported dependency ecosystems fail closed. Required findings retain the existing severity policy; every secret finding blocks, including unverified findings. No threshold, baseline, suppression or exception has changed.

The safe summary binds each required scanner result to run ID, attempt, candidate/base/merge SHA, count, reason token and correlation ID. Findings contain only opaque SHA-256 identifiers, fixed kinds and verdict enums. Native diagnostics, paths, package names, credentials and scanner messages are not published. The completion boundary reconstructs this bounded evidence; an arbitrary candidate waiver cannot authorize a finding.

## Deferred coverage remains OPEN

| Area | Status | Evidence/limit | Review owner |
| --- | --- | --- | --- |
| SAST | OPEN | MVP does not run Semgrep; hardening adapter and pinned rules remain available | Security reviewer, assignment pending |
| License clearance | OPEN | Trivy license findings are retained separately, including restricted/unknown licenses; no release approval | Security/legal reviewer, assignment pending |
| Image/SBOM/provenance | OPEN | MVP does not evaluate images or publish full source SBOM; historical image findings in ledger 08 remain unresolved | Platform/security reviewer, assignment pending |
| Transitive tool inventory | OPEN | `CI_TOOL_DEPENDENCY_COVERAGE` is explicitly deferred; supported pinned Python tool locks still receive dependency scanning | Platform/security reviewer, assignment pending |

Only the named tool-inventory gap is deferred. `CI_ECOSYSTEM_COVERAGE` and `CI_UNLOCKED_DEPENDENCIES` remain required blockers. The MVP summary records OPEN for every deferred area even when no findings were observed. No empty finding list constitutes clearance. Independent verifier, repository enforcement and remote provisioning remain unproven.

## Finding review

No specific finding, severity exception or false positive is approved. Scanner-reported secrets, vulnerabilities and licenses must be reviewed against private evidence; a digest or fixture resemblance alone cannot establish a false positive. Secret review owner and vulnerability remediation owner are unassigned security/platform reviewers until explicitly assigned. Review must record finding ID, evidence, accountable owner, decision and any expiry before a blocked finding can become non-blocking. Candidate-authored JSON is not trusted approval.

Actual local scan evidence is appended below after scanning a committed candidate. Full five-job rehearsal and authorized remote evidence remain separate work.

## Committed candidate scan — 2026-11-12

Candidate `4baea1646a424ab8be03bfecb5c25bf2d413de57`; base `601dde8847cad82d038ea837c01d3da0d7449450`; merge SHA absent; local run `local-mvp-task2`, attempt 1. Command above used `.local/ci-task2/revision.json`, output `.local/ci-task2/security.json`, and `CI_SCANNER_ROOT=.local/task5`. Actual exit **1 / failure / CI_SCAN_INCOMPLETE**. Safe completion preserved failure and all required/deferred evidence (`CI_JOB_FAILED`). No remote job ran.

| Required evidence | Actual result | Classification and decision |
| --- | --- | --- |
| Gitleaks 8.30.1 | BLOCKED; 2 scan surfaces; 64 unique secret finding digests | Scanner-reported secret, unreviewed; every record remains blocked, owner unassigned, no waiver |
| Trivy 0.76.2 | PASS under existing vulnerability policy; 10 locks; 6 vulnerability records | Existing-policy nonblocking severity, no exception or severity change; findings retained for review |
| Dependency coverage | `CI_UNLOCKED_DEPENDENCIES` | Required blocker; project manifests without corresponding locks remain uncovered |
| Tool inventory | `CI_TOOL_DEPENDENCY_COVERAGE` | Explicit deferred hardening; OPEN, no tool-closure claim |
| License | 271 review-required + 508 allowlist-pass records | OPEN; this is neither full clearance nor approval for restricted/unknown licenses |

[Safe classified digest evidence](evidence/2026-11-12-mvp-security-4baea16.json) records each required finding digest and review-required license digest, fixed verdict, reason/correlation and run identity without scanner-owned strings. `approval=NONE` and `review_owner=UNASSIGNED` deliberately record missing review. The six Trivy records are nonblocking according to the unchanged existing policy, not a newly approved risk threshold. SAST/image/tool/license status remains OPEN; historical image findings in ledger 08 are unchanged. This scan targets the implementation commit; subsequent evidence-only commits and the eventual final candidate need their own Task 3 scan.

## Private review replay and identity defect — 2026-11-12

Private replay of `4baea1646a424ab8be03bfecb5c25bf2d413de57` against the
same base reproduced 64 tree findings and zero history findings. All were the
unchanged `generic-api-key` rule. Each scanner-selected JSON line was verified
as an exact SHA-256 field, then recomputed against its underlying bytes:
60 against the retained locally built Keycloak JAR files, four against the exact
candidate OpenAPI contract blobs. This is stronger evidence than hash shape or
fixture resemblance. No credential use or provider verification was attempted;
no raw matched value, native report or token was persisted or published.

A real repeat-scan regression exposed unstable identifiers: Gitleaks `dir` with
an absolute temporary export path embeds that path in its report. The existing
full-record digest consequently changed between identical scans. The fix runs
`gitleaks dir .` with its working directory set to the same verified candidate
export. It retains every native record field in the digest, both scan surfaces,
all rules and all blocking verdicts. Different file/rule/location identities
remain distinct. Report enumeration order is immaterial; repeated findings are
compared as multisets. This changes finding identities, not security policy.

[Review proposals by stable replay ID](evidence/2026-11-12-mvp-finding-review.json)
record all 64 independently recomputed observations. Proposed reviewer action:
review each `RECOMPUTED_JAR_SHA256` or `RECOMPUTED_CONTRACT_SHA256` entry as a
false-positive candidate using its byte verification, then assign an accountable
owner and record an explicit decision and expiry through the trusted review
boundary. `approval=NONE`, `review_owner=UNASSIGNED` and `verdict=blocked` apply
to every entry. There is no suppression, allowlist, scanner exception or approved
finding in this change.

The original 64 published opaque IDs cannot be mapped retrospectively: their
random export directory was not retained and a one-way record hash cannot
recover it. **Original unmapped/unclassified IDs: 64. Stable replay unclassified
findings: 0. Approved findings: 0.** Never attach these new observations to an
old ID by list position or claim the original IDs were adjudicated. Future
review must use a freshly committed candidate scan with stable IDs.

Reproduction uses pinned Gitleaks 8.30.1 and the repository config: export the
exact candidate with `git archive`, run `gitleaks dir .` from that export with
`--redact=100 --ignore-gitleaks-allow --gitleaks-ignore-path=/dev/null
--exit-code=0 --report-format=json --report-path=/dev/stdout`, and capture stdout
only in private process memory. Run history separately with the existing
`--full-history --diff-merges=separate BASE..CANDIDATE` options. Hash the complete
redacted record using the existing sorted compact JSON `["secret", record]`
encoding. Repeat and compare record multisets. For each tree finding, parse only
its selected JSON line, require an exact 64-hex JSON value, and recompute SHA-256
against the referenced JAR under the original built distribution or the matching
`docs/contracts` blob. Publish only the authored classification, opaque ID and
verification enums. Retained JAR bytes remain local diagnostic evidence, not an
independent upstream attestation or a reproducible-build claim.

Unlocked coverage was narrowed to two exact manifests:
`tools/contracts/surface/dotnet/Surface.csproj` and
`tests/Contract/fixtures/sdk/CorvusProbe.csproj`. Neither has a sibling
`packages.lock.json`. The first has no package references; the second references
the generated CRM client and therefore needs a real transitive NuGet lock.
No exception for a tool, fixture or empty project was added. Local throwaway
restore feasibility and the final committed-candidate scan are recorded in the
handoff below; neither finding review nor the coverage blocker is cleared here.

Actual throwaway restore evidence: normal Surface restore/locked restore PASS
with zero package entries; normal CorvusProbe restore FAIL `NU1008`. The latter
inherits root central package management while its generated SDK reference uses
explicit package versions. Diagnostic-only restores with
`-p:ManagePackageVersionsCentrally=false` generated authentic locks for both
(Surface0/CorvusProbe6 entries), and subsequent `--locked-mode` restores PASS.
A reviewable remediation is consistent project-level SDK build isolation plus
these generated locks, followed by normal/locked builds and consumer tests.
No repository configuration or locks were changed by this diagnostic; mandatory
coverage still blocks. The centrally managed application projects need no change.

## Stable-ID committed scan — 2026-11-12

Exact implementation candidate `d10c225d0fad214ddf54dbf4c46fe3b76a79dc6d`,
same base `601dde8847cad82d038ea837c01d3da0d7449450`, no merge SHA;
run `local-mvp-triage`, attempt1. Actual mandatory MVP security command **FAIL**,
exit1, `CI_SCAN_INCOMPLETE`,8.642s. [Bounded scan evidence](evidence/2026-11-12-mvp-security-d10c225.json)
retains Gitleaks64 BLOCKED findings over2 surfaces, Trivy10 locks/6 existing-policy
nonblocking records, required `CI_UNLOCKED_DEPENDENCIES`, deferred tool coverage,
and license271 review-required/508 allowlist-pass records. All64 Gitleaks IDs
exactly match the stable review proposals; zero unmatched current findings.
Safe completion preserves failure (`CI_JOB_FAILED`). No finding was approved.
Later evidence-only commits have no full exact-candidate gate evidence; full
five-job/remote acceptance remains OPEN.

## CorvusProbe coverage remediation; Surface blocker — 2026-11-12

CorvusProbe now has an authentic lock for five NuGet packages plus its project
reference. The fixture opts out of central package versions, and the root props
isolate only the exact generated CRM client directory, whose project owns
explicit versions. Application central package management is retained and
verified by real project evaluation. Public contracts, generated SDK content and
the generator are unchanged. Real tests require locked restore, consumer build,
AutomationRule roundtrip and rejection of a corrupted lock; no empty-lock
shortcut or scanner exception was added.

Surface remains uncovered. In a throwaway export, an authentic empty lock
restores in locked mode and rejects an added package with `NU1004`. Mandatory
inventory parsing rejects its zero packages. Actual pinned Trivy0.76.2 scanning
the lock-only staged input reports zero language targets, so the required
lock-target inventory cannot honestly be satisfied. No Surface lock was adopted: accepting an empty list as
package coverage without complete evaluated input closure would weaken the
required gate. The MVP tool-inventory deferral does not authorize a project
coverage exemption. See the handoff for actual tests and exact committed scan.

Exact remediation candidate `ab3125aee60157979186dd0516b7a9dfe946eaeb`,
same base, run `local-mvp-coverage` attempt1: mandatory MVP security **FAIL**,
exit1/`CI_SCAN_INCOMPLETE`,7.695s.
[Safe evidence](evidence/2026-11-12-mvp-security-ab3125a.json) records11 locks
covered (previously10), Trivy PASS with6 existing-policy nonblocking records,
64 Gitleaks findings still BLOCKED, all matching the prior recomputed-hash
review proposals. Zero new/unclassified current secret findings; original64
unmapped IDs remain unresolved. Exact committed export proves Surface is the
only remaining unlocked manifest. License276 review-required/508allowlist-pass
records remain OPEN, as do all deferred areas. Completion preserves failure.
No finding was approved; later evidence-only commits have no full five-job
exact-SHA acceptance evidence.

## Surface bounded local restore coverage — 2026-11-12

MVP now measures the one known empty NuGet target using
`scripts/ci/surface_restore.py`. The checked-in lock was generated by the real
pinned SDK, not handwritten to suppress coverage. The profile pins exact SHA-256
bytes for Surface.csproj, global.json, the root Directory.Build.props,
Directory.Packages.props and the lock. Additional automatic configuration,
response files, SDK selectors or ancestor build/package imports on Surface's
ancestor path are rejected, as are symlinks or changed pinned bytes. These
restrictions deliberately reject even legitimate future profile changes until
reviewed; they do not attempt to infer arbitrary MSBuild semantics from XML.

Only those verified bytes are reconstructed in a new temporary tree. Candidate
source, obj/assets, targets and custom tasks are never copied or executed. The
entire Surface project subtree permits only the two profile files and Program.cs;
all additional entries, including project.user, differently cased variants and
obj project-extension props/targets, fail before execution. Real SDK regressions
first demonstrate each automatic import adds a package against an unchanged
empty lock, then require the profile to reject it. The
worker uses a fresh home and bounded environment, the installed SDK 10.0.403,
explicit offline cleared NuGet sources, disabled restore audit, an explicit
reviewed DirectoryBuildPropsPath, and disabled automatic Directory.Build.targets.
It runs force-evaluated locked restores separately for Debug and Release and
requires successful exits, unchanged lock bytes and exact net10.0 empty targets,
libraries, dependency groups and project-reference graph in newly produced assets.
SDK/framework packs remain software dependencies covered by the separately pinned
SDK inventory; zero NuGet packages is not a software clearance claim.

MVP `dependency_inputs` omits only this exact lock from Trivy after those real
measurements succeed. Trivy must still observe every package of every nonempty
lock. Missing locks retain `CI_UNLOCKED_DEPENDENCIES`; unsupported/changed/failing
profiles and arbitrary empty locks fail closed. The hardening adapter retains its
existing stricter empty-lock rejection. Safe MVP evidence adds `empty_restores`,
with only a fixed profile/target digest, SDK, configuration matrix, zero count
and `measured-local` status; validation rejects modified/duplicate receipts.
Receipts are descriptive output and are never accepted as restore authorization.
This internal summary change requires fresh summaries; old summaries without
that field fail validation rather than silently acquiring coverage.

This is **candidate-owned local CI evidence**, using the same installed-tool
trust boundary as the other MVP checks. It is not an independently administered
profile, authenticated producer, signed envelope, protected check or approval of
any finding. Candidate policy code remains mutable; independently proving it
cannot be bypassed remains deferred hardening in the provisioning proposal.
No secret rule, severity threshold, finding exemption, lock inventory for another
project, policy transition or remote setting changed. Exact committed-candidate
scanner evidence and final verification are recorded in the session handoff.
