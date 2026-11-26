# Wave 1 identity contract — baseline review package

Status: **OPEN, prepared for independent review**. This is an author's technical assessment, not an independent security approval, trusted-baseline change, waiver, deployment approval, or passing gate. Wave 1 and P0 remain OPEN. Under the user's conditional work-order decision, Wave 2 can begin only after all other applicable Wave 1 requirements and gates pass, with the security gate remaining deferred and honestly reported.

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
