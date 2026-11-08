# Bounded Contexts / Ownership / Dependency Rules

| Context | Source of truth | Published interfaces |
|---|---|---|
| Identity (external IdP) | accounts, credentials, login sessions | OIDC tokens/claims; provisioning adapter |
| Tenant/Access | organization, membership, grants | tenant API, authz policy |
| CRM | contacts, companies (P1), tags, custom fields | contacts REST, CRM events |
| Integration | installs, external mappings, deliveries, schemas | module manifest/registration, webhooks |
| Automation | definitions, runs, action requests | rule APIs, trigger/action registry |
| Marketing P1 | templates, subscriptions, consent, campaigns | provider-agnostic email API/events |
| LMS external | courses/progress/enrollment | LMS API/events |
| Trading external | trading accounts/trades/journal | Trading API/events; sensitive data minimal |
| Backcom external | commission ledger/settlement/payout | Backcom API/events, no CRM ledger writes |

## Contact vs User
- `user_id`: stable subject from IdP + issuer, representing authenticated account.
- `contact_id`: CRM business entity; may be anonymous, not always linked to a user.
- `tenant_id`: business account partition/authorization boundary.
- `external_entity_ref`: `{installation_id, entity_type, external_id}`; unique per source.
- Link and unlink operations are audited and policy-checked; no auto merge on equal email.

## Dependency direction
- Domain → contracts/interfaces, NOT adapters/EF/network.
- Application → domain and ports; infrastructure implements ports.
- Integration translates external payload to domain commands; no business logic inside webhook handler.
- Shared library only primitives/observability, NOT shared `ContactEntity`, DbContext, repositories or mutable domain models.
- One business transaction belongs to one module; inter-module domain changes use durable inbox/outbox and idempotent ports. Technical outbox/audit append is an explicit exception: the owning module writes business rows, audit entry and outbox intent on the same PostgreSQL connection/transaction through infrastructure ports. No other module domain tables/DbContext access; no remote I/O inside transaction.

## CRM customer projection
`Contact360` is a materialized/read projection of selected external facts; original LMS progress/trade/commission remain in owner domain. Projection stores source, timestamp, mapping version and provenance.
