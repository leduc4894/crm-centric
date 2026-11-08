# Threat Model (STRIDE) & Security Gate

| Threat | Attack path | Required control | Test |
|---|---|---|---|
| Spoofing | fake module/webhook | signed payload, credential binding, nonce/time | invalid HMAC/expired window |
| Tampering | payload modified in flight | TLS + HMAC over raw body | tamper one byte |
| Repudiation | admin denies module install | append-only audit + actor/trace | audit stored with request |
| Information disclosure | tenant IDOR, logs | RLS, authz, redaction | cross-tenant probes |
| Denial of service | webhook flood, expensive query | quota/rate limit, size/time budgets | fuzz + load |
| Elevation of privilege | extension asks extra scopes | admin approval, scoped grants, no dynamic code | scope escalation denied |
| SSRF | manifest URL to metadata IP | outbound allowlist, DNS/IP checks, redirects validation | private IP / redirect tests |
| Supply chain | malicious npm/NuGet/container | pinned versions, scan, SBOM, release gates | dependency review |
| Workflow injection | arbitrary JS condition or malformed schema | safe DSL, depth+execution limits | property/fuzz tests |
| Consent abuse | marketing without permission | purpose-specific consent, suppression, unsubscribe | negative send tests |

## P0 Security Release Blockers
No cross-tenant disclosure, no auth bypass, no accepting unsigned/webhook replay, no SSRF to local services, no plaintext secrets, no PII in traces. Security incident runbook required before public production.
