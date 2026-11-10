# Wave 0 Plan 3 — specification handoff

## Objective and authorization
User reports Plan 2 complete and requests the Plan 3 spec. Scope is documentation only: frontend foundation and contract toolchain, following the previously agreed four-plan Wave 0 split. No implementation, dependency installation, generation, builds, push or deployment authorized by this spec request.

## Verified starting point
- Branch `main`, initial HEAD `3af03d0`, clean working tree.
- Plan 2 integration handoff/history confirm merge and publication; root `compose.yaml`, exact secret/project boundary checks, canonical realm metadata and JSON/Activity runtime exist.
- Historical merged-main results: Compose14/non-Compose70 .NET, Contract32/Local20 Python passed. These were read from Plan 2 handoff, not rerun and not Plan 3 evidence.
- `apps/` and `sdks/` absent. Canonical operational OpenAPI is implemented; business OpenAPI remains pre-runtime specification.

## Proposed output and decisions
- [Plan 3 spec](../superpowers/specs/2026-11-10-wave-0-plan-3-frontend-contract-toolchain-design.md).
- Proposed frontend runs via host Node/npm separately from SDK-free backend Compose; generated operations client calls existing health API through tightly scoped dev proxy. No login/business screens or API changes.
- TS/C# clients for both canonical OpenAPI files, reproducibility, local packaging and independent consumer wire/header tests. Stable generators require actual full-contract compatibility proof before selection.
- Trusted Git base for local breaking/security/SDK-surface detection; conservative standalone JSON Schema review-required gate. CI and publication remain Plan4/later scope.
- These detailed choices are author proposals for review, not approved implementation decisions. Builder review complete. Three gaps were raised and resolved: authorization extensions/scheme binding, additive versus incompatible SDK surface, and conditional-schema wire fidelity versus server-only validation. Reviewer confirmed no remaining blocking ambiguity in those sections.

## Changed files and verification
- New spec and this handoff only; no code or runtime configuration changed.
- `git diff --check` and `git diff --cached --check`: PASS, including both staged new Markdown documents. No application/backend/browser/SDK/diff tests run in this spec-writing task.
- Local documentation commit subject `docs: specify Wave 0 Plan 3 frontend and contract toolchain`; resolve resulting SHA from git history. No push or runtime changes.
- Official OpenAPI Generator, oasdiff and Vite sources referenced in spec; no exact current release/version claims inferred. Builder verifies versions and behavior during implementation.

## Next steps
1. Present reviewed spec for user review; no builder-review blocker remains.
2. After user requests it, write implementation plan using writing-plans skill; no application coding from draft alone.
3. Keep Wave0/P0, login/tenant/business/remote DemoModule/DR and remote CI gates OPEN.
