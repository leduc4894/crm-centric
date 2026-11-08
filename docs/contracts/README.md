# Contracts — ví dụ tham chiếu, cần mở rộng khi implementation
- `crm-api.openapi.yaml`: OpenAPI 3.1 illustrative endpoints. Production phải generate/check parity từ implementation.
- `module-manifest.schema.json`: schema khai báo module tối thiểu.
- `demo-module.manifest.json`: manifest ví dụ khớp schema.
- `demo-event.schema.json`: schema CloudEvents-aligned sự kiện demo.
- `automation-rule.schema.json`: DSL rule P0.

## Versioning
- REST `/api/v1` stable, API version != SDK semantic version.
- Event type suffix `.v1`; treat new incompatible payload as `.v2`.
- Manifest `schemaVersion` and `platformApiVersion` explicitly validated.
- `additionalProperties` policy documented per schema; reject unknown security-critical fields.

## End-to-end example
Remote DemoModule sends `demo.activity.created.v1`, mapped to contact by explicit link, engine matches rule, add tag and invokes `demo.echo.v1`. Retry safe. No CRM Core modifications.
