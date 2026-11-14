# Wave 0 Plan 4 — phạm vi CI cho MVP

Ngày quyết định: 2026-11-12. Người dùng đồng ý thu gọn Plan 4 về CI cần thiết để phát triển MVP an toàn và chuyển bảo đảm nâng cao sang hardening trước production. Tài liệu này là quyết định phạm vi hiện hành; nếu phần yêu cầu exit trong spec/plan Plan 4 cũ mâu thuẫn, tài liệu này ưu tiên. Không thay đổi hợp đồng công khai, thứ tự wave, hay các golden rules trong AGENTS.md.

## Điều kiện Wave 0 CI cho MVP

Các mục sau phải được triển khai và có evidence trên revision xác định trước khi đánh giá Wave 0 CI là PASS:

1. CI chạy trên đúng revision được kiểm tra; báo rõ SHA, các job thực chạy và kết quả pass/fail/skipped. Failure, missing job hoặc test bắt buộc không chạy không được báo success. Không dùng local pass làm bằng chứng remote.
2. Locked install/build; backend và integration dùng PostgreSQL thật, Compose tests, frontend build/typecheck/lint/test và browser tests thực tế theo các suite hiện có. Thiếu prerequisite, zero discovery hoặc unexpected skip là failure; simulated negative skips không phải full-suite pass.
3. Contract/schema và generated SDK/consumer checks, compatibility trên base event có thể xác minh. Không tự đổi base, sửa contract hoặc bỏ kiểm tra để gate pass. Các golden rules về contract, tenant, durable events và bảo mật vẫn áp dụng.
4. Secret scan và dependency vulnerability scan phải chạy, báo coverage, lỗi công cụ và findings an toàn; không in secrets hay raw payload. Secret thực phải được xử lý; false positive hoặc finding còn lại phải được phân loại bằng evidence, owner và quyết định review rõ ràng. Không tự giả định finding là an toàn, không tự phê duyệt exception và không coi scanner không chạy là pass.
5. CI definition được chạy trên remote cho candidate tương ứng khi được ủy quyền, ghi URL, revision, job status và kết quả. Không suy ra branch protection hoặc enforcement từ một run xanh. Các thiết lập repository, secrets, push/merge/deploy vẫn cần ủy quyền phù hợp.

Người dùng chưa phê duyệt mức severity, ngoại lệ cụ thể hay ngưỡng rủi ro production. Cần một quyết định review cụ thể cho findings còn lại trước khi coi một finding bị chặn hiện nay là không chặn MVP; báo cáo, owner và lý do phải có thể kiểm tra. Việc thu gọn scope này không tự phê duyệt ngoại lệ hoặc làm workflow hiện tại xanh.

## Chuyển sang hardening trước production

Các mục sau không còn là điều kiện bắt buộc riêng để đánh giá Wave 0 CI cho MVP; chúng vẫn OPEN và cần được xử lý/review trước production hoặc trước khi dựa vào chúng như một bảo đảm enforcement:

- Verifier độc lập, keys/attestation và dual-policy transition được thực thi độc lập. Cho tới khi có verifier, policy transition không được tự bypass; workflow hiện hành vẫn có thể fail `CI_TRUST_NOT_PROVISIONED`.
- Image-level exact-digest provenance và scanner/SBOM đầy đủ cho mọi image test, cùng policy bắt buộc không có HIGH/CRITICAL/UNKNOWN trên mọi dependency/image. Findings và coverage gaps phải được ghi nhận; tiêu chí cụ thể và owner cần review trước production.
- Full license clearance và xác minh độc lập toàn bộ transitive tool inventory; license không rõ hoặc bị hạn chế không được ngầm coi là đã được phê duyệt để phát hành.
- Diễn tập remote fork/merge queue/cancellation, independent required-check enforcement và chứng minh end-to-end liên kết PR/merge method/main SHA. Merge queue và branch protection vẫn UNKNOWN nếu chưa xác minh; không kích hoạt quy tắc dựa trên giả định đã có enforcement.

Phân loại này không cho phép chạy PR code với secrets/write privilege, bỏ SSRF/replay protections, commit credentials, hay bỏ kiểm tra tenant. Các yêu cầu bắt buộc của AGENTS.md và security contracts vẫn giữ nguyên. Remote DemoModule E2E, concurrent duplicate, tenant isolation, outage/recovery và restore drills vẫn là điều kiện acceptance/P0, không bị chuyển hoặc đóng bởi quyết định này.

## Ánh xạ evidence cũ và trạng thái

W0P4-01–06, 08–10 có thành phần thuộc CI MVP và thành phần hardening; đánh giá theo yêu cầu cụ thể ở trên, không tự chuyển toàn bộ ID thành PASS. W0P4-07 tách thành secret/dependency scans + triage cho MVP và full image/SAST/license/SBOM coverage theo phạm vi hardening. W0P4-11 giữ remote CI trên revision xác định cho MVP; proof PR/main/merge, fork và cancellation đầy đủ thuộc hardening. W0P4-12 là review exit theo scope mới, vẫn cần actual MVP evidence. Ledger [08](08-WAVE-0-PLAN-4-VERIFICATION.md) giữ nguyên số liệu lịch sử; không thay đổi thất bại thành pass.

**Trạng thái hiện tại (2026-11-14): Plan 4 / Wave 0 CI cho MVP PASS và
CLOSED; P0/RT-* vẫn OPEN.** Remote run
[43616369162](https://github.com/leduc4894/crm-centric/actions/runs/43616369162)
trên branch head `2c9ad713d05b009fd7888f00a3c8fe2d2979a9e0` đã PASS backend96,
Compose14, contracts88, browser58, security1 và aggregate `wave0-required`, không
failure/skip. Năm summary cùng bind validated candidate
`1a9736de5be613bfe8586011274543452c6cf0c1` và base
`df993ae4ff44510a2ecfc861bb277a3f9418577f`. Artifact actions dùng signed
Node24 releases pin full SHA; log không còn warning Node20. Evidence chi tiết ở
[ledger08](08-WAVE-0-PLAN-4-VERIFICATION.md).

Closure này cho phép tiếp tục Wave 1 theo work order. Nó không đóng P0, không
chứng minh production readiness và không thay đổi backlog hardening nêu trên.
Không có merge/main push, branch-protection/ruleset change hoặc deployment trong
closure này.

## Công việc tiếp theo

1. Bắt đầu Wave 1 trên nền required CI hiện đã PASS; giữ nguyên contract/security invariants.
2. Duy trì các review có expiry và để finding mới/khác nội dung fail closed.
3. Hoàn thành backlog hardening có owner/tiêu chí trước production; không claim branch protection verified khi chưa kiểm tra.
4. P0/RT-* chỉ đóng sau các acceptance drill được AGENTS.md yêu cầu.

## Làm rõ ngày 2026-11-13 — review bởi maintainer cho MVP

Người dùng xác nhận ưu tiên phát triển hướng production thực dụng; verifier độc lập là hardening trước production, không phải điều kiện tiên quyết riêng cho MVP. Có thể review một cơ chế per-ID do candidate sở hữu nếu maintainer đáng tin cậy review cả code và policy; phải ghi rõ không có bảo đảm độc lập chống candidate sửa code/policy. Không tự biến nhãn `approved` trong JSON thành quyền phê duyệt của người dùng. [Đề xuất cụ thể](evidence/2026-11-13-mvp-exact-finding-policy.md) và JSON 64 bản ghi vẫn PENDING, không nối workflow; contract4 có nguồn trong candidate, JAR60 đề xuất review snapshot chính xác dựa trên đối chiếu bytes tại review trước đó (không claim CI tái tính hoặc độc lập). Cả64 vẫn BLOCKED vì chưa có approval. Người dùng chưa duyệt ngoại lệ thực tế; phần làm rõ này không kích hoạt exclusion, không đổi verdict hiện hành hay đóng Wave 0.

## Kích hoạt review 64 ngày 2026-11-13

Người dùng đã phê duyệt thực thi đề xuất chính xác 64 ("Làm đi"): owner `maintainer`, thời hạn review 90 ngày từ 2026-11-13, hết hạn `2027-02-11T00:00:00+00:00`. Phần mô tả PENDING ở trên là lịch sử trước approval. Policy hiện hành: [tools/ci/mvp-secret-reviews.json](../../tools/ci/mvp-secret-reviews.json). Chỉ MVP adapter áp dụng 60 JAR snapshot đã review và 4 contract hash tái tính từ bytes; hardening scanner vẫn chặn tất cả secrets. Xem [activation evidence](evidence/2026-11-13-mvp-secret-review-activation.md). Đây là boundary do candidate sở hữu, được maintainer review; verifier độc lập vẫn OPEN trước production. Approval này không đóng dependency findings, Wave0/P0/RT-* hoặc thay thế remote evidence.

## Balanced maintainer remediation — 2026-11-13

The user authorized the exact sixty historical metadata mappings and practical
maintainer review of the two policy changes, and authorized adjusting the plan.
[Historical policy](../../tools/ci/mvp-history-reviews.json) binds all sixty full
native record IDs to their original commit, actual Git blob, document digest,
field, rule, location, single multiplicity, pinned scanner/config, maintainer and
2027-02-11 expiry. Other historical/new/deleted/merge findings remain blocking.
The existing sixty-four tree reviews keep their original scope.

For this MVP, [contract identity review](../../scripts/ci/contract_review.py)
authorizes only the two exact old/new file hashes recorded in the rehearsal,
against actual base `c4a5ef40a34daded023d5e5121c0462e94cc1487`, only through explicit
`--mvp-reviewed-policy` opt-in (MVP jobs pass it; default/hardening do not). The unmodified
semantic checks still run on actual base/candidate contracts with pinned oasdiff,
security/schema checks, generated SDK surfaces and historical consumers. This is
candidate-owned maintainer review, not independent enforcement. Old/new dual
policy executors and the legacy bootstrap are deferred to pre-production
hardening; no verifier capability, signature or dual execution is fabricated.
Reports explicitly record that limitation. Additional identity changes, wrong
base, expiry, semantic breaks and tool errors remain failures. This supersedes
the earlier statement that an independent transition verifier is a prerequisite
for this exact MVP delta; AGENTS golden rules and contract ADR requirements remain.
No remote action or Wave0/P0/RT-* acceptance approval is included.
