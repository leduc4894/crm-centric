# Wave 0 Plan 3 — implementation handoff

## Objective and status

User authorized execution of [Plan 3](../superpowers/plans/2026-11-10-wave-0-plan-3-frontend-contract-toolchain.md) according to SDD on a new branch, explicitly without a worktree. [Design specification](../superpowers/specs/2026-11-10-wave-0-plan-3-frontend-contract-toolchain-design.md) and project AGENTS.md govern the work. Branch `feat/wave-0-plan-3-frontend-contract-toolchain` starts at `5257abf`; no implementation task has completed. No SDK, gate or frontend was committed. Wave 0/P0, Plan 4 CI and remote acceptance remain open. Do not merge, push or deploy as part of this authorization.

## Investigation and decisions

- Docker daemon 29.9.3 is available; .NET SDK 10.0.403 runs in the matching SDK image. Host Node 26.13.0/npm 11.20.0 needed libatomic from that image; Java and dotnet were absent from host PATH. These observations do not establish the required pinned frontend toolchain.
- OpenAPI Generator 7.27.0 default TypeScript and C# outputs generated from both canonical contracts, but executable probes showed valid AutomationRule actions lose fields in TypeScript and become empty objects in C#. Build success does not establish wire fidelity.
- The stable C# RestSharp template round-tripped isolated action variants, but failed deserialization of schema-valid full rules with leaf and nested conditions. Its generated diagnostics also include raw JSON. Template-only matching cannot safely disambiguate these unions because generated condition models lose `op` const information.
- Kiota 1.38.0 generated a union factory that initializes no action member; NSwag.ConsoleCore 14.7.1 generated C# that failed to compile (undefined `Condition`) and omitted action fields. These findings are limited to the tested versions/configurations and do not prove that all generators are unsuitable.
- `openapi-typescript` 7.13.0 with `--empty-objects-unknown` produced promising literal action types and dynamic input shapes, but no complete client or consumer proof. No generator selection or contract weakening was approved.

## Verification and artifacts

- `.venv/bin/python -m unittest discover -s tests/Contract -v`: PASS, 32 tests, as reported by the implementer.
- Candidate generation and exploratory executable probes: failures described above; no Task 1 generated-check TDD, determinism, four-client fidelity/build or packaged-consumer acceptance completed.
- Detailed report: `.superpowers/sdd/2026-11-10-wave-0-plan-3-frontend-contract-toolchain/task-1-report.md` (git-ignored and local only). Probe files are in `/tmp/plan3-generator-proof` and may disappear. Neither is durable committed evidence.
- Plan 2 historical verification was not rerun in this session. No full project suite, browser, real-stack, or remote acceptance tests were run.

## Next steps

1. Resume Task 1; investigate a supported stable schema-aware generator or audited local generation approach that preserves all valid recursive condition and action variants, nullable values and wire fields, without unsafe diagnostics or rewriting DTOs by hand. Prove complete-rule round trips and canonical-schema negative cases; do not downgrade contracts.
2. Once a viable candidate is proven, follow Task 1 RED→GREEN, deterministic generation, provenance and four SDK build requirements; run required checks and review before moving to Task 2.
3. Continue Tasks 2–7 sequentially with one coding agent at a time and update this handoff as work progresses. Exact-SHA evidence and all W0P3-01–12 checks remain outstanding.
