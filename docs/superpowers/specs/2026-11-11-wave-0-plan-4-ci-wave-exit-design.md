# Wave 0 — Plan 4: CI, security gates và bằng chứng đóng wave

Ngày 2026-11-11. Original design surveyed `main` at `df993ae`. Local implementation subsequently authorized and reviewed task-by-task; acceptance remains OPEN/BLOCKED. [Actual Plan 4 evidence](../../engineering/08-WAVE-0-PLAN-4-VERIFICATION.md) records failures and missing independent trust/remote runs. No remote write/settings/deployment authorization is implied.

## Mục tiêu

Thiết lập GitHub Actions chạy các kiểm tra backend, frontend, contract/SDK, security và container trên đúng commit; tạo evidence cho cold start và remote CI để quyết định Wave 0 exit. Người review phải thấy kiểm tra nào thực sự chạy, skipped/failure, commit/base và kết quả scanner. Wave 0 exit không đồng nghĩa P0/MVP acceptance.

## Hiện trạng và nguồn

- [Plan 3 integration handoff](../../handoff/2026-11-11-wave-0-plan-3-integration-handoff.md), [Plan 3 evidence](../../engineering/07-WAVE-0-PLAN-3-VERIFICATION.md), Plan 1/2 evidence và roadmap.
- `AGENTS.md`; ADR, `docs/engineering/02-REPO-CI-CD.md`, `03-ACCEPTANCE-MATRIX.md`, `docs/operations/02-THREAT-MODEL.md`.
- `.github/workflows/contracts.yml` hiện chạy Python 3.15.2 contract suite. Remote run `42987032191` đã xanh nhưng 55/87 test Plan 3 skipped có lý do vì runner thiếu prerequisites; không xem đó là full contract gate. Plan 3 aggregate mặc định không chạy browser nếu thiếu `--with-browser`.
- `scripts/check-plan3.sh`, `tests/Plan3/`, hai `.NET` categories, `tests/Local`, `compose.yaml`, image/tool locks và browser suite đã có. Plan 2/3 local evidence là lịch sử, không chứng minh workflow mới hay remote run mới.
- Compatibility policy hiện yêu cầu base phù hợp; Plan 3 handoff ghi self-test base `40a7c91` do policy files khác trước đó. Đây không phải base cố định cho mọi PR: CI phải dùng base thực của event và xử lý policy change fail-closed.

## Phạm vi

Trong phạm vi: workflow CI PR/main push; locked tool provisioning; backend/integration/Compose, frontend/browser, full contract/SDK và Plan 3 harnesses; secret/dependency/container scans, SBOM; báo cáo redacted; local rehearsal, negative tests và remote execution evidence; documentation/acceptance matrix Wave 0.

Ngoài phạm vi: CRM login/tenant/business implementation, remote DemoModule E2E chưa có runtime, production deployment, image/package publication, infrastructure production, ký phát hành và production rollout. Không bật branch protection, sửa repository permissions hoặc secrets chỉ vì spec này được viết; cấu hình remote cần được chủ sở hữu ủy quyền khi có thay đổi cụ thể để review.

## Cách tiếp cận

1. Mở rộng contract-only job: ít cấu hình nhưng che phủ kém và dễ nhầm 55 skipped là success.
2. Các job tách theo boundary, shared pinned inputs, required aggregate kết hợp kết quả: chứng minh từng gate và dễ chẩn đoán.
3. Một job tuần tự chạy mọi thứ và deployment: vượt phạm vi và tạo feedback chậm/khó cô lập.

Đề xuất của người viết: cách 2 trên GitHub-hosted Linux runners, phù hợp workflow hiện tại. Tên jobs, scanner và runtime budgets là lựa chọn đề xuất để review, không phải quyết định người dùng đã duyệt.

## Workflow và coverage

Workflow dùng `pull_request` và `push` vào `main`; không chạy untrusted PR code bằng `pull_request_target`. Mỗi job có timeout hữu hạn, permissions tối thiểu `contents: read`, checkout credential persistence tắt sau fetch; không cần repository secrets. Pin mọi third-party Actions bằng full commit SHA; pin tool releases và artifact checksum/image digest hoặc lockfile. Runner label/OS image có thể được GitHub cập nhật: ghi actual runner image/version, không tuyên bố OS immutable.

| Gate | Bằng chứng bắt buộc |
|---|---|
| `backend` | locked .NET restore/Release build; Category!=Compose với PostgreSQL Testcontainers thật; `tests/Local` Python; kết quả test machine-readable |
| `compose` | Category=Compose, API/Keycloak/PostgreSQL thật; lifecycle/secret/lock tests, không shared runner state |
| `contracts-sdk` | Python contract suite đầy đủ kể cả 55 Plan 3 tests; generated-check, bốn SDK build/pack + independent consumers, compatibility gate và Plan 3 harnesses |
| `web-browser` | locked npm install, typecheck/lint/Vitest/build/leak scan; Playwright pinned browser qua test-owned local stack/API thật |
| `security` | static security analysis cho C#/TypeScript source, secret scan, dependency vulnerability/license checks cho Python/npm/NuGet/tool locks; container image scan và SBOM cho images thực tế |
| `wave0-required` | tổng hợp named jobs; fail nếu required job failed/cancelled/skipped/missing hoặc tests không chạy; không `continue-on-error` cho blocking gate |

Chạy đủ năm harnesses `tests/Plan3/aggregate-reporting.sh`, `ci-environment.sh`, `negative-cases.sh`, `scratch-cleanup.sh`, `clean-export.sh`; ghi command/inputs/counts. `clean-export` phải dùng exact candidate SHA, trusted base và browser path trong stack riêng. Nếu một harness cố ý mô phỏng môi trường thiếu prerequisites, các skip **nội bộ của case đó** được assert rõ; chúng không thay thế suite thực tế không skip. Có thể chia job để không chạy lặp cùng một check vô ích; mapping evidence phải chứng minh coverage.

Python contract runner phải xuất structured counts, phân biệt pass/fail/skip và fail nếu suite thiếu prerequisites, số test thấp hơn checked baseline hoặc có skip ngoài allowlist được review. Ban đầu gate đầy đủ kỳ vọng ít nhất 87 tests, zero unexpected skips; không hardcode exact count để cản test mới. Tương tự, .NET TRX, Vitest và Playwright phải có test count >0, fail/error/skip policy rõ; hiện tại 70 non-Compose, 14 Compose, 54 Vitest, 4 browser là tham chiếu lịch sử, không tự cập nhật ngưỡng từ output candidate. Không dựa vào exit code `unittest` một mình.

Thiết lập reviewed minimum/required-test identities cho **từng** suite: khởi điểm tham khảo 87 contract, 20 Local Python, 70 non-Compose, 14 Compose, 54 Vitest và 4 browser; baseline cuối xác lập bằng discovery thực tế trên pinned runtime và được review. Reports phải bind job/run/candidate SHA và không stale; thiếu/malformed report, zero hoặc partial discovery, missing required identity, unexplained skips đều fail. Không cho candidate PR tự hạ ngưỡng/allowlist mà không qua policy transition được kiểm soát. Thêm tests chứng minh thêm test bình thường vẫn pass và xóa/skip required test bị chặn. Cập nhật `tests/Plan3/ci-environment.sh` và assertion tương ứng trong `aggregate-reporting.sh`: hiện chúng chốt chính xác 87/55/32, phải kiểm tra named missing-prerequisite cases và reviewed coverage thay vì khóa tổng số; không xóa test mô phỏng môi trường thiếu dependency.

## Revision và compatibility trust

PR job kiểm tra merge candidate cụ thể và ghi cả PR head SHA, base SHA từ event, candidate SHA và merge-base đã resolve; checkout/fetch đủ Git history để `compatibility.py --base-ref <trusted-base-sha>` đọc được Git object. Không dùng `HEAD~1`, hardcode `40a7c91`, current worktree baseline, PR-supplied arbitrary ref hoặc empty baseline khi thiếu history. Base/candidate không resolve hoặc PR merge ref stale/unavailable: nonzero `CI_BASE_UNAVAILABLE`/`CI_CANDIDATE_UNAVAILABLE`, rerun sau khi sync.

Với PR merge candidate của GitHub, xác minh checked-out HEAD chính là event candidate SHA, merge commit có đúng hai parents là event base/head theo thứ tự, và `git merge-base HEAD <event-base>` bằng event base; ghi cả comparison base thực tế do Plan 3 tool resolve. Nếu event base đã advance, merge ref thay thế, parents không khớp hoặc object thiếu thì fail và chờ GitHub tạo candidate/run mới; không so với ancestor cũ rồi báo theo event base. Negative tests phải tái hiện từng trường hợp. Nếu remote dùng merge queue, required checks cần trigger `merge_group`, verify event candidate/base và ancestry tương ứng, đồng thời có remote evidence riêng; khi chưa xác minh queue, ghi UNKNOWN, không claim merge-queue enforcement.

Main push dùng `github.event.before` và after SHA, chứng minh before là ancestor; zero/missing/non-ancestor SHA fail `CI_BASE_UNAVAILABLE` và yêu cầu trusted baseline do maintainer chỉ định qua quy trình riêng được review. Không tự dùng HEAD so với chính nó. Manual run nếu được thêm phải yêu cầu explicit immutable base/candidate SHA, validate ancestry và provenance. Không lấy PR title/body/branch string đưa trực tiếp vào shell; dùng event fields qua environment và validate SHA.

Kiểm tra thêm event `forced` và reject force-push theo policy, kể cả trường hợp before vẫn là ancestor. Nếu event metadata không đủ để xác định thì fail closed, không suy diễn từ ancestry đơn thuần. Push phải xác minh checkout HEAD bằng event after; không nhận arbitrary maintainer-provided SHA mà không có trusted provenance.

Security/scope/header, SDK surface, generator-only policy và conservative JSON Schema gates giữ nguyên Plan 3. Candidate thay policy/tool config làm gate trả `CONTRACT_POLICY_REVIEW_REQUIRED` phải dừng; PR không tự thêm waiver/relax threshold hay đổi base. Bootstrap hoặc intentional breaking change cần ADR, migration/rollback và consumer tests cùng trusted baseline advancement được review tách biệt; cách thiết lập enforcement ngoài repo phải được chủ sở hữu duyệt. Local CLI/PR workflow trong cùng repo không tự chống được PR sửa chính workflow/gate: không claim branch protection hoặc independent trust khi chưa xác minh repo rules.

Policy transition có chủ đích cần verifier độc lập với code PR và attestation được maintainer tin cậy ký/kiểm soát, bind chính xác old/new policy hashes, event base + PR head/merge-candidate tree, phạm vi policy files, ADR, consumer tests, reviewer và expiry. Verifier/trust key phải đến từ base-controlled hoặc independently pinned required workflow, không từ candidate PR; PR file tự khai approval không đủ. Main push đầu sau merge xác minh `before`, reviewed merge method và resulting tree/hash khớp attestation; nếu squash/rebase hoặc base advance làm thay đổi ràng buộc, yêu cầu evidence/attestation mới. Rollback dùng transition ngược được review. Test transition hợp lệ cho PR **và** first push, cùng forged/expired/wrong-base/hash/commit cases. Nếu chưa có trusted verifier/attestation capability, policy change và Wave 0 liên quan remain BLOCKED; không thêm bypass không được xác thực vào Plan 3 gate. Attestation mechanism/ruleset là implementation proposal, cần quyền và ủy quyền remote tương ứng; spec không khẳng định hạ tầng đó đã tồn tại.

Passing path cho transition đã duyệt: refactor gate để tách **policy-identity preflight** khỏi **semantic compatibility evaluation**. Verifier tin cậy xác minh attestation trên đúng SHA/hashes rồi cho phép chính xác transition đó vượt preflight; attestation tự nó không chứng minh contract compatible. Tiếp theo chạy evaluator với cả policy cũ và policy mới trên cùng canonical base/candidate contracts thực tế; mọi wire/security/schema/SDK breaking result hoặc tool failure của một trong hai đều fail. Dùng immutable policy snapshots đã hash để đánh giá, không thay candidate contracts hoặc event base; report hashes, cả hai kết quả và attestation identity. Candidate code chạy unprivileged không secrets; verifier không thực thi arbitrary PR scripts trong privileged context. Khi policy không đổi, chạy đường Plan 3 bình thường. Positive tests phải chứng minh signed transition có hai kết quả compatible pass trên PR và first push; missing/forged/expired attestation, sai tree/base, policy mới hoặc cũ phát hiện breaking đều fail. Đây là thay đổi có review tới gate hiện tại, không phải đổi base để làm xanh.

## Supply chain, secrets và artifacts

Chọn scanner stable có thể chạy không cần credentials trả phí, version/digest pinned; builder xác minh license/data-source/support và availability trên repo/runner. Static security analysis phải thực sự phân tích C# backend và TypeScript frontend/SDK code theo supported scanner configuration, với positive security fixture và fail nếu analyzer không chạy/không hỗ trợ ngôn ngữ; không thay bằng lint/typecheck. Builder ghi rõ generated-code coverage và rule exclusions. Quét full dependency locks npm/Python/NuGet, direct + transitive khi scanner hỗ trợ; unsupported ecosystem/tool lock phải báo coverage gap và đóng gate bằng scanner tương ứng, không mặc định pass. Quét published local API, migrator, PostgreSQL và Keycloak images theo pinned refs thực tế; không pull tag `latest`. Tạo SBOM machine-readable cho source dependencies và images, gắn input image digest/tool version.

Policy đề xuất: vulnerability/SAST severity HIGH/CRITICAL → fail; mọi secret finding của rule đang bật → fail trừ exception cụ thể đã review độc lập, kể cả unverified/offline/revoked/không thể verify. Verification status không thay severity; không mặc định unverified là false positive. Secret scan bao phủ candidate tree và toàn bộ PR/push commit range từ trusted base, kể cả secret được thêm rồi xóa; thiếu object/history fail closed. Full-history hoặc trusted baseline cho scan được công bố, không in matched value. Unknown severity, scanner/database update/network/permission failure → fail `CI_SCAN_INCOMPLETE`, không coi zero findings. License policy phải công bố explicit allowed/denied/unknown; unknown/restricted fail review, không tự khẳng định legal approval. Exceptions chỉ cho finding cụ thể, owner/reason/expiry và bằng chứng review; không wildcard, không secrets hoặc automatic approval từ PR. Baseline/exceptions được review độc lập và kiểm tra expiry; không vô hiệu gate bằng config PR tự sửa.

Chỉ test-owned synthetic credentials; không in payload, token, connection string hoặc raw service logs. Scanner output có thể chứa matched secret: không upload raw SARIF/logs, dotenv, database/realm export, browser trace/screenshot/video hay Docker inspect dump mặc định. Artifacts allowlist gồm normalized summary, non-sensitive test counts, tool versions/digests, SBOM đã kiểm tra không chứa secrets và safe correlation IDs; bounded retention được document. Audit sentinel secrets qua log/artifact/config/image-history, test failure không echo sentinel. Job cleanup chỉ xóa resources do chính job tạo; không lưu credential trong caches hoặc cross-trust cache.

Sensitive subprocess stdout/stderr và test failures được capture vào private temporary files trước khi GitHub log/annotation nhận output; chỉ phát allowlisted normalized fields sau validation, không xuất raw scanner annotations. Sanitization/parse thất bại → safe reason + nonzero, không echo input. Regression dùng sentinel trong scanner/test-process failure, gồm multiline/encoded fixtures đã xác định; không tuyên bố filter bao phủ mọi encoding hoặc có thể ngăn malicious PR code in dữ liệu khi chính workflow do PR kiểm soát. Không có credentials thật trong runner untrusted. Protected verifier/workflow bên ngoài candidate là điều kiện để claim enforced trust.

Mỗi Compose/browser job ghi image identity thực sự chạy (local image config digest/ID, platform, và OCI manifest digest khi có) và scan đúng immutable images đó trong cùng trust context trước khi báo pass. Aggregate so test evidence với scanner evidence theo run/job/image identity và fail nếu thiếu/khác; không so sánh mù hai tag được build độc lập. Nếu dùng cùng-run OCI artifacts để chia sẻ image, bind content digest/run ID, kiểm tra secret trước upload và chỉ consume trong job không đặc quyền; không chạy artifact untrusted ở privileged context. Local `image ID` không được giả làm registry manifest digest. Negative fixture thay digest phải fail.

## Evidence, failures và duration

Mỗi job xuất JSON summary: job/step, candidate/base/merge SHA khi áp dụng, command ID, status, passed/failed/skipped/total, reason token + correlation ID cho closed failure, elapsed time, actual tool/image versions/digests và artifact path. Không chứa command arguments nhạy cảm. `wave0-required` tổng hợp cả setup failure/timeout/cancel/missing job, và không chuyển `not run` thành success. Third-party step không có reason token phải được wrapper chuyển thành token an toàn; không nuốt exit code.

Aggregator khai báo inventory `needs` tường minh và điều kiện `always()` để chạy sau job failed/skipped khi workflow còn có thể chạy; xét `needs.<job>.result` độc lập với summary, chỉ `success` được chấp nhận. Thiếu/duplicate/malformed/wrong-run/wrong-SHA summary là failure. Artifact/summary không được tự khai job success thay platform status. Nếu **toàn workflow** bị cancel hoặc aggregator không được scheduler chạy, không thể bảo đảm nó phát JSON/failure: giữ workflow cancelled/missing là non-success, không claim aggregate đã chạy; external required-check governance quyết định blocking. Test remote case job fail/skip/cancel và workflow cancel, ngoài parser fixtures. Tránh workflow path filters khiến required status không xuất hiện; merge queue phải có event phù hợp trước khi áp dụng required rule.

Plan 2 merged-main Compose 14 mất ~26.7 phút, non-Compose 70 ~9.5 phút; prepared Plan 3 contract suite 87 mất ~18 phút. Đây là lịch sử local, không estimate chắc chắn remote. Đặt timeout theo measured runner: đề xuất Compose 60 phút, contracts 60 phút, browser 35 phút, các job khác 30 phút; tăng khi evidence thực tế yêu cầu, không vô hạn. PR gates có thể song song trên isolated runners, không chia mutable state; chi phí/quota và thời gian thực tế phải báo cáo. Nếu runner/quota không đủ thì gate BLOCKED, không skip để đóng Wave 0.

## Kiểm thử và nghiệm thu

Viết negative/rehearsal tests trước implementation; workflow parsing/lint chỉ chứng minh cấu trúc, không thay actual remote job. Dùng real PostgreSQL/Testcontainers/Compose trong integration paths.

| ID | Điều kiện bắt buộc |
|---|---|
| W0P4-01 | Clean checkout locked installs/build; SDK/Node/npm/Python/Actions/images/diff tooling khớp pins; actual runner identity recorded |
| W0P4-02 | Backend non-Compose + real Compose tests chạy >0, no unexpected skip; startup/outage/cleanup evidence |
| W0P4-03 | Prepared contract suite >=87 không skip; full Plan 3 SDK/generation/consumer gate và năm harnesses chạy thật; simulated CI missing-prereq test vẫn chỉ là negative test |
| W0P4-04 | Frontend build/typecheck/lint/test và Playwright thật, actual requests qua owned backend; `--with-browser` không được bỏ qua |
| W0P4-05 | PR base/head/merge SHA đúng; unchanged/additive fixture pass; breaking, security-scope/signature, schema, generator-only regressions và thiếu base fail đúng reason |
| W0P4-06 | Main push before/after được kiểm tra; force-push/zero SHA/non-ancestor/missing object fail closed; no hardcoded self-test base |
| W0P4-07 | C#/TypeScript static security analysis, secret/dependency/license/container scans và SBOM có coverage/version/data freshness; known-positive synthetic finding fail và scanner unavailable fail; không scan-only-empty output |
| W0P4-08 | Synthetic sentinel không lộ trong captured logs/artifacts/images/config ở success và failure; test không in raw matches |
| W0P4-09 | Aggregator fail với missing/skipped/cancelled/failed job, zero tests hoặc test skip không cho phép; correlation/reason có mặt |
| W0P4-10 | PR từ fork/untrusted context không có secrets/write privileges; no `pull_request_target`, untrusted expression shell injection, cross-trust cache hoặc privileged artifact execution |
| W0P4-11 | Exact-SHA clean-source local rehearsal và remote PR + main-push runs có provenance, per-job counts, durations, artifacts và same required check set; recorded evidence distinguish local/remote |
| W0P4-12 | Wave exit review: reproducible cold start, SDK/diff/build/tests/scans và remote CI pass; matrix/docs cập nhật; branch protection/required checks được kiểm tra và ghi VERIFIED hoặc OPEN, không claim enforcement khi chưa xác minh |

Negative tests thay scanner bằng controlled test double chỉ để kiểm tra wrapper error handling; mỗi blocking scanner class phải có fixture chạy scanner thật và positive detection (C# SAST, TypeScript SAST, dependency, secret, license và container), cùng inventory/coverage nonempty. Secret scanner thật phải phát hiện synthetic unverified finding; xử lý verified/unverified được test bằng local controlled verification endpoint nếu tool hỗ trợ, nếu không thì test policy adapter bằng canned scanner-result fixture và không claim đã verify credential với provider. Không đưa real credential vào fixture. Tool vulnerability DB phải có timestamp và maximum age cấu hình được review (mặc định đề xuất 24 giờ, nếu nguồn không công bố timestamp ghi coverage gap/fail; không giả timestamp download là database freshness). Broken lockfile, tampered generated file, known breaking contract, unavailable scanner, introduced-then-deleted secret, image-digest mismatch, partial discovery, deliberate skipped required test và một test `tests/Local` cố ý fail phải làm đúng gate/aggregate fail. Không ghi secrets trong failure output.

## Remote review và Wave 0 exit

Trước khi workflow được merge/push, chuẩn bị diff, local rehearsal, negative evidence và danh sách proposed required checks. Sau khi có ủy quyền phù hợp cho remote action, chạy workflow trên PR và `main` push; record run URLs, event, exact SHAs và job results. Nếu fork approval, Actions minutes, package registry, runner support hoặc repository policy chặn, ghi blocker và giữ exit OPEN; không thay bằng local pass.

Evidence phải liên kết reviewed PR candidate/tree và merge method với commit thực tế trên main; chứng minh final implementation hiện diện trong designated acceptance SHA và main-push run thành công trên SHA đó, không ghép hai run xanh không liên quan. Nếu workflow, policy, source, locks, tests hoặc tài liệu acceptance thay đổi sau designated SHA, trạng thái review/acceptance phải đánh giá lại và chạy lại gates bị ảnh hưởng trên final candidate; chỉ docs-only không ảnh hưởng gate có thể reuse evidence khi chứng minh diff và ghi rõ SHA. Record remote status ngay cả khi ruleset/enforcement chưa xác minh.

Wave 0 chỉ đóng khi W0P4-01→12 pass và evidence được review. Theo phạm vi Wave 0 đã thống nhất (CI và remote run), W0P4-12 có thể PASS khi toàn bộ checks/evidence pass và trạng thái branch protection đã được báo rõ, kể cả khi enforcement còn OPEN. Branch protection là gate quản trị riêng: chỉ đánh dấu VERIFIED khi rules thực sự enforce các checks và được xác minh; thay đổi cần ủy quyền phù hợp. Thiếu quyền đọc phải ghi UNKNOWN/OPEN, không chặn Wave 0 kỹ thuật nhưng không được claim enforcement. Remote xanh trên một commit không chứng minh enforcement hoặc security tuyệt đối. `RT-*` tenant/OIDC/business/remote DemoModule/durable events/outage/restore và P0 vẫn OPEN theo AGENTS.

## Rollback và quyết định giao builder

Workflow là code: rollback bằng revert workflow/config commit; không reset DB volumes, publish images hoặc đổi contracts. Revert không nên ngầm xóa required-check rule làm repository bị kẹt; kiểm tra status names/rules trước thay đổi remote. Tạm tắt gate hoặc baseline exception cần quyết định được review, không được tự làm để xanh.

Builder chọn scanner cụ thể, pinned versions/SHAs, runtime report parser, job decomposition và safe artifact format sau khi xác minh availability. Không tự yêu cầu secret/quyền ghi khi public data/tool đủ; ghi mọi tool limitation và chi phí/quota thực tế. Builder review nêu hai câu hỏi: static security analysis được bổ sung vào gate; branch protection là trạng thái quản trị riêng, không phải điều kiện đóng Wave 0 kỹ thuật, và không được claim VERIFIED khi chưa kiểm tra. Sau review spec, viết implementation plan; lượt này không sửa workflow, cài scanner, chạy CI hoặc cấu hình GitHub.

## Tài liệu tham khảo

- [GitHub: securely using pull_request_target](https://docs.github.com/en/actions/reference/security/securely-using-pull_request_target) — không chạy untrusted PR code trong privileged context.
- [GitHub: workflow events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows) — PR merge SHA và event context.
- [GitHub: required status checks](https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/troubleshooting-required-status-checks) — skipped jobs/dependencies và merge queue có semantics riêng.
- [GitHub: dependency review](https://docs.github.com/en/pull-requests/how-tos/review-pull-requests/reviewing-dependency-changes-in-a-pull-request) — availability phụ thuộc repository/plan; không giả định có feature.

Các nguồn không thay runtime/remote evidence của dự án.
