# Contracts — P0 baseline, pre-runtime
- `crm-api.openapi.yaml`: typed P0 OpenAPI 3.1 baseline, contract revision 0.2.0. Chưa có runtime endpoints; cần check implementation parity ở từng wave.
- `module-manifest.schema.json`: strict manifest v2 có action/panel/event descriptors; legacy v1 deliberately rejected theo ADR-018.
- `demo-module.manifest.json`: manifest ví dụ khớp schema.
- `demo-event.schema.json`: schema CloudEvents-aligned sự kiện demo.
- `automation-rule.schema.json`: DSL rule P0.
- `../examples/03-SAMPLE-RULE.json`: complete consumer rule fixture, explicit installation binding và built-in+remote action.
- `cloud-event.schema.json`: envelope; các `demo-echo-*.schema.json`, `demo-panel.schema.json` là pinned DemoModule consumer contracts.
- `WEBHOOK-PROTOCOL.md`, `webhook-signature.fixture.json`: exact-byte framing/retry/binding. Key fixture công khai chỉ dùng test, không provision thật.
- `MODULE-CALLBACKS.md`: publisher trust, semantic validation, callbacks, panel/SSO và SSRF.
- `publisher-signature.schema.json`: detached publisher sidecar; `remote-action-request.schema.json`, `remote-panel-request.schema.json`: typed callback wrappers, không thay pinned business input schema.

## Versioning
- REST `/api/v1` stable, API version != SDK semantic version.
- Event type suffix `.v1`; treat new incompatible payload as `.v2`.
- Manifest `schemaVersion` and `platformApiVersion` explicitly validated.
- `additionalProperties` policy documented per schema; reject unknown security-critical fields.
- Breaking baseline/migration/rollback: `../architecture/08-ADR-018-P0-CONTRACT-BASELINE.md`. API version khác contract revision và SDK SemVer.
- CloudEvent extension metadata được phép nhưng không quyết định tenant/grants. Snapshot pinned schemas; không fetch arbitrary remote refs khi ingest/evaluate.
- JSON Schema không chứng minh DNS/IP/authz/publisher trust, descriptor ID uniqueness/scope subset/node quota hay transactional durability. Runtime gates ở acceptance matrix.

## Chạy kiểm chứng
Python 3.15.2; tạo venv, cài `requirements-contracts.lock`, chạy `python -m unittest discover -s tests/Contract -v`. Windows `.venv/Scripts/python.exe`, Linux/macOS `.venv/bin/python`. Đây là contract tooling, không phải runtime CRM.

Generated SDK compile, automated breaking diff và runtime security/reliability vẫn OPEN theo `../engineering/03-ACCEPTANCE-MATRIX.md`.

## End-to-end example
Remote DemoModule sends `demo.activity.created.v1`, mapped to contact by explicit link, engine matches rule, add tag and invokes `demo.echo.v1`. Retry safe. No CRM Core modifications.
