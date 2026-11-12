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

## Task 1 implementation — 2026-11-12

Objective: execute required MVP test jobs without deferred image scanners; preserve five jobs, immutable revision evidence, contract policy preflight and existing secret/dependency blocking. User authorized the local MVP adjustment plan; no finding exception, policy bypass or remote action is authorized.

Implemented explicit `--mode mvp` in job execution, safe completion and aggregate parsing. Default `hardening` behavior retains image verification. MVP fixture subprocesses receive neither image evidence hook (even when inherited); safe reports explicitly record `image_hardening.status=not-evaluated` and reject image clearance claims. Non-security MVP setup omits scanner provisioning. Testcontainers resource ownership and CLI run labels remain independent of image scanning. The MVP workflow no longer has an unconditional independent-trust exit; contract policy identity preflight is unchanged and policy transitions remain blocked without trusted approval. Security job code, scanner policies, finding thresholds and evidence requirements are unchanged for Task 2.

Changed files: `.github/workflows/wave0.yml`; `scripts/ci/{aggregate,completion,jobs,publication,summary}.py`; `scripts/ci/setup.sh`; `tests/CI/test_{aggregate,job_reports,summary,workflow}.py`; `tests/Integration/Fixtures/ContainerImages.cs`; this handoff. Public API/schema, lockfiles and application domain behavior unchanged. Branch `feat/wave-0-plan-4-ci`; implementation follows base `601dde8`; see commit history for Task 1 commit. Existing untracked Plan 3 handoff preserved.

Verification so far: RED confirmed missing MVP mode/parser/context, scanner setup still executing, and unconditional trust failure. GREEN focused regression coverage includes required test failure/skip/missing/wrong SHA, complete five-job reports without images, completion sanitation and unchanged security evidence gates. Local discovery PASS (20 tests); real PostgreSQL Testcontainers MigrationTests PASS (5 tests) under MVP execution context, no scanner hook, build included. Workflow contract lint, actionlint, shell syntax and `git diff --check` PASS. Initial CI discovery on system Python or incomplete PATH failed existing real policy semantic evaluator with `CONTRACT_TOOL_ERROR`; pinned Node/Python/toolchain rerun is required and its final outcome is recorded below. This was working-tree selective verification, not full exact-candidate MVP acceptance.

Remaining: Task 2 security scoped evidence/triage; Task 3 full exact candidate local five-job rehearsal and authorized remote evidence. Full backend/Compose/browser jobs, remote run, repository enforcement and P0 drills have not been completed by Task 1. Image clearance and independent trust hardening remain OPEN; Wave 0/P0/RT-* OPEN.

Additional Task 1 verification: real built API/container security fixture `ContainerRuntimeTests.Api_uses_only_application_secret_as_nonroot` PASS (1/1) under MVP context with image scanner hooks absent; actual container build/start, database migration, API readiness/nonroot/secret mounting and dependency outage assertions executed. Toolchain versions verified equal all six runner pins. This adds selective evidence, not a full backend/Compose job pass.

Final Task 1 CI regression: pinned Node/Python/toolchain `python -m unittest discover -s tests/CI` PASS, 120 tests, 0 failures/errors/skips, 305.037s. Includes real dual-policy semantics and unchanged-vs-changed preflight regressions. The earlier incomplete-PATH failure was resolved by using the verified toolchain, without changing policy code. Final syntax/lint/diff checks PASS. Task 1 implementation is complete; five-job exact-candidate/remote acceptance and Tasks 2–3 remain outstanding.
