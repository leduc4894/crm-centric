# Wave 2 — CRM Contacts: thiết kế

**Trạng thái:** đề xuất để người dùng review; chưa phê duyệt triển khai.
**Phạm vi:** US-03, US-04 và phần CRM của US-05; [backlog](../../roadmap/01-BACKLOG.md), [acceptance matrix](../../engineering/03-ACCEPTANCE-MATRIX.md), [data model](../../architecture/06-DATA-MODEL.md), [access/state](../../architecture/07-P0-ACCESS-AND-STATE.md), [CRM OpenAPI](../../contracts/crm-api.openapi.yaml).

## Mục tiêu và điều kiện vào wave

Agent có thể quản lý, tìm và xem contact trong tenant của mình, gắn tags, duy trì custom fields và xem timeline với nguồn rõ ràng. Màn hình hoạt động với API thật, được phân quyền và xử lý chỉnh sửa đồng thời. Contact là thực thể CRM, không phải tài khoản đăng nhập; email nullable, không là identity hay khóa duy nhất.

Trước khi triển khai, đối chiếu các yêu cầu và gate Wave 1 còn áp dụng với evidence trên candidate thích hợp. Quyết định hiện tại cho phép bắt đầu Wave 2 khi các điều kiện khác đạt, trong khi security FAIL được hoãn; không đổi kết quả security thành PASS hay coi P0 đã đóng. [Quyết định và evidence](../../engineering/11-WAVE-1-EXIT-VERIFICATION.md#conditional-authorization-for-wave-2--2026-11-26), [handoff gần nhất](../../handoff/2026-11-27-wave-1-pr3-ci-handoff.md). Xác định cụ thể điều kiện còn thiếu và xử lý theo phạm vi được phép trước khi code Wave 2. Remote DemoModule, outage/recovery và restore là các yêu cầu P0 về sau; không tự động coi chúng là gate Wave 1.

## Phương án và ranh giới

Chọn năm plan tuần tự theo chức năng: CRUD và contract; truy vấn; tags/fields; timeline và UI; exit. Cách này cho phép kiểm chứng từng lát cắt, giữ UI gắn contract đã ổn định và dành benchmark/full suite cho exit. Một plan lớn duy nhất khó review; chia backend và UI thành hai dự án độc lập làm chậm kiểm chứng luồng người dùng. Đây là đề xuất kỹ thuật dựa trên cấu trúc đã trao đổi, không mở rộng phạm vi MVP.

Không làm integration kernel, webhook ingestion, external identity mapping, automation, marketplace hay visual editor. Timeline nhận dữ liệu ngoài qua Wave 3; không giả lập tích hợp đã hoạt động. Không tự động liên kết contact theo email hoặc cấp membership khi tạo contact. Identity linking và extension panels không là điều kiện hoàn thành UI Wave 2.

## Contract, dữ liệu và bảo mật

- Bắt đầu từ OpenAPI CRM hiện có; xác định chính xác các operation, schema và SDK cần cho mỗi plan trước khi code. Contract-first; cập nhật schema, fixtures và generated TypeScript client khi cần. Breaking change phải có ADR, migration plan và consumer tests; không lặng lẽ đổi baseline.
- Contact CRUD gồm tên, email nullable, phone và soft delete theo contract. Dữ liệu tenant được lấy từ context đã xác thực và membership active, không tin tenant trong body. Mọi query/mutation dùng tenant predicate; PostgreSQL RLS, tenant-aware FK và role ứng dụng least privilege là lớp bảo vệ bổ sung. Integration tests dùng PostgreSQL thật qua Testcontainers.
- Contact GET/create/update trả strong ETag `"vN"`; PATCH/DELETE và mutation tags, fields, notes dùng If-Match. Thiếu trả 428, stale 412, wildcard/weak/malformed 400. Predicate cập nhật gồm tenant/id/version; tăng version atomic. Audit và outbox cho mutation phải cùng transaction theo kiến trúc hiện có; rollback không để audit/event giả. No-op tag vẫn kiểm tra version và không tạo audit/event giả. Mọi lỗi đóng có reason token và correlation ID, không log secrets hoặc PII không cần thiết.
- Tags có normalized name unique trong tenant. Field definition có key unique theo tenant, immutable sau tạo theo baseline; giá trị được validate theo definition và type. Không áp đặt email unique. Index, constraints và migration theo data model, có forward/rollback plan phù hợp.
- Tìm kiếm/filter đúng contract, không tải toàn bộ tenant về browser. Contacts phân trang keyset `(updated_at,id)`; timeline `(occurrence_time,id)` giảm dần. Cursor opaque, được ký hoặc validate, ràng buộc tenant/filter/sort; cursor sai hoặc khác ngữ cảnh bị từ chối. Không hứa snapshot khi dữ liệu thay đổi. Kết quả không lộ contact đã soft-delete trừ khi contract cho phép.
- Timeline đọc theo contact và tenant, giữ thời điểm và provenance; record từ inbox về sau unique theo `(tenant,contact,inbox,kind)` khi có inbox. Notes nếu triển khai theo contract phải tuân If-Match, audit và tenant isolation. Wave 2 chứng minh phần lưu/đọc của chính nó; dedup, reorder và crash qua pipeline thực phải hoàn tất Wave 3, không đánh dấu RT-EVT-01 hoàn tất sớm.

## UI và trải nghiệm

[Prototype index.html](../../../prototypes/centric-ui/index.html) là nguồn thiết kế: tham chiếu sidebar, topbar, bố cục, màu sắc, bảng, tags và biểu mẫu; sử dụng React/Vite và auth/tenant flow hiện có. Danh sách và chi tiết contact dùng API/SDK thật; không hiển thị số liệu mẫu như dữ liệu thật. Không biến nút hoặc thống kê trong prototype thành tính năng bắt buộc khi contract chưa hỗ trợ; ẩn hoặc ghi rõ chưa khả dụng thay vì tạo hành vi giả.

Danh sách hỗ trợ tìm/filter, trạng thái loading/empty/error, phân trang phù hợp cursor và điều hướng đến chi tiết. Chi tiết hỗ trợ thuộc tính, tags, custom fields và timeline; tạo/sửa/xóa theo quyền. Form có validation và phản hồi rõ ràng. Với 412, giữ nội dung người dùng nhập, giải thích xung đột và cho tải lại dữ liệu mới; không tự overwrite. Tenant switch không hiển thị dữ liệu tenant cũ. Thiết kế dùng được với bàn phím, nhãn biểu mẫu, focus và màn hình hẹp; UI không thay thế kiểm tra quyền ở server.

## Phân chia plan đề xuất

1. **Contact contract và CRUD:** rà soát contract và generated client; migrations, tenant-scoped CRUD/soft-delete, version, audit/outbox, permission và negative tests. Đầu ra là API chạy thật với evidence test liên quan.
2. **Search/filter/pagination:** truy vấn và index tenant-safe, cursor validation và tests khi filter, tenant, sort hoặc dữ liệu thay đổi. Không khẳng định benchmark exit chỉ từ fixture.
3. **Tags và custom fields:** definition, values, validation, gắn/gỡ tag, concurrency và audit. Kiểm thử tenant, permission, type, normalization và race liên quan.
4. **Timeline và UI:** phần lưu/đọc timeline và notes trong scope contract, provenance, UI list/detail và các luồng CRUD/tag/field; tích hợp SDK thật, kiểm thử UI và browser liên quan. Tích hợp external event vẫn thuộc Wave 3.
5. **Wave 2 exit:** review traceability, migration, audit và docs; chạy full suite trên GitHub CI và benchmark có evidence. Xử lý thất bại trước khi đánh giá exit; báo chính xác các gate còn fail/deferred.

Plan 1–4 có thể chia nhỏ thành commit review được; mỗi plan xác định danh sách test và điều kiện hoàn thành cụ thể. Các thay đổi dùng RED → GREEN → refactor. Test integration PostgreSQL dùng Testcontainers; không dùng in-memory thay thế cho isolation/concurrency.

## Chiến lược kiểm thử và exit

- **Plan 1–4:** chạy local chỉ các test liên quan đến thay đổi, bao gồm test unit, contract, integration PostgreSQL thật và UI/browser phù hợp. Viết test trước, ghi kết quả RED/GREEN và phạm vi đã chạy. Không chạy full suite local trong các plan; không coi test liên quan là bằng chứng full suite. Nếu cần CI hay workflow hiện tại tự chạy thêm, ghi nhận kết quả thực tế; không tự ý tắt gate bắt buộc.
- **Plan 5:** full test suite chỉ chạy ở bước Wave 2 exit **trên GitHub CI**, không chạy full suite local. Xác định workflow, candidate SHA, lệnh và artifact trước khi chạy; nếu workflow hiện tại chưa đáp ứng thì kế hoạch exit phải bổ sung cách chạy có thể kiểm chứng, không vô hiệu hóa gate. Ghi rõ job nào pass/fail/skip, security deferred và aggregate thực tế; không suy diễn pass từ test chọn lọc. Test tập trung local để chẩn đoán hoặc xác nhận sửa lỗi vẫn được phép, nhưng full suite chỉ chạy lại trên GitHub CI.
- Benchmark contact list với ít nhất 50.000 contacts/tenant và 50 concurrent users; p95 < 400 ms. Ghi CPU/RAM/DB/network, dataset/seed, workload, warmup, duration, sample count, error rate, p95, commit và artifact. Benchmark là kiểm chứng riêng tại exit; chọn môi trường có thể mô tả và tái lập, không mặc định runner GitHub đủ ổn định nếu chưa chứng minh. Không thay benchmark bằng seed fixture.
- Exit cần permission/concurrency/audit tests pass và bằng chứng đầy đủ cho phạm vi Wave 2, kể cả migration và tenant isolation; cập nhật acceptance matrix, docs và handoff theo kết quả thật. Security FAIL/deferred và P0 vẫn OPEN cho tới khi các điều kiện tương ứng được giải quyết; không tuyên bố P0 hoàn tất từ Wave 2 exit.

## Các quyết định dành cho kế hoạch chi tiết

Chọn test commands, cách chạy benchmark có tài nguyên đo được, query/filter cụ thể theo contract, phạm vi note và các chi tiết thiết kế responsive trong từng plan sau khi khảo sát mã; không mở rộng contract bằng giả định. Mọi thay đổi public contract phải qua kiểm tra tương thích và quy trình review đã quy định. Nếu điều kiện vào wave hoặc hạ tầng CI/benchmark chưa đủ, ghi blocker và evidence thay vì bỏ qua.
