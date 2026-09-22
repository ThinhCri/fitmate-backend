# Quy trình thực thi plan production
PROD-2026-09-22-v1. Plan là hướng dẫn triển khai, không approval deploy/commit.
Các rule AGENTS.md và commit-policy vẫn áp dụng.

## 1. Dependency có milestone, không chờ toàn dự án
- BE-800 mở khóa khi baseline ownership/build/gaps được ghi; lỗi ngoài scope có owner, không bỏ qua lỗi chặn module đang làm.
- BE-801 mở khóa module khi operation/DTO/auth/errors module đó đã freeze và có fixture, không cần mọi endpoint của toàn sản phẩm đã implement.
- BE-820: dựng harness sớm sau BE-801 foundation; mỗi feature thêm tests của mình; VERIFIED toàn bộ là gate cuối, không chặn code tất cả feature trước đó.
- BE-821: chuẩn bị workflow sớm; proof full suite được cập nhật liên tục. BE-826 chờ endpoints cần dùng VERIFIED, không dùng stub thay flow.
- Các task 802..819 có thể chuẩn bị contract/unit/domain trước provider credentials; chỉ bước integration external BLOCKED.
- P1-CAPABILITY không được bật nếu chưa verified. Nếu release trước AI/VIP cần manifest explicit, không âm thầm bỏ feature.

## 2. Checklist trước code
Đọc task/plan/domain/authorization/OpenAPI và relevant handoff. Kiểm HEAD/dirty files cả repo sẽ chạm.
Tìm code/config/schema thực tế; record route vs spec gaps và planned files, không tạo file dựa tên khái niệm trong plan.
Xác nhận credentials chỉ qua secret store/env local; không in giá trị. Xác định test DB/project/volume riêng.
Nếu phát hiện conflict nghiệp vụ hoặc migration phá dữ liệu ngoài scope, nêu cụ thể; không hỏi lặp lựa chọn đã chốt.

## 3. Thiết kế trước mutation
Lập request/response/error examples, state transitions và authorization; table/index/backfill plan nếu có schema.
Xác định command idempotency, transaction boundary, concurrency guard và external provider behavior.
Mỗi provider attempt phải biết cách recovery khi gọi thành công nhưng process crash trước commit.
Đối với read model, ghi source/timezone/filter/recognition để app/admin không tự tính khác nhau.

## 4. Triển khai theo lát chức năng
Mỗi commit build được và có behavior/test hoặc migration coherent; không ép chia Domain→interface→empty service.
Chỉ stage file task mình; không tự commit, hỏi duyệt message sau diff/test. Message English<=72, giữ ID, no coauthor.
Không giữ DB transaction mở khi gọi network. Mọi outbox side effect phải atomic với mutation nguồn.
Không dùng test stub làm production adapter. Gate disabled phải truthful và fail config khi bật thiếu dependency.

## 5. Verification
Unit kiểm logic; PostgreSQL integration kiểm SQL/constraint/transaction/race; HTTP contract kiểm actual serialization/auth/routing.
Run commands theo repo toolchain thực; không ghi sẵn “X tests pass”. Missing Docker/credential là blocker evidence, không success.
Không tự cài service/mua tài nguyên/deploy production để vượt blocker; hoàn thiện phần độc lập.
Đổi endpoint phải refresh generated types/fixtures/hashes và test client adapter.
Performance tests cần dataset/environment, không lấy healthcheck200 làm evidence throughput.

## 6. Migration và rollback
Expand/backfill/verify/cutover, preserve data source và policy_version. Down rõ nhưng không default chạy Down destructive production.
Precheck counts/orphans/duplicates, backup+restore rehearsal khi rủi ro. Unknown legacy data quarantine/null có audit, không đoán.
Roll back app chỉ khi compatible schema/contract; money corrections là compensating entries, không xóa lịch sử.
Operator approval cuối trước live mutation/deploy. Feature kill switch không bỏ auth/privacy guard.

## 7. Evidence và trạng thái
TODO→IN_PROGRESS→IMPLEMENTED→VERIFIED→RELEASED theo tasks. Doc/plan tồn tại không tăng implementation status.
Viết evidence theo template, gồm failed/skipped/blockers, command exit, commit/env, files changed, rollback rehearsal và clients.
Không sao chép secret/PII vào screenshot/log artifact. Provider test refs được sanitize.
Không claim legal/Play compliance chỉ vì đọc policy; policy/source date và operator sign-off ở release gate.
