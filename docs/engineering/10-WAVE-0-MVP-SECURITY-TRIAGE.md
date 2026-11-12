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
