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

**Trạng thái hiện tại: Wave 0 OPEN; P0/RT-* OPEN.** MVP execution/reporting đã tách khỏi deferred image/verifier hardening; policy transition vẫn fail-closed. Full local attempts tại `5a17535` có backend96/96 và Compose14/14 PASS, contracts87/87 và browser58/58 PASS sau clean-state rerun, required security BLOCKED; xem [ledger08](08-WAVE-0-PLAN-4-VERIFICATION.md). Chưa có bộ năm gate PASS tại final SHA hoặc remote run; findings chưa được phê duyệt, không có exit claim.

## Công việc tiếp theo

1. Viết/cập nhật plan thực thi CI MVP từ scope này, phân định scanner blocking, finding triage và cách báo coverage; giữ security invariants và không suy diễn exception.
2. Điều chỉnh workflow, runners và tests theo plan được review; không làm thay đổi repository settings hoặc remote trong bước local.
3. Chạy full local jobs trên exact candidate, ghi kết quả và giới hạn; sau ủy quyền phù hợp chạy remote CI và ghi evidence.
4. Chỉ khi điều kiện MVP thực sự đạt mới review Wave 0 exit; duy trì backlog hardening có owner/tiêu chí trước production, và không claim branch protection verified khi chưa kiểm tra.

## Làm rõ ngày 2026-11-13 — review bởi maintainer cho MVP

Người dùng xác nhận ưu tiên phát triển hướng production thực dụng; verifier độc lập là hardening trước production, không phải điều kiện tiên quyết riêng cho MVP. Có thể review một cơ chế per-ID do candidate sở hữu nếu maintainer đáng tin cậy review cả code và policy; phải ghi rõ không có bảo đảm độc lập chống candidate sửa code/policy. Không tự biến nhãn `approved` trong JSON thành quyền phê duyệt của người dùng. [Đề xuất cụ thể](evidence/2026-11-13-mvp-exact-finding-policy.md) và JSON 64 bản ghi vẫn PENDING, không nối workflow; contract4 có nguồn trong candidate, JAR60 đề xuất review snapshot chính xác dựa trên đối chiếu bytes tại review trước đó (không claim CI tái tính hoặc độc lập). Cả64 vẫn BLOCKED vì chưa có approval. Người dùng chưa duyệt ngoại lệ thực tế; phần làm rõ này không kích hoạt exclusion, không đổi verdict hiện hành hay đóng Wave 0.

## Kích hoạt review 64 ngày 2026-11-13

Người dùng đã phê duyệt thực thi đề xuất chính xác 64 ("Làm đi"): owner `maintainer`, thời hạn review 90 ngày từ 2026-11-13, hết hạn `2027-02-11T00:00:00+00:00`. Phần mô tả PENDING ở trên là lịch sử trước approval. Policy hiện hành: [tools/ci/mvp-secret-reviews.json](../../tools/ci/mvp-secret-reviews.json). Chỉ MVP adapter áp dụng 60 JAR snapshot đã review và 4 contract hash tái tính từ bytes; hardening scanner vẫn chặn tất cả secrets. Xem [activation evidence](evidence/2026-11-13-mvp-secret-review-activation.md). Đây là boundary do candidate sở hữu, được maintainer review; verifier độc lập vẫn OPEN trước production. Approval này không đóng dependency findings, Wave0/P0/RT-* hoặc thay thế remote evidence.
