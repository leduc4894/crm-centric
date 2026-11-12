# Wave 0 Plan 4 — quyết định thu gọn CI MVP

## Objective and authority

Người dùng đồng ý giữ CI cần cho MVP và chuyển verifier độc lập cùng supply-chain hardening nâng cao sang trước production. Phiên này cập nhật tài liệu quyết định, không ủy quyền tự động ngoại lệ security, thay workflow, push/merge, repository settings hoặc deployment. AGENTS.md golden rules, P0 và RT-* không thay đổi.

## Completed

[Quyết định CI MVP](../engineering/09-WAVE-0-CI-MVP-SCOPE.md) nêu gates, điều kiện evidence, phân định hardening, mapping W0P4 và trạng thái OPEN. Đã thêm chỉ dẫn ưu tiên scope mới vào spec/plan Plan 4, acceptance matrix, CI docs, verification ledger và roadmap; evidence cũ giữ nguyên. Mức severity, ngoại lệ cụ thể và approval finding chưa được người dùng quyết định, không tự suy diễn đã phê duyệt.

## Workspace and verification

Branch `feat/wave-0-plan-4-ci`; resolve current HEAD via git. Các thay đổi phiên này gồm tài liệu nêu trên và handoff này; chưa sửa runtime/workflow/locks/contracts. Untracked `docs/handoff/2026-11-12-wave-0-plan-3-resumption-handoff.md` có sẵn từ trước được giữ nguyên. `git diff --check`: PASS; kiểm tra relative Markdown links của tám file thay đổi: PASS; không chạy runtime/CI/scanner vì documentation-only, không có remote action.

## Status and next steps

Wave 0, P0 và RT-* OPEN. Workflow hiện tại vẫn chặn trust/image/security; không coi việc đổi scope là pass hoặc xóa gate. Viết plan thực thi CI MVP, phân định rõ scanner blocking và quy trình review findings, sau đó triển khai và chạy full local/remote evidence khi được ủy quyền. Hardening vẫn OPEN trước production; verifier không tồn tại không cho phép policy transition bypass. Review governance trước bất kỳ thay đổi repository settings.

## MVP execution continuation

Người dùng yêu cầu viết plan ngắn và triển khai CI MVP. [Plan điều chỉnh](../superpowers/plans/2026-11-12-wave-0-ci-mvp-adjustment.md) phân tách image/trust hardening khỏi thực thi test MVP và giữ secret/dependency scan fail-closed, không tự phê duyệt findings. Tiếp tục thực thi local theo SDD tuần tự; remote operations/settings vẫn cần ủy quyền riêng. Wave 0/P0/RT-* OPEN. Untracked handoff Plan 3 có sẵn được giữ nguyên.
