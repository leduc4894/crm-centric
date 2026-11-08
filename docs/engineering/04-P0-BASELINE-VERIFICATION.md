# P0 baseline — change and verification report

Date: 2026-11-08. Scope: documentation/contracts/tooling, không phải runtime CRM MVP. Branch: `fix/p0-contract-gaps`.

## Goal/user story và invariants
Khép các khoảng trống phát hiện khi review blueprint: module tự mô tả capabilities, event identity xuyên ingress/automation, integration protocol đủ chính xác, acceptance có traceability. Giữ API-only external module, tenant authorization ở backend/DB, Contact khác Identity, durable ACK, at-least-once và idempotent effects.

## Public contract/schema diff
- Manifest v1→v2: descriptors/schema pins/scopes/entityType, canonical event.trigger; migration và explicit legacy rejection theo ADR-018.
- OpenAPI 3.1 P0 baseline có 52 operations với typed requests/responses, Problem Details và contact If-Match. Đây là route specifications, chưa có server implementation.
- DSL all/any/exists/typed gt, bounded depth; explicit trigger/action installation binding; typed callback contact reference. P0 external profile 1:1 theo installation/entityType tránh ambiguous action/panel lookup.
- Inbox UUID tham chiếu xuyên execution/timeline, full ingress tuple/hash chống conflicting reuse; pinned rule revision/action identity.

## Tests và verification đã chạy
Environment: Windows, Python 3.15.2; isolated `.venv`, dependencies cài từ full transitive `requirements-contracts.lock`.

| Command/check | Result |
|---|---|
| `.venv/Scripts/python.exe -m unittest discover -s tests/Contract -v` | PASS — 29 tests, schema/sample/negative/API/signature fixtures |
| `.venv/Scripts/python.exe -m pip check` | PASS — no broken requirements |
| `git diff --check` | PASS |
| Independent .NET HMAC-SHA256 over framed fixture bytes | PASS — PowerShell/System.Security.Cryptography agrees with fixture |

RED→GREEN observed: initial 16 tests exposed 17 assertion failures; later protocol/consumer/reviewer cases failed before fixes. Read-only independent review found two important ambiguities (destination installation and typed external identity); both fixed with regression tests and re-reviewed without remaining critical/important findings. Final additional panel query binding has its own RED→GREEN test.

Windows CRLF→LF portability check exposed stale schema hash pins; changed text normalized, pins recalculated over exact LF bytes, `.gitattributes` enforces LF. CI definition uses pinned action SHAs and the same test command; remote GitHub Actions/Linux execution has not run in this session.

## Migration và rollback
No runtime/database deployed, therefore no SQL migration executed. ADR-018 describes pre-release consumer migration, no implicit v1 fallback, future backfill/collision quarantine and rollback of the whole baseline. No merge/push/deploy is part of this change.

## Observability và reason tokens
Common Problem requires reasonToken/correlationId; protocol docs map auth/schema/conflict/retry errors. Access/state and recovery docs specify redaction, audit, paused/revoked replay and uncertain post-restore interval quarantine. Chưa có runtime telemetry để chứng minh.

## Risks, assumptions, follow-ups
- Schemas kiểm tra structure, không enforce runtime RLS/JWT/SSRF/crypto verification/scope grants, DB atomicity, cross-field subject equality, total DSL quota hoặc worker correctness.
- Runtime acceptance, remote DemoModule E2E, 100 concurrent duplicates, tenant isolation, outage/recovery, migrations và restore vẫn OPEN tại `03-ACCEPTANCE-MATRIX.md`.
- Wave 0 còn application scaffold, Compose cold-start, .NET/React build, generated SDK compile, automated breaking diff và security scans. Contract CI không thay release gate.
- Product pilot là giả thuyết vận hành, chưa có customer/market validation. Privacy/retention thực tế cần product/operator xác nhận trước real-data deployment.
