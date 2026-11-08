# Module / Plugin System — thiết kế mở rộng

## Kỳ vọng
Module mới chạy ở host bất kỳ có thể tự khai báo navigation, contact panel, event trigger, automation action, scopes và health endpoint; CRM không sửa code lõi.

## Manifest contract
- `moduleId` reverse-domain immutable; `version` SemVer, `platformApiVersion` compatible range, `manifestSchemaVersion`.
- `displayName`, `homepage`, `baseUrl` + endpoints, `capabilities[]`, permissions[]; each capability has input/output schema URI/version.
- Signature/verification and admin approval required; manifest content is untrusted.
- Store immutable installation snapshot, checksum, status, capabilities, allowed origins, secret ref, last health.
- Lifecycle: discovered → validated → pending_approval → active → degraded → disabled → uninstalled.

## Extension points P0
1. `navigation`: external URL + SSO link.
2. `contact.panel`: schema-described read-only panel resolved via connector API, authorization and caching.
3. `event.trigger`: event type/version + JSON schema.
4. `automation.action`: action id, version, input schema, required scopes and remote HTTP execution.
5. `permissions`: declared immutable scope IDs.
6. `health`: status endpoint, last successful delivery and lag.

## Extension points later
Custom workflow node UI, reports, write-capable contact panel, data connectors, module marketplace, on-prem agent, custom UI iframe/MFEs.

## Install sequence
1. Admin enters manifest URL or uploads signed package metadata.
2. Platform retrieves with URL+DNS+redirect protections, validates JSON/schema/version/signature.
3. Admin reviews scopes/egress endpoints and approves.
4. Platform registers tenant installation + obtains credentials securely.
5. Validate remote challenge/health and publish registry snapshot.
6. UI navigation and builder read registry; no need redeploy.

## Upgrade/deactivate
- Compatible manifest update: validate diff, approval for new scopes, atomic version switch.
- Breaking change: side-by-side capability `v1`, `v2`, migration and rollout window.
- Disable: revoke tokens/secrets and pause future automation actions; preserve run history. No orphan runtime executions.
- Uninstall: mark installation tombstone, schedule retention cleanup; keep immutable audit records.

## Contract acceptance
Add `demo.activity.created.v1` plus `demo.echo.v1` remote action without changes to CRM application code; demonstrate full cycle including network interruption, duplicate delivery, disabling and version mismatch rejection.
