# Wave 1 Identity and Tenancy Delivery Plan

> Execution: one coding agent, test-first, sequential reviewable slices. This is a delivery map; each security-sensitive slice needs a detailed executable plan before implementation.

**Goal:** Deliver OIDC, verified tenant membership, RBAC, RLS and audit with independently tested tenant isolation.

**Architecture:** The IdP validates identity via standard OIDC/JWT libraries; CRM resolves verified issuer and subject to active membership before establishing transaction-local tenant context. PostgreSQL RLS supplements tenant-scoped queries and application authorization.

**Tech Stack:** .NET 10, ASP.NET Core, PostgreSQL/EF Core, Testcontainers, React and the existing pinned toolchain.

**Spec:** [Wave 1 design](../specs/2026-11-15-wave-1-identity-tenancy-design.md); [P0 access decisions](../../architecture/07-P0-ACCESS-AND-STATE.md).

## Global constraints

No trust in tenant headers or request body; stable issuer/subject, no contact auto-link; no RLS bypass for app role; transaction-local context; public contracts remain compatible; no credentials logged; every closed error has reason token and correlation ID; RED/GREEN with real PostgreSQL integration tests; locks remain pinned.

## Review focus

1. Missing or mismatched token claims: reject before tenant lookup; test issuer, audience, signature, algorithm, expiry, nbf and rotation.
2. Membership removed while a request is in progress: define transaction/locking and prove revocation semantics under concurrency; no stale cache authorization.
3. Absent or reused tenant context: RLS denies missing context, connection pooling cannot leak prior tenant; test commit, rollback and exception paths.
4. Bootstrap and privileged SQL: application cannot enumerate arbitrary principals or write through elevated routines; pin search_path and narrow grants; test privilege abuse with real app role.
5. Concurrent membership/invite operations: enforce single-use and last-owner invariants atomically; test races in PostgreSQL.

## Slices and gates

1. **Identity and tenant foundation.** Map existing OpenAPI tenant list schemas and error responses; specify exact SQL privilege boundary and issuer/backchannel configuration. Test token rejection and JWKS rotation, identity lookup, active membership and tenant header validation. Implement migration, narrow bootstrap lookup, transaction-scoped context and RLS with PostgreSQL Testcontainers tests. Exercise missing context, cross-tenant access and pooled connection reset. Expose only contract-compatible behavior. Produce a detailed plan with file paths, test cases, and security review before coding this slice.
2. **Tenant and membership lifecycle.** Implement contract operations, provisioner authorization, scope matrix, version/ETag concurrency and last-owner protection. Test forged roles, cross-tenant access, concurrent updates and rollback.
3. **Invites and audit.** Implement hash-only single-use invite tokens, verified email, expiration/revocation and atomic accept; persist audit with state changes. Race accept/revoke and confirm secrets absent from output and logs.
4. **Browser OIDC.** Implement Authorization Code + PKCE with pinned standard client, tenant selection and logout. Test callback validation and real IdP browser flow; never treat test issuer metadata as login evidence.
5. **Wave exit.** Run full suites and independent A/B sessions with negative API and SQL/RLS tests, token security suite and documented evidence; update acceptance matrix and handoff. P0 and later acceptance remain open.

Each slice requires its own RED/GREEN evidence, focused regression, review, documentation and small commit. Do not report a slice as passing unless its commands actually pass.
