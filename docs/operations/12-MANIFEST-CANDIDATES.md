# Manifest candidate registry

Wave 3 Plan 1 provides internal components for verifying and recording a platform-wide catalog candidate. Tenant installation, challenge, grants, activation and a production trust configuration loader are future integration work.

## Publisher trust

1. Authenticate the publisher and independently confirm the Ed25519 public key, module ID, key ID and permitted HTTPS origins before creating an active trust entry. Keep the signing key with the publisher. Use explicit, narrowly scoped private networks only where an operator has approved private egress.
2. Inventory declared event types. Assign a platform-wide, case-sensitive prefix including its trailing dot to each module that declares events. For example, `com.example.demo` owns `demo.` only with an explicit assignment. All keys of a module share its assignment. Reject duplicate, ancestor, descendant and reserved `crm.` assignments. Check the entire proposed snapshot before switching revisions. Retain the previous valid snapshot on invalid reload and fail closed at startup without a valid one. [ADR-022](../architecture/12-ADR-022-MODULE-EVENT-NAMESPACE.md)
3. Rotate by recording a new audited revision with a distinct active key ID. Verify a release signed by the new key before retiring the old key. For compromise, mark the affected key revoked immediately in a new revision. Correct an origin or namespace through another audited revision. Record who authorized the change and which module, key ID and revision it affects.
4. Treat the recorded revision as historical evidence. Registration can finish using a proof from before revocation. Before approval or use, the future installation boundary must recheck the current key status, origins and namespace ownership. Reassignment conveys no tenant grants. Preserve historical candidates for investigation.

## Fetch and persistence

Each HTTPS fetch resolves and checks every returned address, then connects directly to a checked numeric IP with TLS hostname and chain verification against the requested host. Redirects and proxies are disabled. TLS chain building uses local trust and cached revocation evidence and tolerates unavailable revocation information. The five-second deadline includes body reads. The internal candidate registration operation accepts a queryless, fragmentless manifest URL. It fetches at most 256 KiB from that URL and at most 4 KiB from its derived sibling at `manifestUrl + ".sig.json"`. Each declared schema fetch is limited to 64 KiB. A signed module base and explicit operator approvals constrain schema fetches.

Ed25519 covers the exact manifest response bytes. The validator hash-checks the declared documents before resolving references across their resources in memory. Unlisted references, ambiguous resource IDs, cycles and paths deeper than eight fail validation. Registration submits the signed manifest, descriptor evidence and all schema snapshots atomically. The application role can execute the registration function and lacks direct table write privileges. A concurrent identical latest release reuses its candidate. Changed bytes at the same version conflict, and replay of an older version is a downgrade.

## Migration and recovery

Apply `20261128000000_ManifestRegistry` then `20261128010000_ManifestRegistryIsolation` as migration owner. Keep writers stopped until both migrations are present. Use READ COMMITTED for registration, including caller-owned transactions and role defaults. The per-module transaction lock then serializes version checks with a fresh statement snapshot. Other isolation levels return `UnsupportedIsolation`.

For application rollback, stop candidate writers and retain both migrations and their historical data. The shipped migrator CLI applies pending migrations forward to the latest version. Tests exercise empty-registry downgrade through EF Core using IMigrator.MigrateAsync("20261127041000_AtomicContactNote"). This requires a separate, controlled migration-owner maintenance path in READ COMMITTED. Keep writers stopped while it reverses the isolation migration and then the registry migration. The intermediate function lacks the isolation guard. The registry downgrade takes exclusive locks and refuses a populated registry with `manifest_registry_contains_data`; stale-snapshot isolation fails with `manifest_registry_rollback_requires_read_committed`. Preserve rows and roll forward when a guard fires.

## Diagnosing failures

Pass the correlation ID to `ManifestCandidateRegistration.RegisterAsync`. On failure, `CandidateRegistrationException` carries the failing stage, its typed reason and the supplied correlation ID. Record reason, stage, revision and correlation ID without response bodies, URL queries or credentials.

| Reason | Operator action |
| --- | --- |
| InvalidTarget, OriginDenied, AddressDenied | Check target syntax, approved origins, DNS answers and approved network ranges. |
| HttpStatus, TooLarge, Timeout, Transport | Check publisher availability, TLS and response sizes. Retry transient failures after correcting the cause. |
| InvalidManifest, InvalidSidecar, InvalidSignature, UntrustedKey | Verify the exact release bytes, detached signature, unique active key and revision with the publisher. |
| NamespaceConflict, NamespaceDenied, InvalidDescriptor | Check assignments as a complete set, declared event names and descriptors against the contract. |
| InvalidSchema, SchemaHash, SchemaSize, InvalidSnapshot | Retrieve the signed release again and compare all pinned schema bytes and descriptors. Correct publisher errors with a newly signed release. |
| VersionConflict, Downgrade | Inspect candidate history and publish a higher version for changed content. |
| UnsupportedIsolation, Storage | Check effective transaction isolation, migration state, execute grant and database availability. Retry the same release after recovery. |
