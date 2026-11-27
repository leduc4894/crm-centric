# Identity provider configuration

The local profile provides an identity provider, metadata inspection and API bearer
validation and a browser OIDC slice. Full tenant lifecycle, production deployment and
Wave 1 acceptance remain incomplete.
A CRM Contact is not an authenticated Identity User. Wave 1 must use stable issuer/sub
and explicit tenant-scoped linkage rather than matching email addresses.

## Local profile

The pinned Keycloak image uses `start-dev` only on the loopback local profile. The
canonical issuer is `http://localhost:8080/realms/crm-local`; `LOCAL_IDP_PORT`
changes the public port consistently through `KC_HOSTNAME=http://localhost:<port>`.
The adapter accepts only `start-dev` or `start-dev --import-realm`, a canonical
HTTP localhost hostname, and database username `keycloak`; it rejects arbitrary
argument overrides. Set public hostname through `KC_HOSTNAME`, not argv.
Port 9000 is management health inside the container and is never published. The
native Bash/timeout health probe checks that actual container's management listener.

`crm-web-local` and `demo-module-local` are distinct public browser clients using
Authorization Code with required PKCE S256. Implicit grants, direct password grants
and service accounts are disabled. Redirect placeholders are exactly
`http://localhost:5173/auth/callback` and `http://localhost:5180/auth/callback`;
origins are the corresponding URLs without paths. No wildcard, seeded user, client
secret or signing private key is supplied. The CRM client has
an exact post-logout return `http://localhost:5173/` and a dedicated audience mapper
adding `crm-api` to access tokens, not ID tokens. The demo client remains separate.
Public discovery/JWKS and client metadata do not establish a working application
login or audience validation by CRM.

The public browser hostname differs from container DNS. The API and Keycloak share a
dedicated OIDC network; the API reads discovery and JWKS over
`https://keycloak:8443` while validating the unchanged public issuer. Keycloak's
dynamic backchannel discovery advertises that internal HTTPS JWKS URL. A per-stack
self-signed certificate is stored alongside the secret state in `<state>.tls`;
only Keycloak receives its private key, and the API trusts the certificate through
`SSL_CERT_FILE`. The management health port uses HTTP on the Keycloak container's loopback
interface and is not published. The local profile explicitly uses Development because
the public issuer is loopback HTTP; production requires HTTPS.

The local lifecycle needs `openssl` to provision and validate that certificate.
The certificate and key are private files with an integrity manifest, persist across
warm starts, and are never regenerated automatically after migration. A missing,
modified, or nearly expired TLS state fails closed. The first upgraded `up` creates
TLS state alongside existing version 1 credentials and records stack metadata version
2; it does not rotate credentials or recreate database volumes. Pending migrations can
be retried. For rollback, stop/remove the version 2 service containers, retain the
credential and TLS directories and volumes, restore the original version 1 stack
metadata for that project, then run the old checkout; back up the metadata before an
upgrade. Do not replace the issuer with `http://keycloak:8080` or disable issuer or
transport checks to solve routing.

Startup import creates a missing realm and skips an existing realm. The trusted host
helper `scripts/local/realm.py` checks actual discovery, JWKS and persisted Admin API
metadata, including both clients and their exact redirect/origin and grant settings.
`inspect_realm(public_base_url, AdminSecretFiles(password_file, username='local-admin'),
timeout_seconds)` returns `RealmStatus(status, reason_token, correlation_id)`.
Statuses: `ready` (reason null), `drift` (`LOCAL_REALM_DRIFT`), `unavailable`
(`LOCAL_IDP_NOT_READY`). Every invocation has a fresh correlation ID. It uses only
an explicit HTTP localhost port, disables proxy/redirect following, applies one
finite total deadline across connect, sends, status, headers and body. Its
synchronous socket reader closes every request before returning, including
timeouts; it creates no background worker. It connects to numeric IPv4 loopback
and preserves canonical localhost Host. It never writes tokens, repairs drift,
or logs responses.
Lifecycle callers must validate Docker project/port ownership before calling it.

### Upgrade a persistent local realm

Import runs only for a missing realm. An existing realm does not acquire new client
settings when the JSON changes, and inspection reports `LOCAL_REALM_DRIFT` with a
correlation ID until its actual configuration matches. The operator may inspect the
`crm-web-local` client using the Keycloak admin console, after backing up the realm
configuration and its database: confirm the exact post-logout URI above, and the
client-level `crm-api-audience` mapper (`oidc-audience-mapper`) with custom audience
`crm-api`, access-token claim enabled and ID-token claim disabled. Do not change
`demo-module-local` or replace issuer/audience checks. Inspect the status again after
any change; metadata inspection alone does not prove an issued token works.

Repair is explicit and opt-in: an operator may add or update these two settings in
the existing client after recording its prior values, then rerun local inspection.
To roll back, restore the saved prior client settings; the updated inspector will
report drift until its expected settings are restored. Recreating a realm or volume
deletes stored identity and configuration data; it is not an automatic migration
and requires separate authorization before deleting any real data. Tests use only
disposable, test-owned resources.

The bootstrap admin password is read from its owner-only 0600 file, and only sent
in an in-memory form body to master realm `admin-cli` to manage metadata. It is not
an end-user login test. Do not pass passwords/tokens in command arguments or save
the management token in the manifest. Only the trusted local helper receives this
administrator credential; API and external modules never receive it.

Secret files are generated and retained by `ensure_secret_state`:

| Consumer | File and environment contract |
| --- | --- |
| idp-db administrator | `idp_db_admin_password`, `POSTGRES_PASSWORD_FILE=/run/secrets/idp_db_admin_password` |
| idp-db initializer | `idp_db_password`, `LOCAL_IDP_DB_PASSWORD_FILE=/run/secrets/idp_db_password`; `LOCAL_DATABASE_SERVICE=idp-db` |
| Keycloak database login | `idp_db_password`, `KC_DB_PASSWORD_FILE=/run/secrets/idp_db_password`; `KC_DB_USERNAME=keycloak`, `KC_DB_URL=jdbc:postgresql://idp-db:5432/keycloak` |
| Keycloak/bootstrap inspector | `idp_admin_password`, `KC_BOOTSTRAP_ADMIN_PASSWORD_FILE=/run/secrets/idp_admin_password`; public `KC_BOOTSTRAP_ADMIN_USERNAME=local-admin` |

Use `POSTGRES_USER=postgres` and `POSTGRES_DB=postgres` (the official defaults)
for idp-db; the initializer creates `keycloak` separately. Do not precreate that
database through `POSTGRES_DB`.

Keycloak mounts only its database and bootstrap-admin password files. Its `keycloak`
PostgreSQL LOGIN owns its dedicated database/schema and has no superuser, createdb,
createrole, replication or bypassrls privileges. idp-db has its own network/storage
and mounts no CRM credential. PostgreSQL administrator credentials never enter
Keycloak. Initialization SQL suppresses statement/error SQL logging; passwords are
read into private process memory, not command arguments. Existing databases are
never recreated by warm startup. Warm lifecycle checks actual credentials, health
and persisted realm/client metadata; it does not audit every administrator-made
IdP database privilege or ownership change. CRM controlled bootstrap has a separate
explicit warm credential/ACL/ownership contract. See the [final scope rulings](../superpowers/reviews/2026-11-10-wave-0-plan-2-final-review.md).

Adding the separate IdP database administrator password changes the exact local
state file set. State generated by the earlier unshipped Task 4 intermediate
implementation fails `LOCAL_SECRET_STATE_MISMATCH`; it is never silently rotated
or upgraded. Reconcile state and its database credentials explicitly. Keep data
and secret backups together; startup import is not a backup or migration tool.

For realm drift, inspect Admin API settings through a trusted administrator,
compare the documented contract, and reconcile intentionally. A normal up/down
must preserve the realm, users and database. Destructive disposable-local reset
requires an explicit operator choice outside normal lifecycle commands.

The clean-source run and named persisted-realm/drift tests are mapped under
W0P2-10 in [Plan 2 evidence](../engineering/06-WAVE-0-PLAN-2-VERIFICATION.md).
Those historical checks verify metadata and management inspection only. A separate disposable
real-provider browser test now covers end-user login and CRM audience acceptance; production
authentication remains OPEN.

## Production design checklist (guidance, not deployment evidence)

- Use supported pinned Keycloak releases with HTTPS and a fixed public hostname.
  Terminate TLS on a trusted ingress or Keycloak, restrict forwarding headers to
  trusted proxies, strip spoofed inbound headers, and keep management/admin
  endpoints on restricted networks. Never use this HTTP `start-dev` profile.
- Configure each resource server's exact issuer and intended API audience. Validate
  signature, issuer, audience, expiry and accepted algorithms. Give browser apps
  separate public clients, required S256, exact HTTPS redirects/origins and only
  necessary scopes. Decide audience mappers explicitly; the local CRM client has one
  for the local API, and the API enforces its configured audience.
- Keep discovery/JWKS reachable from browsers and resource servers. Cache keys
  within a defined lifetime, refresh for unknown key IDs, overlap signing keys
  for outstanding tokens, and test rollover and emergency retirement. Private
  keys stay in the provider/KMS and never enter realm source or application logs.
- Require administrator MFA, restricted admin roles, auditable changes, secure
  account recovery and break-glass procedures. Decide end-user MFA policy based
  on risk, and test enrollment/recovery before rollout.
- Set access/refresh token lifetimes, idle/absolute sessions, refresh rotation,
  logout redirect allowlists and front/backchannel logout deliberately. Verify
  logout and revocation behavior across both applications; avoid assuming a
  browser redirect revokes every token immediately.
- Use dedicated non-superuser database identities, isolated storage/networks,
  secret manager delivery, controlled rotation, encrypted transport/storage,
  tested backups and restore drills. Coordinate database/provider upgrades and
  schema rollback compatibility before changing pinned versions.
- A managed OIDC provider is a valid alternative. Recheck issuer/audience,
  client/redirect/PKCE policies, key rotation, session/logout, administrator MFA,
  tenancy and data residency rather than importing Keycloak-specific assumptions.

No production instance was deployed or production authentication verified by
these local checks. Remote DemoModule E2E, duplicate consumption, tenant isolation,
outage/recovery and restore acceptance remain separate gates.

## Wave 1 API identity and tenant foundation

The API requires `Oidc:Issuer`, `Oidc:Audience` and `Oidc:MetadataAddress` at
startup. The issuer must exactly match the token `iss`, including its spelling and
trailing slash; the discovery address is a separate reachable backchannel and does
not replace the public issuer. The audience is the intended resource-server value
(for local Compose, `crm-api`). Missing or invalid configuration stops startup
with `OIDC_CONFIGURATION_INVALID` and a correlation ID. HTTPS is required for
issuer and metadata; HTTP is allowed only for loopback when explicitly enabled by
`Oidc:AllowHttpLoopback` in Development or Testing. Metadata and JWKS retrieval
do not follow redirects. Tokens require a valid RS256 signature, issuer, audience,
expiration, valid lifetime and nonempty issuer/subject, with zero clock skew.
Bearer tokens and subject values must not be logged. Provider keys are fetched
through discovery/JWKS; the configured library defaults observed in integration
tests are a 12-hour automatic refresh and a 5-minute minimum requested refresh.
Overlap keys for outstanding tokens and plan emergency retirement separately;
these intervals do not promise immediate removal of an old cached key.

The local CRM browser client configures the `crm-api` audience on its access token;
metadata inspection alone is not token acceptance proof. The disposable real-provider
browser test separately verifies an issued token against the API. See the
[web setup and limitations](../../apps/crm-web/README.md#browser-sign-in-and-tenant-selection).
Never commit provider keys, API credentials or tokens. Deliver production credentials
through an appropriate secret mechanism and restrict use of the application DB
credential to the API.

Migration `20261116000000_TenantFoundation` creates tenants, memberships, RLS and
restricted lookup functions. Apply migrations with the designated migrator role;
use a backup and an intentional deployment/rollback plan before changing a durable
database. The migration down path drops the lookup functions and new tables, and
therefore removes tenant and membership data; restore from backup as appropriate.
Rollback was exercised on a disposable PostgreSQL database and is not a restore drill.
The application role cannot directly read tenant rows or mutate memberships; RLS
also constrains membership SELECT by transaction-local tenant context. The narrow
`SECURITY DEFINER` bootstrap functions trust the issuer/subject passed by the API:
a holder able to execute arbitrary SQL with the app credential can supply another
principal, so neither RLS nor the functions protect against that credential being
compromised. The foundation lookup functions disclose only constrained results;
tenant transaction authorization takes row locks through completion. The later
tenant lifecycle migration also grants the API role narrowly scoped write functions,
so this credential must be treated as trusted server authority, not as proof of the
identity supplied to SQL.

## Controlled tenant provisioning

`POST /api/v1/tenants` requires a valid CRM-audience access token whose single,
well-formed, space-separated OAuth `scope` claim contains the exact case-sensitive
value `platform.tenants.create`. A tenant header, membership or role claim does not
grant this scope. The API generates the tenant ID and uses the validated token's
issuer and subject as the initial active owner. Tenant, owner and redacted audit
entry are written in one database transaction; an audit error rolls the create back.

The IdP operator is responsible for restricting issuance of that scope to approved
platform operators and appropriate clients. This is a trust assumption: the API
validates the token and exact scope but cannot independently verify the IdP's
assignment process. For initial bootstrap, an authorized IdP administrator must
establish and audit a controlled operator identity and client, configure the CRM
audience and narrowly assign this scope through the provider's managed process,
then verify both allowed issuance and denial to unapproved identities in the target
environment. The approved operator can then authenticate and invoke the endpoint;
there is no unauthenticated bootstrap API. Revoke the assignment when no longer
needed, and protect and review provider administration and issuance logs. Do not
put access tokens or credentials in commands, source, tickets or logs. The local
realm metadata does not establish this issuance policy or an end-user login.

The API database credential can execute lifecycle security-definer functions with
caller-selected arguments. PostgreSQL restrictions and RLS constrain access but do
not authenticate a claimed issuer or subject supplied by a holder of that
credential. Keep the credential exclusively with the trusted API, restrict SQL
access, rotate on compromise and investigate any potentially affected tenant.
Membership role alone is not evidence distinguishing a staff identity from a
service identity; membership management also requires the controlled verified
staff binding described below.

### Verified staff bindings for membership management

A valid access token and active owner/admin membership do not themselves prove
that a principal is staff. `team.manage` additionally requires an active
`platform.staff_bindings` record for the exact validated `(issuer, sub)`.
Existing memberships and tenant provisioning do not create bindings; until an
operator verifies and registers the principal, membership management fails
closed. Service principals must never be registered as staff. An issuer must
maintain stable subject identifiers and the operator must verify both their
issuer and subject against an authoritative staff identity source; email or
token role claims alone are not sufficient. The database owner/migration
credential is trusted to perform this verification and must be protected and
accessible only through the controlled operator process. The application role
cannot read or write this table, or read or alter the audit table.

Bootstrap is an offline, restricted database operation, not an unauthenticated
API. Under an approved change ticket, a designated operator independently
verifies the human's exact issuer and subject and records an opaque, bounded
verification ticket reference (never a token, password or sensitive evidence).
Using a parameterized SQL client and the privileged migration-owner credential,
insert `(issuer, subject, 'active', verification_reference)` into
`platform.staff_bindings` in a transaction; commit only after checking the
record. Do not bulk-copy existing memberships or infer staff from ownership.
The insert automatically writes `audit.staff_binding_changes` in the same
transaction; failure of the audit insert aborts the change. Review the audit by
privileged access under the organization's retention policy, along with the
verification evidence in its protected system of record.

For departure, compromised identity, or verification withdrawal, update that
exact row to `status='revoked'` with an updated verification ticket reference
in a transaction; audit is written automatically. Revocation waits for already
authorized membership transactions holding the binding's share lock. Requests
starting after revocation commits are denied. Reactivation requires a new
independent verification and ticket reference before changing the existing row
to `active`; do not delete bindings or audit records. Use parameterized values
and do not log tokens or verification evidence. The migration refuses downgrade
when bindings or their audit exist; retain backups and prefer a forward fix.
An operator with database-owner powers can bypass SQL controls: restrict and
audit that credential externally, including schema and trigger changes.

## Invitation acceptance and recovery

Configure the CRM audience on the Keycloak client. All implemented Wave 1 tenant, membership, invitation and tenant audit routes require exactly one signed payload member `typ` with string value `"Bearer"`. This discriminator distinguishes a Keycloak access token from an ID token with the same audience only when the issuer reserves it exclusively for access tokens. The expanded requirement is a behavioral break for previously accepted access tokens without this profile; inventory and migrate every issuer and consumer before rollout as described in [ADR-019](../architecture/09-ADR-019-ACCESS-TOKEN-PROFILE.md). Invitation acceptance additionally requires the email scope. After the provider verifies ownership of the address, issue the following signed access-token payload members exactly once.

| Member | Required JSON value |
| --- | --- |
| `typ` | String `"Bearer"` |
| `email` | String containing the recipient's address |
| `email_verified` | Boolean `true` |

Validate the mapping with a dedicated test identity. Keep tokens and signed payloads out of logs, tickets and CI output. The API validates the bearer first and uses its issuer and subject to identify the caller. Acceptance also requires an active independently verified staff binding for that identity. An email match never establishes a staff binding or links a CRM contact.

Creation and acceptance use the same ASCII address comparison. Preserve the case of the dot-atom local part (1–64 characters). Fold the domain to lowercase (1–253 characters, labels of 1–63 letters, digits or hyphens, each starting and ending with a letter or digit). The complete address is limited to 320 characters. Supply the address exactly as verified, without whitespace, quoted local parts, display names or Unicode. Plus tags and dots are significant.

Deliver the single-use token manually to the verified recipient over a confidential channel. It expires after 24 hours. For creation with an uncertain outcome, check the tenant list and audit, revoke the matching pending invitation and issue a fresh one. After an ambiguous acceptance, inspect membership and tenant audit before taking further action. A durable acceptance consumes the invitation and records membership and audit together. See [TLS and redaction requirements](01-DEPLOYMENT-OBSERVABILITY.md).

Apply migration `20261126000000_Invitations` as the migration owner. The runtime login uses narrowly granted functions for invitation operations and tenant audit reads. Keep owner credentials outside the application. Earlier revisions of this unreleased migration assumed disposable development databases. Recreate such a disposable database from the current migration chain. For a database holding real data, take a verified backup and plan a forward migration. Downgrade locks the invitation and audit tables and refuses to proceed when invitations or invitation audit entries exist.
