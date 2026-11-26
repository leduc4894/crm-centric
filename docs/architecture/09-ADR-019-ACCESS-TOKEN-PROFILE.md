# ADR-019 — Access-token purpose for Wave 1 identity APIs

- Date: 2026-11-26
- Status: Accepted for the unreleased Wave 1 candidate under the authorized security fix; deployment and consumer migration evidence remain OPEN.
- Scope: tenant list/create, membership list/get/patch, invitation and tenant audit routes. Does not assert implementation of other P0 routes.

## Context and decision

An issuer can sign an ID token with the same audience and subject as a CRM access token. Signature, issuer, audience and lifetime checks alone cannot establish token purpose. The existing invitation/audit implementation requires the Keycloak profile: exactly one signed JSON payload member `typ`, with scalar string `Bearer`, reserved by the issuer exclusively for access tokens. Extend that requirement to all implemented Wave 1 identity routes in scope. The JWT JOSE header `typ` and HTTP `Bearer` scheme are not substitutes. Keep existing issuer, signature, audience, lifetime, algorithm, scope, staff and tenant checks.

This is a **behavioral breaking change** for valid access tokens without the required payload member, including tokens from otherwise supported issuers that use a different profile. They now receive HTTP 401 `AUTHENTICATION_REQUIRED` and a correlation ID. Token formats are not fully described by the previous OpenAPI HTTP bearer JWT scheme; a structural OpenAPI diff or SDK surface comparison alone cannot detect this break. No released consumers are documented, but unknown consumers must not be assumed migrated. The API supports managed issuers only if they can enforce this precise access-token-only discriminator; client-editable claims or mapping it to ID tokens defeats the security boundary.

## Migration and rollback

1. Inventory every issuer, browser and service consumer of the affected routes; identify owner and compatibility before rollout. Do not log or attach tokens or raw claims to tickets or CI output.
2. Configure each issuer to emit exactly one signed scalar payload `typ=Bearer` exclusively in access tokens for the CRM audience, and prevent client/user control or equivalent mapping on ID tokens. Provisioning clients retain their exact scope; invitations retain verified-email and staff requirements. If an issuer cannot provide this guarantee, block its migration and do not deploy this candidate for it.
3. In an isolated environment, test representative browser and service consumers using the actual configured provider: valid access token succeeds for authorized role/scope; same-audience ID token and absent, duplicate or non-string type are rejected; verify normal authorization and safe 401 reason/correlation without recording tokens. Exercise tenant and membership reads and writes and invitation/audit routes. Existing local HTTP consumer regressions and the real Keycloak browser fixture are partial evidence only; rerun on the final candidate and record issuer-specific results.
4. Deploy only after consumer owners confirm readiness, the ADR and contract diff receive normal review, and required gates pass. Monitor safe reason/correlation aggregates and issuer configuration without token content. This ADR grants no security waiver.
5. If compatibility blocks rollout, stop or roll back the deployment and forward-fix the issuer or introduce a separately reviewed, equally secure discriminator. Rolling back the server to a version accepting indistinguishable ID tokens reopens the documented vulnerability; do not treat that rollback as a safe security resolution. No database migration or credential rotation is implied.

## Verification and open gates

The local signed-issuer HTTP tests reproduce ID-token acceptance before the fix and rejection after it; additional absent/array/duplicate cases exercise each newly restricted route. Existing success tests exercise provisioner and tenant/member access. Real Keycloak Chromium proof predates the security fix and must be rerun on the final candidate. Packaged SDK fixtures use synthetic HTTP and do not prove real issuer compatibility. Actual provider/consumer migration, exact-candidate tests, independent review, security gate and Wave 1/P0 acceptance remain OPEN.
