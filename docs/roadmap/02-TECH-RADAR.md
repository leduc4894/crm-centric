# Technology Radar — hiện đại nhưng production-oriented (2026-11)

## Adopt in P0
| Domain | Choice | Reason |
|---|---|---|
| Backend | .NET 10 LTS / ASP.NET Core / C# | LTS production |
| Persistence | PostgreSQL, EF Core 10 | ACID, migrations; tenant isolation |
| Frontend | React, TypeScript strict, Vite | ecosystem, SPA dashboards |
| UI | TanStack Query/Table/Router; TanStack Form theo Plan 8 | data, bảng, routing và form; xem trạng thái triển khai bên dưới |
| Contract | OpenAPI 3.1.x if toolchain passes; track 3.2.1 | 3.2.1 exists but tool support must be verified |
| Event schema | JSON Schema + CloudEvents-aligned envelope | interoperability |
| Auth | OIDC/PKCE, Keycloak or managed IdP | do not hand-roll login/security |
| Testing | xUnit, Testcontainers, Vitest, Playwright | full-stack confidence |
| Observability | OpenTelemetry + Grafana stack | logs/traces/metrics |
| Delivery | Docker Compose, GitHub Actions | simple repeatability |

## Frontend libraries — đánh giá và thời điểm

Người dùng chốt **Apache ECharts** là lựa chọn biểu đồ cho CRM; đây là quyết định công nghệ, chưa phải dependency, tính năng reporting hay benchmark đã triển khai. Các lựa chọn khác dưới đây giữ đúng work order và [frontend convention](../engineering/02-REPO-CI-CD.md#frontend).

| Thư viện | Đánh giá cho CRM | Thời điểm / trạng thái |
|---|---|---|
| TanStack Query | Quản lý server state, cache và refresh; cần scope identity/tenant/session, xử lý ETag và hủy request an toàn. | Đã tích hợp Plan 5, focused local review; tiếp tục dùng theo convention. |
| TanStack Table | Headless, phù hợp giao diện prototype; filter, sort và cursor của contact vẫn do server quyết định. | Đã tích hợp Plan 6, focused local review. |
| TanStack Router | Route và deep link có kiểu; phải bảo toàn OIDC callback, tenant/session và browser history. | Plan 7 đã hoàn tất review cục bộ; deployed-host SPA fallback chưa được xác minh. |
| TanStack Form | Phù hợp form contact/custom fields, validation và state; giữ draft khi 412, ETag và JSON number lossless. | Plan 8 Wave 2; chọn bản stable và kiểm thử regression trước khi chốt triển khai. |
| Apache ECharts | Nhiều chart và tương tác dựng sẵn; chọn làm chart library cho các dashboard/reporting của CRM. | Chỉ tích hợp khi Wave 5 cần biểu đồ để đáp ứng P0 dashboard hoặc khi bắt đầu reporting P1/P2; không đưa feature reporting mới vào trước P0 acceptance. Trước tích hợp làm mẫu dữ liệu thật, kiểm tra version/license/advisory, bundle, responsive, keyboard/screen reader và khả năng export. |
| TanStack Virtual | Giảm số phần tử render trên màn hình; không thay server pagination. | Sau khi đo thấy danh sách dài thực sự gây chậm; chưa nằm trong Plan 8/9. |
| TanStack Pacer | Debounce/throttle có ích nếu tìm kiếm hoặc validation chạy liên tục; debounce bỏ các gọi trung gian. | Chỉ đánh giá khi có use case và stable release phù hợp; không dùng thay durable queue hoặc idempotency của backend. |
| TanStack DB | Local-first collections/live queries có thể hữu ích cho offline/realtime, nhưng tăng độ phức tạp đồng bộ, xung đột và bảo vệ dữ liệu tenant. | Để sau P0; trang chính thức đánh dấu RC tại lần tra cứu 2026-11-27. Chỉ xem xét khi có use case, bản stable và review; experimental cần ADR theo AGENTS.md. |
| TanStack Start | Full-stack/SSR framework; CRM hiện dùng React/Vite và ASP.NET API, chưa có nhu cầu chuyển. | Hoãn; chỉ đánh giá nếu có yêu cầu SSR hoặc server functions cụ thể và quyết định kiến trúc. |
| TanStack Store / Hotkeys / AI | Chưa chứng minh cần dependency trực tiếp cho P0; Store được trang chính thức đánh dấu alpha tại lần tra cứu. | Đánh giá theo nhu cầu sau P0; không triển khai AI trước P0 acceptance. Thư viện experimental cần ADR. |

ECharts là **lựa chọn ưu tiên đã duyệt**, không có nghĩa đã chứng minh nhanh hơn TanStack Charts hoặc phù hợp mọi biểu đồ; thử nghiệm thực tế quyết định cấu hình. Số liệu/aggregation và phân quyền tenant phải từ backend, UI không dùng chart để áp quyền. Mỗi chart cần trạng thái loading/empty/error, mô tả dễ hiểu và cách tiếp cận giá trị bằng bảng/văn bản khi cần. Chỉ pin dependency tại lúc triển khai sau khi kiểm tra release/source, không mặc định dùng phiên bản mới nhất.

Nguồn: [TanStack Form validation](https://tanstack.com/form/latest/docs/framework/react/guides/validation), [TanStack Virtual](https://tanstack.com/virtual/latest), [TanStack Pacer](https://tanstack.com/pacer/latest/docs/guides/which-pacer-utility-should-i-choose), [TanStack DB](https://tanstack.com/db/latest), [TanStack Store](https://tanstack.com/store/latest), [Apache ECharts](https://echarts.apache.org/en/), [ECharts accessibility](https://echarts.apache.org/handbook/en/best-practices/aria/). Trạng thái thư viện có thể thay đổi; kiểm tra lại trước khi cài.

## Trial / P1
- Temporal .NET SDK for long-lived contact-centric automation; test SDK, task queues, versions and sizing.
- RabbitMQ vs NATS JetStream after throughput/operational analysis; do not run both.
- Redis cache/ratelimit when useful; must not become source of truth.
- OpenAPI 3.2.1 after generated SDK, lint and diff toolchain can parse/round-trip and test.
- OpenFeature for feature flags if release/tenant entitlement complexity warrants.
- .NET Aspire for local orchestration/observability if simpler than existing Compose.

## P2+/Hold
- ClickHouse for behavioral analytics only when PostgreSQL projections insufficient.
- OpenFGA/Cedar when permissions outgrow scoped RBAC.
- React Flow for visual workflow builder after runtime stable.
- Kubernetes + GitOps after proven operational need, not in MVP.
- AI assistants/RAG only when explicit use cases + privacy/security reviews.

## Confirmed sources
- .NET 10 LTS: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- OpenAPI 3.2.1: https://spec.openapis.org/oas/v3.2.1.html
- Temporal .NET SDK: https://docs.temporal.io/develop/dotnet
- Keycloak releases: https://www.keycloak.org/docs/latest/release_notes/

**Version policy:** pin tested release versions on project initialization, record exact patch in lockfiles/images. 'Mới nhất' không luôn là lựa chọn an toàn nhất.
