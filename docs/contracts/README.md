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

## Route hiện có và spec trước runtime
- **Route đã triển khai (runtime thật):** backend chỉ phục vụ anonymous `GET /health/live` và
  `GET /health/ready`; đây là toàn bộ nội dung runtime của `platform-operations.openapi.yaml`.
- **Spec trước runtime (chưa có endpoint):** `crm-api.openapi.yaml` và toàn bộ business/event/
  automation schema là thiết kế đã ký hiệu; không có route, không có dữ liệu, không có
  authorization runtime tương ứng. Generated SDK từ các spec này là **client contract**, không
  phải bằng chứng endpoint đã tồn tại.
- **Toolchain (Wave 0 Plan 3, local):** `python3 scripts/contracts/generate.py` sinh bốn client
  TS/C#, `python3 scripts/contracts/check.py` kiểm tra read-only, `python3
  scripts/contracts/compatibility.py --base-ref <ref>` chạy breaking/security/schema gate tại
  base Git, và `bash scripts/check-plan3.sh --base-ref <ref>` gom các bước trên. Tất cả chạy
  local, dùng tool đã pin; không publish package, không upload contract, không CI remote.
- **Vẫn OPEN:** runtime security/reliability (tenant isolation, DR/restore), remote DemoModule
  E2E, remote CI và toàn bộ Wave 0/P0 theo `../engineering/03-ACCEPTANCE-MATRIX.md`.

## Chạy kiểm chứng
Python 3.15.2; tạo venv, cài `requirements-contracts.lock`, chạy `python -m unittest discover -s tests/Contract -v`. Windows `.venv/Scripts/python.exe`, Linux/macOS `.venv/bin/python`. Đây là contract tooling, không phải runtime CRM.

Frontend/toolchain của Plan 3: `npm ci` ở root với Node đã pin, `npm run typecheck|lint|test|build -w apps/crm-web`,
`bash tests/ClientConsumers/run.sh` cho consumer đóng gói, và `bash scripts/check-plan3.sh --base-ref <ref>`
là aggregate gate fail-closed. Chi tiết bằng chứng: `../engineering/07-WAVE-0-PLAN-3-VERIFICATION.md`.

## End-to-end example
Remote DemoModule sends `demo.activity.created.v1`, mapped to contact by explicit link, engine matches rule, add tag and invokes `demo.echo.v1`. Retry safe. No CRM Core modifications.
