# ADR-018 — P0 contract baseline và pre-release migration

- Date: 2026-11-08
- Status: Accepted for pre-runtime baseline theo ủy quyền xử lý gaps; runtime acceptance chưa đạt.
- Supersedes: illustrative manifest v1; làm rõ ADR-008/009/010/011/013, giữ architectural boundaries.

## Context
Repo chỉ có blueprint và illustrative contracts, chưa có released server/SDK hay production consumer. Manifest cũ thiếu action/panel; automation dedup thiếu source identity; webhook chưa có framing; PRD trộn durability với tỷ lệ SLO.

## Decision
1. Manifest schemaVersion `2`: canonical capabilities navigation/contact.panel/event.trigger/automation.action/health; permissions là property. Descriptors có endpoint/scopes/schema URI+version+hash. P0 API range chỉ `^1.0`, module SemVer release-only. Reject unknown version/capability/fields, không downgrade âm thầm.
2. Ingress identity `(tenant,installation,source,event_id)`; downstream dùng internal inbox UUID. Raw body hash phát hiện conflicting reuse. Execution unique theo rule revision+inbox.
3. Normative webhook, callback và access/state docs là đầu vào implementation; không tự invent auth/DSL trong adapter.
4. OpenAPI vẫn 3.1.0, path `/api/v1`; info.version `0.2.0` thay illustrative `1.0.0` là pre-release revision, không deployed API downgrade. Typed P0 paths/error/concurrency trước runtime.
5. Rule DSL all/any, JSON Pointer, gt numeric, exists không value; migrate unpublished examples cũ. Depth bounded trong schema; field/scopes/node quota ở runtime publisher.
6. Technical outbox/audit append tham gia business-owner transaction qua port; không FK/DbContext/domain mutation xuyên context.
7. Không mất accepted events trong process/network failure envelope đã định nghĩa; disaster RPO/RTO là gate riêng, không hứa zero loss sau total storage disaster.
8. Bind external trigger/action tới installation rõ ràng; descriptors có entityType và callback có typed external reference. Event subject bắt buộc, Demo duplicated externalEntityId phải bằng subject. Không chọn source/destination từ type string hoặc email.

## Breaking diff và migration
| Consumer cũ | Baseline mới | Migration |
|---|---|---|
| schemaVersion 1, events string[] | schemaVersion 2, descriptor[] | Publisher phát manifest v2 và schema bytes/hash; trust publisher, approve scopes/endpoints |
| capabilities events/automation.trigger | event.trigger | Đổi token, thêm event descriptor; không implicit alias |
| condition field tùy ý, exists value | JSON Pointer /data/..., exists không value | Migrate offline unpublished rules; gt numeric, all/any nếu cần |
| raw event_id downstream | inbox UUID | Runtime tương lai backfill via full ingress tuple; collision không rõ nguồn quarantine/manual review |
| descriptive errors/optional concurrency | Problem + If-Match | Consumer đọc ETag và xử lý 412/428/reasonToken/correlationId |

Chưa có deployment/data để migrate. Legacy v1 fixture được giữ để chứng minh reject có chủ ý; DemoModule v2 là consumer fixture mới. Sau release đầu, breaking change cần side-by-side version/sunset plan và generated SDK compatibility tests; ADR này không miễn quy tắc cho lần sau.

## Rollout và rollback
- Gói này: contract tests+review → merge sau review của người dùng → scaffold theo Wave 0. Không deploy service/rotate credentials.
- Rollback: revert baseline cùng tests/fixtures/docs, không chỉ một schema; baseline cũ vẫn illustrative. Runtime tương lai không hạ schema trên active installation: giữ snapshot, stop dispatch, rollback app tương thích hoặc forward-fix migration.
- Không hardcode DemoModule capabilities trong CRM; runtime đọc registry descriptors.

## Verification và remaining gates
Contract suite kiểm tra schema/sample/negative cases/API shape/signature known-answer. Chưa chứng minh RLS/JWT/SSRF/publisher crypto/atomic commit/concurrency/retry effects/SDK compilation/automated breaking diff/restore. Theo acceptance matrix, chưa đánh dấu done.
