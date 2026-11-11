# Wave 0 Plan 4 — specification handoff

## Objective and authorization
User requested a Plan 4 spec after completing Plan 3. Work is design documentation only; no implementation, push, deployment or repository-settings change is authorized by this request.

## Status and context
- [Proposed spec](../superpowers/specs/2026-11-11-wave-0-plan-4-ci-wave-exit-design.md) covers CI, security scans, remote evidence and Wave 0 exit.
- Starting branch `main`, HEAD `df993ae`, clean working tree. Plan 3 integration handoff confirmed publication and prior local results.
- Existing remote contract workflow was green but skipped 55 Plan 3 tests; the spec requires prepared full-suite evidence and visible skip accounting. Prior results are historical, not Plan 4 results.
- CI topology, policy thresholds and job names are proposals, not approved implementation decisions. P0 and security acceptance remain OPEN.
- Builder review found two gaps, resolved in spec: C#/TypeScript static security analysis is required; branch-protection enforcement is reported separately and is not a Wave 0 technical-exit prerequisite. Unknown enforcement stays OPEN, never claimed verified.

## Files and verification
- New spec and this handoff only; no workflow, source, secret or dependency changes.
- Runtime, scanners and CI: NOT RUN; specification task only. Official GitHub docs referenced in spec.
- Staged whitespace check required before local docs commit. Commit subject: `docs: specify Wave 0 Plan 4 CI and wave exit`; resolve SHA from git history. No remote push.

## Next steps
1. User can review the design; builder-review gaps were resolved.
2. Upon user request, write the implementation plan. Do not modify repository settings or publish without the relevant authorization.

## Independent review and corrections

User requested a review and corrections of the Plan 4 spec. Two read-only reviewers examined revision/base semantics and security/evidence separately. Verified findings against current repository scripts and GitHub's official required-check/event documentation, then revised the spec.

Corrections: bind PR merge parents/head/base and push forced/after SHA; define independently attested policy transition with old/new evaluators and first-push proof; require evidence linking reviewed PR and final main acceptance SHA; include `tests/Local` and prevent frozen total-count/partial-discovery loopholes; define merge queue boundary; enforce fail-closed job aggregation and honest whole-workflow cancellation semantics; cover unverified secrets, commit history, secret-safe live output, scanner positive fixtures, DB freshness, and image identity provenance. Reviewers confirmed their findings addressed in scoped re-review. No implementation or remote verification was performed.

`git diff --check` and staged diff whitespace check run before the review-correction commit. Runtime/scanner/CI tests remain NOT RUN; source-only documentation task. Updated files: Plan 4 spec and this handoff. Local commit subject `docs: tighten Plan 4 CI specification after review`; no push. Branch `main`, parent `d3b5a40` before correction; verify current HEAD/status on resume. Next: user review, then write implementation plan on request; Wave 0 and P0 remain OPEN.
