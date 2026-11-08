# Module / Plugin System — thiết kế mở rộng

## Kỳ vọng
Module mới chạy ở host bất kỳ có thể tự khai báo navigation, contact panel, event trigger, automation action, scopes và health endpoint; CRM không sửa code lõi.

## Manifest contract
- `moduleId` reverse-domain immutable; `version` SemVer release-only trong P0, `platformApiVersion` chỉ hỗ trợ `^1.0`, `schemaVersion` baseline `2`.
- `displayName`, `baseUrl`, `capabilities[]`, `permissions[]`; action/panel/event descriptors có endpoint, requiredScopes và schema URI/version/SHA-256. `homepage` không thuộc strict P0 schema. Range khác cần compatibility consumer tests trước khi hỗ trợ.
- Signature/verification and admin approval required; manifest content is untrusted.
- Store immutable installation snapshot, checksum, status, capabilities, allowed origins, secret ref, last health.
- Lifecycle: discovered → validated → pending_approval → active → degraded → disabled → uninstalled.

## Extension points P0
1. `navigation`: external URL + SSO link.
2. `contact.panel`: schema-described read-only panel resolved via connector API, authorization and caching.
3. `event.trigger`: event type/version + JSON schema.
4. `automation.action`: action id, version, input schema, required scopes and remote HTTP execution.
5. `permissions[]`: declared immutable scope IDs (manifest property, không phải capability token).
6. `health`: status endpoint, last successful delivery and lag.

## Extension points later
Custom workflow node UI, reports, write-capable contact panel, data connectors, module marketplace, on-prem agent, custom UI iframe/MFEs.

## Install sequence
1. Admin enters queryless manifest URL; P0 fetch detached signed sidecar theo MODULE-CALLBACKS. Metadata upload chưa có API P0.
2. Platform retrieves with URL+DNS+redirect protections, validates JSON/schema/version/signature.
3. Admin reviews scopes/egress endpoints and approves.
4. Platform registers tenant installation + obtains credentials securely.
5. Validate remote challenge/health and publish registry snapshot.
6. UI navigation và form rule P0 đọc registry; không cần redeploy. Visual builder vẫn P2.

## Upgrade/deactivate
- Compatible manifest update: validate diff, approval for new scopes, atomic version switch.
- Breaking change: side-by-side capability `v1`, `v2`, migration and rollout window.
- Disable: revoke tokens/secrets; block ingress và action claims/sends mới, unsent work chuyển skipped, giữ history. HTTP call đã gửi có thể hoàn tất; ghi nhận outcome. Reactivation cần fresh credentials/challenge và explicit replay cho skipped work.
- Uninstall: mark installation tombstone, schedule retention cleanup; keep immutable audit records.

## Contract acceptance
Add `demo.activity.created.v1` plus `demo.echo.v1` remote action without changes to CRM application code; demonstrate full cycle including network interruption, duplicate delivery, disabling and version mismatch rejection.

Descriptor/callback/publisher semantics: `../contracts/MODULE-CALLBACKS.md`. Breaking baseline và migration: `08-ADR-018-P0-CONTRACT-BASELINE.md`. JSON Schema không thay runtime authorization/SSRF/schema-resolution validation.
