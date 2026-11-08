# Identity, Tenancy, Authorization — thiết kế bảo mật bắt buộc

## Identity
- OIDC Authorization Code + PKCE for browser; use standards-based library, avoid custom OAuth grant code.
- IdP recommended: Keycloak (self-hosting) or managed OIDC provider; separate security/credential boundary. Keycloak does NOT mean CRM Contact table becomes user table.
- Issuer, audience, exp, nbf, token signatures must validate. Use JWKS caching and key rotation; reject alg confusion and unsigned tokens.
- Stable identity key `(issuer, subject)` not raw email. Scope tenant context through verified membership, not untrusted browser header.
- For backend to backend use client credentials + scoped tokens; never reuse end-user access token as service credential.
- MFA for admins; session termination, recovery, refresh token policies delegated to IdP.

## Multi-tenancy
- All domain rows carry `tenant_id` (except global platform registry), all query paths require verified tenant context.
- Unique constraints include tenant_id where appropriate; enforce IDOR prevention in service and SQL queries.
- PostgreSQL RLS defense-in-depth for core tenant tables; set context securely per transaction and reset with pooled connections; test fail-closed when missing.
- Tenant owner/admin/agent/integration-service roles; separate permissions scopes `crm.contacts.read`, `crm.contacts.write`, `integrations.install`, `automation.manage`.
- Do not trust installation_id or tenant_id from webhook body; resolve from verified credential and installation binding.

## Module security
- Manifest URL SSRF guards: HTTPS only in production, block loopback/link-local/private IP by default, DNS rebinding checks, redirect validation, egress control; explicit allowlist exceptions for approved private/self-hosted environments.
- Install flow requires tenant admin, approval of requested scopes, OAuth consent where applicable, bound credentials and signed challenge/response.
- Webhooks: HMAC-SHA256 over exact bytes + timestamp + event id (or asymmetric signature), per-installation rotating secret, constant-time compare, replay window, event-id dedup; require TLS.
- Module action callbacks use OAuth client credentials, least privileges, allowlisted actions, audit, retry limits; block arbitrary request URLs, scripts, SQL or DLL execution.
- External panels use schema-rendered sanitized content only; CSP, strict allowlist links.

## Privacy
- Explicit marketing consent (purpose, timestamp, source, proof); transactional email separate from marketing.
- Retention, deletion/export request, legal hold and audit policy per tenant/jurisdiction; document handling of backup and derived data.
- Trading/Backcom integration minimizes transmitted personal/financial data. No raw brokerage credentials or payment card data in CRM.

## Normative P0 details
- Verified tenant selection, role matrix và invite/identity lifecycle: `07-P0-ACCESS-AND-STATE.md`.
- Webhook framing, key binding/rotation và replay: `../contracts/WEBHOOK-PROTOCOL.md`.
- Publisher trust, SSRF trên mọi schema/endpoint fetch, callback/panel authorization: `../contracts/MODULE-CALLBACKS.md`.
- Contract validation không chứng minh runtime controls; xem acceptance matrix.

## Tests
- Cross-tenant IDOR, forged role/tenant, webhook replay, JWT wrong issuer/audience, expired signatures, server-side request forgery, scope escalation, revoked installation and unsubscribe enforcement must be automated.
