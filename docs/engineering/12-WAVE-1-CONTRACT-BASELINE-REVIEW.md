# Wave 1 identity contract — baseline review package

Status: **MVP scoped contract review authorized; deployment and issuer/consumer readiness remain OPEN.** The user selected a narrowly scoped reviewed-change mechanism on 2026-11-27. It does not advance the trusted baseline, approve deployment, or independently verify the candidate. Wave 1 and P0 remain OPEN. Under the user's conditional work-order decision, Wave 2 can begin only after all other applicable Wave 1 requirements and gates pass, with the security gate remaining deferred and honestly reported.

## MVP reviewed breaking change

The MVP compatibility option accepts only the exact reviewed comparison from base `40bc2905b7750b139ff0607000127a3e5c06e2d8` to the pinned CRM contract documents and the listed ADR, selected implementation and test file hashes, before `2027-02-11T00:00:00Z`. Any other security projection change or change to those reviewed inputs remains blocked. OpenAPI breaking detection, SDK surface comparison and historical consumer checks still execute. A successful accepted case is reported as `CONTRACT_BREAKING_REVIEWED` / `reviewed-breaking`, not as compatible; CI job logs emit a bounded review marker. Hardening/default mode does not accept it.

This mechanism and its fixed review live in the candidate and do **not** provide independent enforcement against a candidate changing the gate or other unpinned files. The exact recorded file hashes are a bounded review scope, not a claim that all runtime code is immutable. The existing independent technical review was performed on an earlier revision and is not formal rollout approval. The user authorized this MVP governance decision; it does not turn the deferred security scan into a pass. Actual issuer exclusivity, consumer readiness and deployment prerequisites in ADR-019 remain open.

## Exact scope

- Comparison base: `40bc2905b7750b139ff0607000127a3e5c06e2d8` (local `main`, also the recorded `origin/main` at preparation); reviewed implementation candidate: `c812ccf4e40c9baeb06ad058f3ecae2c6eae4ec3` on `wave-1`. Later commits need their own evidence and review.
- CRM OpenAPI Git blobs: base `9d89d2c9b52a2d5eb6b24aea23f5f9b0c40c12a8`; candidate `41d81471273949d3033e3101af03fe942f38846d`. The sole OpenAPI diff adds a bearer scheme description; generated SDK provenance and a .NET generator lock also differ. These facts do not make the behavior compatible.
- [ADR-019](../architecture/09-ADR-019-ACCESS-TOKEN-PROFILE.md) records the actual break: the implemented identity routes require the issuer to reserve exactly one signed scalar JWT payload `typ=Bearer` exclusively for access tokens. Existing consumers using other profiles may be denied; an issuer must not emit the same discriminator in ID tokens. The ADR lists migration and rollback. The issuer inventory, real consumer-owner confirmations and production migration remain OPEN.
- API code validates issuer, audience, signature, expiry and RS256 before endpoint access-token purpose enforcement. Tenant, membership, invitation-management and audit endpoints require the discriminator. Invitation acceptance has a separate identity-evidence path; consult the ADR for error differences. See `src/Platform.Api/Identity/AuthenticationSetup.cs`, `AccessTokenEvidence.cs` and endpoint mappings.

## Evidence and review required

- `dotnet test tests/Integration/Integration.csproj -c Release --no-restore --filter 'FullyQualifiedName~Consumer_rejects_missing_or_ambiguous_access_token_type|FullyQualifiedName~Same_audience_id_token' --logger 'console;verbosity=minimal'`: PASS 10/10 on `c812ccf`, no skips; these are signed synthetic-issuer HTTP tests, not proof every external issuer migrated. Existing valid-access-token behavior was covered in prior focused runs listed in the [debug handoff](../handoff/2026-11-26-wave-1-debug-handoff.md).
- `CRM_REAL_OIDC_TEST=1 npm exec --workspace @crm-centric/crm-web -- playwright test tests/e2e/browser-oidc.spec.ts --project=chromium --grep 'real provider signs'`: PASS 1/1 on `c812ccf`, local Keycloak/Chromium; not production or an exhaustive negative-token test.
- `python scripts/contracts/compatibility.py --base-ref 40bc2905b7750b139ff0607000127a3e5c06e2d8 --mvp-reviewed-policy`: FAIL `SECURITY_REVIEW_REQUIRED`, correlation `bcf9d02d-33a3-4bb5-bb43-0fea75623633`, on `c812ccf`. The comparison includes the security scheme description. No compatibility PASS is inferred.
- Backend gate PASS 256/256 on earlier `2daa44d`, not on this candidate. Full integration and final candidate gates remain OPEN as described in the [exit handoff](../handoff/2026-11-26-wave-1-exit-handoff.md).

An independent reviewer must assess the token-purpose invariant, every affected endpoint and consumer, security and migration/rollback implications, tests and real issuer guarantees; the author's inspection here does not substitute for that review. Resolve the outstanding ADR-019 issuer inventory and consumer-owner confirmations before rollout. The deferred security gate and its findings remain separate and are not approved by this package.

## Proposed advancement procedure — not executed

1. Identify the owner and separately reviewed mechanism for advancing the trusted target baseline. The repository's workflow derives it from immutable GitHub event revisions; the repository does not establish actual branch protection or an independent baseline-enforcement mechanism. Obtain the required owner review for the exact immutable contract and supporting ADR, migration and consumer evidence.
2. Have the authorized maintainer advance the trusted target through that separately reviewed process. This may require a separate reviewed baseline change and an explicit decision about how it is integrated; this package does not choose or mutate a target branch. Do not use the candidate as its own base or fabricate event identities to obtain a pass.
3. After the baseline is actually advanced, verify its immutable SHA and ancestry, compare the candidate to that real trusted baseline and run contracts-sdk and web-browser with the unchanged gate. Record actual outputs and any remaining breaking changes. Separately run the remaining required non-security exit checks and assess the final candidate.

No trusted baseline, branch, scanner, policy, deployment or remote state was changed while preparing this package. A merge, push, hosted settings change or formal exception is not implied by preparation.

## Follow-up evidence and independent review

On committed candidate `fa9f75c3ec3d7295cd3b7c2498a32e4b8bf70e86` with base `40bc2905b7750b139ff0607000127a3e5c06e2d8`, local full integration PASS 245/245, no skips; backend gate PASS 256/256, correlation `f3e28244-8f3f-44bb-84f5-7f93af95c69b`; Compose gate PASS 16/16, correlation `17ae1185-9dc2-4cbc-9f95-a4caf84e881e`. Frontend typecheck, lint, build and unit tests PASS (116/116); real-provider Chromium PASS 1/1; ordinary browser E2E PASS 7 with one provider skip, which was exercised separately. All are local, not hosted CI. These results belong only to `fa9f75c`; later documentation changes are different commits.

An independent reviewer examined the contract/security change and fixture commits through `fa9f75c` and identified no critical implementation finding. Important unresolved conditions are issuer-exclusive token-purpose guarantees and consumer readiness, plus the separate trusted-baseline decision and gate evidence. This is an independent technical review, not an affirmative rollout, maintainer approval or baseline advancement. The reviewer also identified stale ADR wording; the follow-up updates that wording without changing behavior.

Read-only GitHub inspection found remote `main` at the base SHA above; the authenticated account reported admin access, the classic branch protection endpoint returned 404 and the repository rulesets endpoint returned an empty list. These observations do not prove an external review mechanism exists or is absent outside the repository. No settings were changed.
