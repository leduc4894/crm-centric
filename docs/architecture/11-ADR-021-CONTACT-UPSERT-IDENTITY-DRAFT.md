# ADR-021 draft — bind contact upsert precondition to resource identity

- Date: 2026-11-27.
- Status: **Approved by the user for implementation on 2026-11-27; rollout and compatibility remain open.**
- Proposed amendment to [ADR-020](10-ADR-020-CONTACT-EMAIL-UPSERT.md), [CRM OpenAPI](../contracts/crm-api.openapi.yaml), and [SDK migration guidance](../operations/07-CONTACT-SDK-MIGRATION.md).

## Problem

Contact ETags are version-only (`"vN"`). A client may read contact A with email E and ETag `"v1"`; A can be soft-deleted or have its email changed, and a different contact B can subsequently acquire E at version 1. A later conditional POST for E with A's ETag matches B's version and can overwrite B. A version match alone does not establish that the current email match is the resource the client read. This does not change the fact that contact identity is independent of authentication and email is not an identity proof.

## Approved decision

Keep the existing strong version ETag and add an **optional at the OpenAPI schema level** single request header `X-Contact-Id` containing the expected contact UUID on `POST /api/v1/contacts`. The header is **mandatory together with `If-Match` for an update**, because a new contact is still created without either header. UUIDs must be canonical lowercase `D` format, one value only. The server obtains tenant solely from verified membership, finds the active email match in that tenant, locks the row and checks **both ID and version atomically** before mutating. The expected ID is a precondition, never a tenant identifier or authorization assertion. Preserve current response ID and strong ETag. PATCH/DELETE already address a contact by ID and retain their existing preconditions.

Precedence after authentication and tenant/staff/permission checks:

| If-Match | X-Contact-Id | Outcome |
| --- | --- | --- |
| malformed, weak, wildcard, or multiple | any | 400 `PRECONDITION_INVALID`; no mutation |
| any otherwise valid/missing | malformed, noncanonical, or multiple | 400 `PRECONDITION_INVALID`; no mutation |
| absent | absent | create 201 if no active email match; otherwise 428 `PRECONDITION_REQUIRED` |
| present and valid | absent | 428 `PRECONDITION_REQUIRED`; no mutation |
| absent | present and valid | 428 `PRECONDITION_REQUIRED`; no mutation |
| both valid | active match has the same ID and ETag | 200 update or no-op; no-op keeps version and emits no journal |
| both valid | no active match, different ID, or stale ETag | 412 `VERSION_CONFLICT`; no mutation |

Apply the missing-header rule consistently even when email is null or no record matches; this avoids silently turning an update attempt into a create. A matching pair does not grant any access and does not reveal a different tenant's data. Preconditions must be checked for no-op updates as well. Persisted contact, audit and outbox remain atomic; error paths create none. Concurrency tests must include the A→B email-reuse scenario, concurrent replacement, normalized variants, and both commit orderings with real PostgreSQL barriers.

A compound ETag containing both contact UUID and version could achieve the same binding without a second header, but changes ETag format for GET, create, PATCH and DELETE, their generated clients and every existing `"vN"` consumer. The proposed identity header confines the compatibility change to conditional POST and retains the user-requested ETag.

## Migration and rollback plan

1. **Before server rollout**, review and approve this ADR, precise OpenAPI diff, SDK/consumer migration and tests. Publish new package versions only through the normal release process. Existing version-only upsert clients cannot safely update; do not treat a syntactically additive header as compatible. Preserve trusted baseline and historical locks. Current compatibility gate failure remains unresolved.
2. For update, consumer obtains the current authorized contact ID and strong ETag, holds both as one snapshot, and sends both headers with the email/body. If ID or ETag cannot be obtained, do not update. On 412, retain the user's input and explicitly refresh/review the current contact before retry; never automatically transfer A's intended update to B. This plan does not assume a lookup endpoint exists in the current slice; any email lookup/UI workflow belongs to later plans and needs its own authorization and tenant isolation.
3. Deploy upgraded clients before enabling the server requirement. During a staged rollout, block unsafe version-only upsert; do not silently fall back. Existing create calls without conditional headers remain valid. Validate consumer owner readiness and actual deployed version; local fixtures do not prove rollout.
4. Roll back clients and server together only if the preceding server's ABA risk is explicitly managed; disable conditional email upsert during rollback or retain a server-side identity guard. Do not fall back to version-only writes. Preserve contact/audit/outbox data; follow existing backup/forward-fix guidance for database migrations. No database migration is inherently required for this header change.

## Required evidence before completion

Contract tests for header shape, conditional requirement, 400/412/428 and version-only consumer rejection; generated SDK and independent old/new consumer tests and migration evidence. Runtime tests must use real PostgreSQL and deliberately orchestrate email reuse: A(v1) read → email released/deleted → B(v1) created with same email → stale A ID/v1 request gets 412 without changing B or creating audit/outbox. Also test matching pair, missing/malformed/multiple headers, null/new email, cross-tenant IDs, no-op and races, and absence of sensitive values in errors/logs. Test local focused slices only; full suite remains Wave 2 exit on GitHub CI. Security remains FAIL/deferred; P0 and schema compatibility remain OPEN.
