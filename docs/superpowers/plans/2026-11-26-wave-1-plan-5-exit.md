# Wave 1 Plan 5 — Final Verification and Exit Implementation Plan

> **Execution:** one coding agent, sequential work and small reviewable commits. This plan supersedes the earlier instruction to avoid full suites **at Wave 1 exit**: the user now requests full verification for closure. Continue using focused RED/GREEN tests while changing code; run complete exit checks once on the final candidate. Security dependency remediation is deferred by the user, not waived.

**Goal:** Complete and document the remaining Wave 1 review, acceptance evidence, full regressions and required gate results, then close Wave 1 only when its actual exit conditions are met.

**Architecture:** Verify the implemented OIDC, tenant authorization/RLS, invitation and audit slices against existing contract and acceptance requirements. Preserve exact candidate identity, observed results and historical failures. Future module, automation and recovery work is not fabricated to pass current tests.

**Tech stack:** .NET 10, PostgreSQL/Testcontainers, Keycloak, React/TypeScript, Vitest, Playwright, existing pinned CI toolchain and `.github/workflows/wave0.yml`.

**Sources:** [Wave 1 delivery map](2026-11-15-wave-1-delivery.md), [Wave 1 roadmap](../../roadmap/01-BACKLOG.md), [acceptance matrix](../../engineering/03-ACCEPTANCE-MATRIX.md), [Plan 4 evidence](../../handoff/2026-11-26-wave-1-plan-4-verification-handoff.md), [Plan 3 evidence](../../handoff/2026-11-26-wave-1-invites-audit-handoff.md), [security status](../../handoff/2026-11-26-repo-status-handoff.md).

## Scope and guardrails

- Baseline at planning: local `wave-1` HEAD `02315d4`, `main` remains at Wave 0 `40bc290`; check Git/worktrees and handoffs at execution. Preserve untracked user documents. The user requested full Wave 1 closure; run full suite and required gates at final verification, not repeatedly after every small change.
- **Security deferral:** user explicitly defers the finding awaiting a compatible dependency. The last documented security gate is FAIL due to Semgrep's pinned MCP dependency; this is not an approval, waiver, exception, suppression or PASS. Continue all independent tasks. Record it as an open blocker; do not close Wave 1 or start Wave 2 while this required gate remains failed. When a supported fix exists, the remaining security remediation and gate re-run are needed before closure; no automatic network publication, dependency bypass or scan-policy change.
- Independent final Plan 4/branch review must be completed and findings resolved before exit. Respect `AGENTS.md`: one coding agent at a time. No unauthorised deploy, merge, push, settings change or external message; local verification is authorized by the user. Remote/hosted evidence is reported only if actually obtained under separate authorization.
- Integration tests use real PostgreSQL/Testcontainers. No tenant ID from body/header grants access. No contact/user conflation, secrets in logs or contract-breaking diff without ADR/migration/consumer tests. Every closed error path has reason token and correlation ID. P0 remains open until all remote DemoModule, duplicate, tenant, outage/recovery and restore drills pass.
- Existing real Keycloak browser A/B evidence is valid only for the exact recorded revision and Chromium/Linux conditions. Reuse it for traceability; rerun the targeted scenario on final candidate if application, fixture or runtime inputs change or if the full wave-exit checks require it. Never present old evidence as a fresh final-candidate run.

## Review focus

1. A/B API and RLS isolation, forged headers and pooled connection reset remain enforced across invite, membership and audit; verify membership and race tests plus real browser evidence (Tasks 1–2).
2. Wrong issuer/audience, signature, token type, expiration, replay/state and unverified email are denied; test OIDC/browser/invite negatives and log redaction (Tasks 1–2).
3. Expiry/revoke/accept races, last-owner and audit atomicity remain correct under actual PostgreSQL concurrency (Tasks 1–2).
4. Full gate results and test counts are bound to one immutable candidate; skipped, cancelled, unavailable and failed results remain non-PASS (Tasks 3–4).
5. Dependency findings, production CSP/reverse-proxy assumptions and absence of later-wave worker/replay behavior are explicitly recorded; no false closure or security downgrade (Tasks 1, 4–5).

## Task 1 — Traceability and final review

**Files:** create `docs/engineering/11-WAVE-1-EXIT-VERIFICATION.md`; inspect `docs/engineering/03-ACCEPTANCE-MATRIX.md`, `docs/handoff/2026-11-26-wave-1-*-handoff.md`, Plan 1–4 source and tests. Do not alter behavior in this documentation task.

- [ ] Record baseline commit, tracked/untracked status, runtime versions and current security verdict. Map US-01/02, RT-IAM-01, RT-OIDC-01, RT-TENANT-01 and audit requirements to exact test names, commands, candidate SHA, results, source handoff and remaining gaps. Separate Wave 1 API/SQL/browser requirements from future worker/replay and remote module evidence; no blanket RT closure for unimplemented future paths.
- [ ] Review all Wave 1 changes against `40bc290`, focusing token/session leakage, role/verified staff boundary, RLS/grants, lock ordering, audit atomicity, browser proxy/CSP and public contract. Record findings, severity and evidence; obtain required independent review before claiming it complete. Do not spawn a second coding agent. Review of `oauth4webapi` adapter and callback cleanup must consider exact validation guarantees rather than assuming library name proves security.
- [ ] For each confirmed issue, add a focused failing test first, minimally fix, rerun that test and directly affected regression, review and commit separately. If no issue, state that with the review's scope and reviewer. Do not silently omit known moderate npm findings or deferred MCP finding.
- [ ] Verify `git diff --check` and links in new evidence document. Record review and evidence gaps as OPEN until observed results exist.

## Task 2 — Explicit Wave 1 acceptance evidence

**Files:** existing tests in `tests/Integration/OidcAuthenticationTests.cs`, `TenantIsolationTests.cs`, `TenantAuthorizationTests.cs`, `MembershipEndpointsTests.cs`, `InviteAcceptanceTests.cs`, `AuditEndpointsTests.cs`, and `apps/crm-web/tests/e2e/browser-oidc.spec.ts`; add focused tests only for a documented gap.

- [ ] Cross-check existing tests for A/B independent sessions, forged tenant/role, tenant-scoped SQL/RLS, pooled connection cleanup, token issuer/audience/signature/lifetime/type, PKCE/state/nonce/replay, invite expiration/single-use/revoke race, last-owner and transactional audit. Mark each coverage cell PASS only against observed results; identify any missing tests before implementing them.
- [ ] Add and run focused RED/GREEN tests only for demonstrated gaps. Example regression commands when their tests are changed: `dotnet test tests/Integration/Integration.csproj -c Release --no-restore --filter 'FullyQualifiedName~OidcAuthenticationTests|FullyQualifiedName~TenantIsolationTests|FullyQualifiedName~InviteAcceptanceTests|FullyQualifiedName~AuditEndpointsTests'`; `CRM_REAL_OIDC_TEST=1 npm exec --workspace @crm-centric/crm-web -- playwright test tests/e2e/browser-oidc.spec.ts --project=chromium --grep 'real provider signs'`. Do not run these again solely to repeat an unchanged passing result unless final verification requires it.
- [ ] Record real provider/browser limitations, cleanup ownership and any skip/failure. Worker/replay, external module and disaster recovery tests belong to later waves; list them as P0 open, never simulate them as a Wave 1 pass.

## Task 3 — Full verification on the final candidate

**Inputs:** clean committed candidate SHA and immutable base `40bc290`; existing exact pinned toolchain; workflow job definitions in `.github/workflows/wave0.yml`. No application edits while checks are running.

- [ ] Confirm free disk/Docker prerequisites and prepared pinned tools. Run `dotnet restore CrmPlatform.sln --locked-mode`, `dotnet build CrmPlatform.sln -c Release --no-restore`, `dotnet test tests/Integration/Integration.csproj -c Release --no-restore`; record total/pass/fail/skip, candidate SHA and durations. No test output containing credentials or payloads in documents.
- [ ] Run full existing frontend and contract suites using the repository's pinned Node/npm/Python environment: `npm run typecheck`, `npm run lint`, `npm run build`, `npm test`, `npm run test:e2e`, and contract/SDK checks invoked by the existing workflow. Ensure the real-provider opt-in case is separately run with `CRM_REAL_OIDC_TEST=1` when required; a skipped real-provider test is not PASS. Record exact commands and outcomes.
- [ ] Run existing local required workflow gates exactly as defined by `.github/workflows/wave0.yml` using its supported revision/receipt setup: backend, Compose, contracts-sdk, web-browser and security, then required aggregate. Use a correctly prepared immutable local revision identity; do not substitute a different base or claim local simulation is hosted CI. Security gate may reproduce the deferred dependency FAIL; report its actual result, not an expected result. Avoid re-running a failed gate until evidence or inputs change.
- [ ] For each failure, capture safe reason/correlation ID and identify failing test; apply systematic debugging and targeted fixes followed by affected regression. New code or lock changes require a new committed final candidate and fresh relevant gate evidence; no transfer of results across different candidate trees. A cancelled full suite is NOT RUN/INCOMPLETE, not PASS.

## Task 4 — Security and operational disposition

**Files:** update `docs/engineering/11-WAVE-1-EXIT-VERIFICATION.md`, `docs/engineering/03-ACCEPTANCE-MATRIX.md`, and operations guidance only when supported by evidence.

- [ ] Preserve current historical `CI_SCAN_FINDINGS` and the user decision to defer Semgrep/MCP remediation. Separately identify any npm findings and their applicability without printing sensitive scanner payloads; record actual evidence and disposition. Do not remove scanner, alter thresholds, forge metadata, unpin dependencies or mark a deferred finding as resolved.
- [ ] Document required production CSP for emitted callback script, Trusted Types, issuer/audience, reverse proxy and TLS trust. Distinguish local test evidence from production verification. Any production deployment or exception remains outside this plan unless separately authorized.
- [ ] If supported upstream dependency fix becomes available and the user resumes the deferred security work, pin supported versions and locks, verify clean install and scanner positive/negative coverage, then rerun affected security and required gates. Until then, security remains a blocker to unconditional Wave 1 closure.

## Task 5 — Exit decision and handoff

**Files:** `docs/engineering/11-WAVE-1-EXIT-VERIFICATION.md`, `docs/engineering/03-ACCEPTANCE-MATRIX.md`, `docs/handoff/YYYY-MM-DD-wave-1-exit-handoff.md`.

- [ ] Assemble a gate table with requirement, candidate SHA, command, actual result/counts, linked evidence and owner/next action for anything open. Include review status, full suites, real IdP A/B, token/RLS tests, compatibility, backend/Compose/contracts/browser/security/aggregate and hosted evidence if required. Note that scope/acceptance cannot be inferred from historical checks against other commits.
- [ ] Mark Wave 1 CLOSED only if all required Wave 1 acceptance, review and required gates actually pass on appropriate final evidence. A deferred failing security gate means Wave 1 remains OPEN; report all completed work and the specific remaining blocker. Any change to that gate or formal exception requires an explicit applicable decision, not inference from deferral.
- [ ] Keep Wave 2 blocked until Wave 1 is closed under project work order. P0 remains OPEN regardless of Wave 1 closure and requires later-wave acceptance drills. Run `git diff --check`, update handoff with actual commands/results and branch/commit/uncommitted work, then commit scoped documentation when appropriate.
