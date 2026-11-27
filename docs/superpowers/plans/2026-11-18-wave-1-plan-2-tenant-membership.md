# Wave 1 Plan 2 — Tenant and Membership Lifecycle Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` sequentially, with one coding agent. Write and run failing tests first, then implement and verify. Checkbox items record work still to do; no task is complete merely because it appears here.

**Goal:** Implement controlled tenant provisioning and tenant-scoped membership read/update with RBAC, concurrency and transactional audit.

**Architecture:** The API validates the existing OIDC principal, enforces platform provisioning scope or active tenant membership and `team.manage`, then performs database work in the same transaction as audit. PostgreSQL constraints, RLS and restricted security-definer entry points enforce the storage boundary; the existing API credential remains a trusted server credential and cannot authenticate an arbitrary SQL caller's claimed identity.

**Tech stack:** .NET 10 / ASP.NET Core, EF Core migrations, PostgreSQL/Npgsql, xUnit, Testcontainers, existing locked dependencies.

**Spec:** [Wave 1 design](../specs/2026-11-15-wave-1-identity-tenancy-design.md), [access decisions](../../architecture/07-P0-ACCESS-AND-STATE.md), [OpenAPI](../../contracts/crm-api.openapi.yaml), [delivery map](2026-11-15-wave-1-delivery.md).

## Baseline and scope

- Starting point: local `main` `9f4214b`, 13 commits ahead of `origin/main` at planning time. Plan 1 implements bearer auth, restricted tenant lookup, `VerifiedTenantTransaction.BeginAsync`, and tenant list. Prior full integration evidence is 120/120 on `53a7cd6`; hosted CI was not run. Recheck Git and handoff at execution time; do not push or deploy based on this plan.
- Implement `POST /api/v1/tenants`, `GET /api/v1/memberships`, `GET /api/v1/memberships/{id}`, `PATCH /api/v1/memberships/{id}`. Invites, browser login, audit read API and owner transfer belong to later slices. No contract/schema breaking change is authorized.
- RBAC grants `team.manage` to active owner/admin only; agent/operator and integration-service have none. Token role claims, tenant headers and body fields never grant tenant permissions. Only verified issuer/sub from token plus an active DB membership can establish tenant authorization.
- Platform tenant creation requires exact `platform.tenants.create` value in the space-separated OAuth `scope` claim of an already validated CRM-audience access token. The IdP operator must restrict issuance of this scope to platform operators; no role or tenant membership implies it. Reject malformed or duplicate `scope` claims; case-sensitive exact match, never substring match. Record issuer-side provisioning and its trust assumption in operations docs; operator bootstrap is an explicit controlled procedure, never an unauthenticated API.
- For each tenant mutation, audit must commit in the same DB transaction; an audit failure rolls back the mutation. `issuer` and `sub` are the stable actor identity; no raw tokens, secrets or unbounded caller payload in audit/logs. The user-facing audit read endpoint remains out of scope.
- Use real PostgreSQL Testcontainers, locked dependencies, fail-closed Problem responses with reason token and correlation ID. Preserve original existing files, untracked handoffs and planning docs. No completion claim for Wave 1 or P0.

## Review focus

1. Forged token roles, substring scopes, inactive membership, cross-tenant ID/header, or platform operator without valid scope cannot authorize an operation (Tasks 2–4).
2. Two concurrent updates or revocations, self-revoke, and active request vs revocation cannot remove the last active owner or allow stale authorization; deadlock/serialization errors roll back safely (Tasks 2 and 4).
3. Audit failure, duplicate creates and process/DB interruption cannot leave partial tenant, owner or membership mutation (Tasks 1, 3, 4).
4. Existing database stores `inactive`, while contract exposes `revoked`. The migration and rolling-deployment/rollback procedure must not let different versions misinterpret state (Task 1).
5. Missing, weak, wildcard, malformed or stale `If-Match`; invalid cursor/limit; malformed, empty or unknown PATCH input cannot mutate data or leak cross-tenant existence (Tasks 3–4).

## Interfaces and transaction decisions

- `platform.memberships.status` remains internally `active`/`inactive` for this slice; API maps `inactive` ↔ `revoked` explicitly in both directions. No data rewrite or wire schema change is needed. Test existing Plan 1 lookup still rejects inactive. Any future database rename needs its own expand/contract migration and rollback plan.
- Membership cursor is an opaque base64url encoding of version, selected tenant UUID and last membership UUID, strictly parsed and bounded at 2048 characters. Server checks tenant equality, and queries use `tenant_id` and ordered `id` as keyset; cursor never grants access. Invalid or cross-tenant cursor → 400 `CURSOR_INVALID`. A valid deleted/revoked anchor remains a position, not proof of authorization. Limit defaults 20 and is 1–100; duplicate/invalid parameters → 400 `REQUEST_INVALID`.
- Strong `ETag` is `"v{version}"`; `If-Match` absent → 428 `PRECONDITION_REQUIRED`; wildcard, weak, list, malformed, overflow or multiple headers → 400 `PRECONDITION_INVALID`; stale → 412 `VERSION_CONFLICT`. Target hidden outside selected tenant → 404. Unknown PATCH fields, invalid enum or empty object → 400 `REQUEST_INVALID`. An unchanged valid patch returns current representation/ETag and creates no mutation audit or version increment, after checking version and permissions.
- PATCH cannot grant owner, change any existing owner's role, or revoke the last active owner. Owner reactivation is allowed only by setting status `active` on an existing revoked owner; the role cannot change. All role/status mutations require active actor with `team.manage`, checked inside transaction. Self-revocation may succeed only if another active owner remains; subsequent requests are denied.
- Mutations serialize per tenant using `pg_advisory_xact_lock` with a deterministic application namespace and stable tenant UUID-derived key; hash collisions only serialize unrelated tenants. Acquire it **before** calling existing `lock_active_membership`, then lock target and count active owners in that transaction. All writers to owner/role/status, including later invite/administrative paths, must follow this protocol. Existing reader `FOR SHARE` locks may cause waiting or PostgreSQL deadlocks: rollback the whole transaction on SQLSTATE `40P01`/`40001`, retry at most twice from authorization with a bounded delay, then return 503 `IDENTITY_STORE_UNAVAILABLE`; never retry after an ambiguous commit. Test cross-updates and lock waits, not just sequential writes.
- Preserve existing `VerifiedTenantTransaction.BeginAsync` for reads. Add `BeginMembershipMutationAsync` which acquires the tenant advisory lock first, then uses the same tenant/membership verification and transaction-local context on the **same transaction**; refactor existing method into a shared helper if needed. Never call the existing method after already opening a separate transaction.

## Task 1 — Storage, atomic audit and privilege boundary

**Files:** create `src/Platform.Infrastructure/Persistence/Migrations/<generated>_TenantLifecycle.cs` plus designer/snapshot; modify `src/Platform.Infrastructure/Persistence/PlatformDbContext.cs`, `DatabaseRoleGrants.cs`; create `src/Platform.Infrastructure/Identity/LifecycleStore.cs`; test `tests/Integration/TenantLifecycleSchemaTests.cs` and existing migration/privilege tests.

**Produces:** restricted SQL functions `platform.create_tenant_with_owner(uuid,text,text,text,text)` and `platform.update_membership(uuid,uuid,bigint,text,text,text,text,text)`; the latter receives tenant, target, expected version, nullable requested role/status, actor issuer/sub and correlation ID, returning a typed outcome and row. Both use `SECURITY DEFINER`, fixed `search_path=pg_catalog`, schema-qualified names, no dynamic SQL, revoke PUBLIC and grant only exact signatures to the validated app role. Include a tenant-bound append-only `audit.entries` table with UUID id, tenant id, actor issuer/sub, action, target id, bounded redacted summary, correlation id and UTC timestamp; app role has no direct update/delete/truncate or unrestricted read. Do not expose a general arbitrary audit append function.

- [ ] RED: add Testcontainers tests proving tenant + initial owner + audit are atomic; forcing audit insert failure rolls all three back; audit update/delete and PUBLIC function invocation denied; cross-tenant mutation through app role rejected; `inactive` membership round-trips as `revoked` at API boundary and remains denied by Plan 1 lookup. Run `dotnet test tests/Integration/Integration.csproj --filter FullyQualifiedName~TenantLifecycleSchemaTests`; confirm expected failure.
- [ ] Create migration and precise constraints/indexes; extend privilege validation for new audit schema, ownership, column privileges and function grants without weakening existing checks. Function for create validates exact field bounds and inserts tenant, active owner `(verified issuer,subject)` version 1, and redacted audit atomically. Because the app DB credential can invoke definer functions with chosen arguments, API scope/auth is an explicit trusted boundary; do not claim SQL alone authenticates the actor.
- [ ] Add migration upgrade from existing Plan 1 schema with existing inactive rows, and rollback tests. Migration rollback must not silently delete populated audit data: document and test forward-fix/backup procedure; do not automate destructive rollback on real data. Verify absence of secret/payload in audit and RLS/privilege invariants.
- [ ] Rerun focused tests plus `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~MigrationTests|FullyQualifiedName~RolePrivilegeTests|FullyQualifiedName~TenantSchemaTests|FullyQualifiedName~TenantLifecycleSchemaTests'`; record actual pass/fail/skip. Commit scoped changes `feat(tenancy): add lifecycle storage and audit`.

## Task 2 — Verified authorization and serialization

**Files:** create `src/Platform.Api/Identity/TenantAuthorization.cs`, `src/Platform.Infrastructure/Identity/MembershipMutationTransaction.cs`; modify `src/Platform.Infrastructure/Identity/VerifiedTenantTransaction.cs`; test `tests/Integration/TenantAuthorizationTests.cs`, `tests/Integration/TenantIsolationTests.cs`.

**Produces:** `TenantAuthorization.HasTeamManage(string role)` and `HasProvisioningScope(ClaimsPrincipal principal)` with exact policies above; `MembershipMutationTransaction.BeginAsync(NpgsqlConnection connection,string issuer,string subject,Guid tenantId,CancellationToken ct)` returns the verified transaction after taking tenant lock, and a tenant-scoped role lookup returns the active actor role within it. A read authorization path obtains role on the same connection and verified transaction.

- [ ] RED: tests cover all four staff roles, forged JWT roles, inactive membership, missing/duplicate/invalid tenant header, wrong tenant and exact scope cases (`platform.tenants.create`, substring, case change, duplicate claims, missing scope); verify no tenant context on denial and no leaked data. Include simultaneous actor-revoke and operation: revocation waits for already authorized transaction; subsequent operation is denied.
- [ ] Implement transaction helper and authorization. Tenant header must occur exactly once and be canonical UUID; missing/malformed → 400 `TENANT_CONTEXT_INVALID`, no membership → 403 `TENANT_ACCESS_DENIED`, wrong role → 403 `INSUFFICIENT_SCOPE`. Never trust a role claim for `team.manage`. For service identities deny unless a separately implemented and verified installation binding exists; do not infer staff status from token type.
- [ ] GREEN: run `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~TenantAuthorizationTests|FullyQualifiedName~TenantIsolationTests'`; include two concurrent mutation transactions on same tenant, different tenants and cancellation; demonstrate no stale context after pool reuse. Commit `feat(tenancy): authorize tenant membership operations`.

## Task 3 — Tenant provisioning

**Files:** modify `src/Platform.Api/Identity/TenantEndpoints.cs`, `src/Platform.Api/Operations/OperationalErrors.cs`; create `tests/Integration/TenantProvisioningTests.cs`; modify `tests/Integration/Fixtures/FakeOidcIssuer.cs` only to issue test scope claims.

**Consumes:** Task 1 `create_tenant_with_owner`; Task 2 `HasProvisioningScope`. **Produces:** contract-compatible `POST /api/v1/tenants` returning 201 `Tenant`, with initial active owner based solely on validated token `iss`/`sub` and transactional audit.

- [ ] RED: unsigned/invalid token → 401; valid token without exact scope or with forged role/tenant → 403; valid provisioner → 201, active owner version 1, audit and visibility in existing list; invalid/unknown body fields, empty or >200-char name → 400; injected DB/audit failure leaves neither tenant nor owner. Use generated fixture identities only, no credentials in output.
- [ ] Implement strict request validation and parameterized call. Generate UUID server-side; subject/issuer never taken from request body. Error responses use existing Problem shape and correlation ID; DB failure → 503 `IDENTITY_STORE_UNAVAILABLE` after rollback. No tenant header required for this global operation.
- [ ] GREEN: run `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~TenantProvisioningTests|FullyQualifiedName~TenantEndpointsTests'`; validate response against existing OpenAPI consumers. Commit `feat(tenancy): provision tenant with initial owner`.

## Task 4 — Membership reads and versioned updates

**Files:** create `src/Platform.Api/Identity/MembershipEndpoints.cs`, `MembershipCursor.cs`, `MembershipPreconditions.cs`, `tests/Integration/MembershipEndpointsTests.cs`; modify `src/Platform.Api/Program.cs`, `LifecycleStore.cs` and SQL function as needed.

**Produces:** list/get/PATCH endpoints in scope. List/get use verified tenant transaction and `team.manage`; PATCH uses mutation transaction, same-transaction role check, row/version predicate and audit. Membership identity is immutable. List returns exact contract fields and `nextCursor`; GET/PATCH return strong ETag.

- [ ] RED read tests: owner/admin read only their selected tenant; agent/operator denied even with forged claims; cross-tenant UUID returns 404; list default/limits and opaque cursor behave per interfaces; malformed/duplicate headers and cursor fail with Problem/correlation ID. Response `inactive` maps to `revoked`.
- [ ] Implement reads and run `dotnet test tests/Integration/Integration.csproj --filter FullyQualifiedName~MembershipEndpointsTests`; read tests GREEN while PATCH tests remain separately filtered or not yet introduced.
- [ ] RED PATCH tests: 428/400/412 cases above, valid role/status changes, mapping `revoked` ↔ `inactive`, no-op version/audit, unknown fields, attempted owner grant/change, last active owner revoke, authorized self-revoke with another owner, target in another tenant and audit failure. Concurrent same ETag yields exactly one mutation and one 412; concurrent owner revocations leave at least one active owner. Cross-update deadlock handling must leave no partial writes/audit.
- [ ] Implement versioned update and audit atomically. Target predicate includes tenant, ID and version; check owner rules under tenant advisory lock; 409 `OWNER_INVARIANT` for owner grant/change/last-owner violation. `OWNER_INVARIANT`, `VERSION_CONFLICT`, and all errors use Problem plus correlation ID. Audit contains actor, target, operation and bounded old/new role/status only.
- [ ] GREEN: run `dotnet test tests/Integration/Integration.csproj --filter 'FullyQualifiedName~MembershipEndpointsTests|FullyQualifiedName~TenantAuthorizationTests|FullyQualifiedName~TenantIsolationTests'`. Add consumer/contract checks for 200/400/403/404/409/412/428, response schemas and ETags without changing public contract. Commit `feat(tenancy): manage tenant memberships`.

## Task 5 — Evidence and exit for this plan

**Files:** `docs/engineering/03-ACCEPTANCE-MATRIX.md`, `docs/operations/05-IDP-CONFIGURATION.md`, `docs/operations/03-RECOVERY-AND-INCIDENT-RUNBOOK.md` if needed; same-day `docs/handoff/` entry.

- [ ] Run `dotnet restore CrmPlatform.sln --locked-mode`, `dotnet build CrmPlatform.sln -c Release --no-restore`, `dotnet test tests/Integration/Integration.csproj -c Release --no-restore`; run contract and existing local Wave 0 gates using their documented prepared environment. Record counts, failures and skips; investigate the previously intermittent Compose parallel-start timeout if it recurs, never describe a rerun as a root-cause fix.
- [ ] Document scope issuance, operator bootstrap, SQL trust limits, migration and audit retention/rollback, concurrent revoke semantics and tested evidence. Run `git diff --check` and relevant relative-link checks. Update acceptance matrix only for tests actually run. Commit `docs: record tenant lifecycle verification`.
- [ ] Run final review against P0 invariants and plan. Do not declare Wave 1 complete until browser OIDC, invites and independent tenant A/B sessions and token security suite pass; P0 also requires remote DemoModule, concurrent duplicate, outage/recovery and restore drills.
