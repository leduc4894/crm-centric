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
