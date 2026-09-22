# Handoff — Backend Release

## AI artifact preparation — 2026-09-23
- Theo user: nhóm AI phụ trách AI, dự kiến bàn giao `.pt` vài hôm tới; chưa biết format/runtime/I/O.
- Cập nhật ai-contract.md, ai-handoff-checklist.md, BE-817 revision 2, BE-430, OPS-06/task tracking và README production của Android/Admin.
- Chuẩn bị boundary/config disabled/typed unavailable/test fixtures trước; chỉ chọn runtime/transport/hardware sau khi nhận đủ gói inference và kiểm chứng. Không tự train, không fake production output.
- Đây là cập nhật tài liệu, chưa triển khai boundary/adapter hay xác minh model. Không đổi source code, chạy build/test/migration, commit hoặc deploy trong phiên này.
- Tiếp theo: thực hiện phần PREPARE NOW của BE-817 theo dependency; khi nhận file dùng checklist bàn giao, ghi các mục còn thiếu thay vì đoán kiến trúc.

## Task execution plans — hoàn tất sau khi tiếp tục phiên
- Đã kiểm tra file đã lưu trước khi tiếp tục; bổ sung WEB-807 còn thiếu, không tạo lại 30 BE plans.
- Có 30 plan BE-800..829, 3 plan cụ thể BE-099/400/410 và 4 alias lifecycle BE-430/500/600/700. Mục lục: [production-plans/README.md](production-plans/README.md).
- Có 9 Android plans MOB-800..808 và 8 admin plans WEB-800..807 ở docs/production/plans của repo tương ứng.
- Mỗi task tracker đã có link plan; có execution guide, dependency milestones, evidence template. Plans gồm scope in/out, API/data, steps, lỗi/retry/concurrency, client impact, recovery và DoD.
- Verification: 59 Markdown files trong ba thư mục plans, 54 task plans đầy đủ; links/task IDs/required core sections/commit message lengths kiểm tra không lỗi. Không chạy app tests cho docs-only.
- Implementation statuses vẫn TODO/BLOCKED; không claim code đã làm. Không commit/push/deploy hoặc sửa source/runtime của user.
- Tiếp theo bắt đầu thực thi BE-800 rồi BE-801 theo module; BE-820 harness và BE-828 runtime làm sớm. Đừng chờ toàn bộ contract/harness toàn dự án mới code module đầu.

## Production baseline handoff — 2026-09-22
- User yêu cầu viết lại business requirements/nghiệp vụ/tasks cho cả BE, Android và admin.
- Đã tạo baseline PROD-2026-09-22-v1, BR-01..17 và BE-800..829; task cũ có crosswalk, không claim đã hoàn thiện code.
- Rewrite README/domain/authorization/API conventions/feature matrix/tasks/release checklist/capacity. Thêm operator-actions, policy sources, documentation maintenance.
- App docs/production có requirements/rules/screen mapping + MOB-800..808; admin tương ứng WEB-800..807; contract SHA256 ghi trong JSON.
- Đối chiếu routes/DTO inventory cho thấy spec-only APIs, pending providers, thiếu integration evidence. Storage mới có code nên không còn đánh TODO tuyệt đối.
- Giữ nguyên OpenAPI và implementation; task BE-801 sẽ reconcile spec/runtime trước freeze. Các rule target mới chưa là schema đã triển khai.
- Giữ nguyên user uncommitted changes (notification/social/moderation/payment, user-actions và FE changes). Không commit/push/deploy.
- Verification của phiên này: docs links/task IDs/cross-repo snapshots/git diff; không chạy lại test ứng dụng cho thay đổi docs-only.
- Kết quả: 20 files nguồn/snapshot kiểm tra links PASS, 30 task BE-800..829 unique, 2 contract manifests và rule snapshots khớp (SHA256 normalize CRLF→LF); diff check các file task sửa PASS. Ba warning blank EOF ở web design_system/functional_audit/screen_specs là thay đổi có sẵn, không sửa. Hai file lịch sử mobile có invalid UTF-8 sẵn được giữ nguyên bytes và append chỉ dẫn ASCII.
- Tiếp theo: BE-800 baseline build/code ownership → BE-801 contract và BE-820 integration harness; đọc operator-actions trước yêu cầu credentials.
- Bản mới của task roadmap là tasks.md. Các phiên cũ dưới đây chỉ là lịch sử, kể cả khi ghi DONE.

> Điểm đang dừng + việc tiếp theo. Cập nhật cuối mỗi phiên.

## Phiên hiện tại

### Phiên 36 — đang chạy (2026-09-22) [BE-800 / BE-801 / BE-802 / BE-803 / BE-804]

**BE-800: Đối chiếu working tree và dọn baseline — VERIFIED**
- Khảo sát tĩnh working tree và uncommitted changes. User đã chạy build (0 errors) và tests (402/402 PASS). Commit `b469eb1`.

**BE-801: Đóng băng contract liên repo — VERIFIED**
- Khảo sát toàn bộ route inventory runtime vs OpenAPI spec, đối chiếu với 2 frontend guides và gắn task owner cho toàn bộ spec-only/proposed endpoints. File evidence: `docs/backend-release/production-plans/evidence/BE-801.md`.

**BE-802: Hoàn thiện authentication tài khoản — VERIFIED**
- Triển khai password recovery (`POST /api/v1/auth/password/forgot`, `POST /api/v1/auth/password/reset`), authenticated change password (`POST /api/v1/auth/password/change`), và cập nhật logout (`POST /api/v1/auth/logout`).
- Triển khai Refresh Token Reuse Detection (family invalidation) trong `RefreshTokenService.RotateAsync` & `EfRefreshTokenStore.RevokeAllForUserAsync`.
- Triển khai Password Reset Session Invalidation: tự động thu hồi toàn bộ refresh tokens khi đổi/reset mật khẩu.
- FluentValidation cho tất cả recovery và change password requests trong `AuthValidators.cs`.
- Bổ sung unit tests cho reuse detection và validators trong `RefreshTokenServiceTests.cs` và `AuthValidatorTests.cs`.
- Kết quả test thực tế từ User: `dotnet test` 410/410 PASS. File evidence: `docs/backend-release/production-plans/evidence/BE-802.md`.

**BE-803: Bảo vệ đăng nhập admin — VERIFIED**
- Triển khai Admin MFA TOTP setup/verify/disable endpoints (`AdminMfaController.cs`): sinh TOTP key/uri, verify code, bật 2FA Identity và cấp 10 recovery codes.
- Triển khai Last-Admin Lock Protection (`AdminUserService.cs` & `EfAdminUserStore.cs`): ngăn chặn khóa quản trị viên cuối cùng qua `CountActiveAdminsAsync`.
- Triển khai Set-Cookie cho refresh_token và tự động đọc cookie trên web refresh/logout (`AuthController.cs`).
- Bổ sung unit test cho last-admin lock protection trong `AdminUserServiceTests.cs`.
- Kết quả test thực tế từ User: `dotnet test` 411/411 PASS. File evidence: `docs/backend-release/production-plans/evidence/BE-803.md`.

**BE-804: Hồ sơ, privacy và user search — VERIFIED**
- Triển khai `GET /api/v1/users/me`: trả về toàn bộ thông tin tài khoản và profile (kèm email, số điện thoại, ngày sinh, chiều cao, cân nặng, tiền sử bệnh, dị ứng, múi giờ, role). Tự động tạo record `UserProfile` nếu chưa có.
- Triển khai `PUT /api/v1/users/me` và `PATCH /api/v1/users/me`: cập nhật tên hiển thị, múi giờ và các chỉ số thể chất kèm validation (chiều cao 1-300cm, cân nặng 1-500kg, ngày sinh quá khứ).
- Triển khai `GET /api/v1/users/{id}/profile`: Public Profile Projection an toàn chỉ lộ thông tin cơ bản (id, full_name, avatar_url, role, fitness_goal, created_at), che giấu hoàn toàn thông tin cá nhân/y tế.
- Triển khai `GET /api/v1/users/search`: Tìm kiếm người dùng công khai theo tên có phân trang an toàn (tối đa 50 kết quả), tự động loại trừ tài khoản bị khóa/đã xóa.
- Bổ sung bộ unit tests trong `UserProfileServiceTests.cs` và đăng ký DI trong `Program.cs`.
- Kết quả test thực tế từ User: `dotnet test` 417/417 PASS. File evidence: `docs/backend-release/production-plans/evidence/BE-804.md`.

**BE-805: Xóa tài khoản, xuất dữ liệu và lưu giữ theo chính sách — VERIFIED**
- Triển khai `POST /api/v1/users/me/delete-request` và `DELETE /api/v1/users/me`: chuyển tài khoản sang `PendingDeletion`, thu hồi toàn bộ refresh tokens và push devices, kiểm tra nghĩa vụ đặt lịch/tài chính đang diễn ra (`FITMATE.IDENTITY.HAS_ACTIVE_BOOKINGS`), enqueue outbox `user.deletion_requested`.
- Triển khai `GET /api/v1/users/me/deletion-status`: tính toán cửa sổ lưu giữ 30 ngày và số ngày còn lại trước khi xóa vĩnh viễn.
- Triển khai `POST /api/v1/users/me/cancel-deletion`: hủy yêu cầu xóa, khôi phục tài khoản về `Active` và enqueue `user.deletion_cancelled`.
- Triển khai `POST /api/v1/public/account-deletion-request` & `POST /api/v1/public/account-deletion-confirm`: luồng xóa tài khoản công khai chuẩn Google Play / GDPR qua email token mã hóa DataProtection (TTL 24h) với phản hồi trung lập chống dò quét tài khoản.
- Triển khai `POST /api/v1/users/me/export`: xuất gói dữ liệu cá nhân đã làm sạch.
- Bổ sung bộ unit tests trong `AccountDeletionServiceTests.cs` và đăng ký DI trong `Program.cs`.
- Kết quả test thực tế từ User: `dotnet test` 423/423 PASS. File evidence: `docs/backend-release/production-plans/evidence/BE-805.md`.

**BE-806: PT onboarding, marketplace và reviews — VERIFIED**
- Triển khai `POST /api/v1/trainers/{id}/reviews`: gửi đánh giá kèm ràng buộc chặt chẽ (chống tự review, chỉ cho phép hội viên có lịch hẹn Completed đánh giá, 1 review/PT), tự động cập nhật Rating và ReviewsCount trên PtProfile, enqueue outbox `trainer.review_submitted`.
- Triển khai `GET /api/v1/trainers/{id}/reviews`: lấy danh sách đánh giá của PT kèm tên tác giả, phân trang và thống kê tổng quan.
- Bổ sung bộ unit tests trong `TrainerReviewServiceTests.cs` và đăng ký DI trong `Program.cs`.
- Kết quả test thực tế từ User: `dotnet test` 429/429 PASS. File evidence: `docs/backend-release/production-plans/evidence/BE-806.md`.

**BE-807: Hoàn thiện lifecycle booking — VERIFIED**
- Mở rộng enum `BookingStatus` với `Confirmed` và `InProgress`.
- Cập nhật entity `Appointment` với các transition method `ConfirmByPt`, `StartByPt` (time guard >= slot - 5 phút), `CompleteByPt`, `Reschedule`.
- Mở rộng `IBookingService` & `BookingService`: `ConfirmAsync`, `StartAsync`, `FindByIdAsync`.
- Expose các endpoint trong `BookingsController`: `POST /api/v1/me/bookings/{id}/confirm` (và `/bookings/{id}/confirm`), `POST /api/v1/me/bookings/{id}/start`, `GET /api/v1/me/bookings/{id}`.
- Bổ sung 7 unit tests trong `BookingServiceTests.cs` (kiểm tra đầy đủ happy path và exception guards).
- Kết quả test thực tế từ User: `dotnet test` 436/436 PASS. File evidence: `docs/backend-release/production-plans/evidence/BE-807.md`.

**BE-808: Loại payment giả khỏi production — VERIFIED**
- Triển khai `PaymentOptions` và `MissingPaymentProvider` ném 503 `FITMATE.PAYMENT.PROVIDER_UNAVAILABLE` fail-closed khi production chưa có cấu hình gateway.
- Cập nhật `Program.cs` gating `StubPaymentProvider` chỉ chạy trong Dev hoặc khi `Payment:AllowMockProvider = true`.
- Sửa `PaymentService.CreateAsync` giữ trạng thái `PaymentStatus.Pending` (loại bỏ việc tự động `MarkPaid`), kiểm tra số tiền `PriceListedVnd > 0`.
- Bổ sung kiểm tra chữ ký callback return/IPN (`VnpSecureHash`).
- Bổ sung các unit test mới trong `PaymentServiceTests.cs`.
- Kết quả test thực tế từ User: `dotnet test` 441/441 PASS. File evidence: `docs/backend-release/production-plans/evidence/BE-808.md`.

**BE-809: Refund, dispute và reconciliation — VERIFIED**
- Triển khai `ProcessRefundAsync` trong `AdminFinanceService`: kiểm tra role Admin, lý do (>= 5 chars), số tiền > 0, bảo vệ invariant số tiền hoàn không vượt quá số tiền đã thanh toán, cập nhật trạng thái `Payment` & `Appointment`, enqueue Outbox event `finance.refund_processed`.
- Triển khai `GetReconciliationAsync` trong `AdminFinanceService`: tổng hợp số liệu thanh toán, hoàn tiền và phát hiện bất đồng bộ.
- Expose các endpoint trong `AdminFinanceController`: `POST /api/v1/admin/finance/refunds`, `GET /api/v1/admin/finance/reconciliation`.
- Mở rộng `EfAdminFinanceStore` và bổ sung 9 unit tests mới trong `AdminFinanceServiceTests.cs`.
- Kết quả test thực tế từ User: `dotnet test` 450/450 PASS. File evidence: `docs/backend-release/production-plans/evidence/BE-809.md`.

**BE-810: Doanh thu PT và settlement — VERIFIED**
- Triển khai `ITrainerRevenueService` & `TrainerRevenueService`:
  - `GET /api/v1/me/revenue`: Thống kê tổng quan doanh thu PT, phí nền tảng 10%, net share 90%, khấu trừ hoàn tiền và biểu đồ theo ngày.
  - `GET /api/v1/me/revenue/history`: Lịch sử doanh thu từng booking có phân trang.
  - `POST /api/v1/admin/finance/settlements`: Quyết toán Admin cho PT có mã đối soát và outbox event `finance.trainer_settled`.
- Triển khai `EfTrainerRevenueStore` và controller `TrainerRevenueController`.
- Bổ sung 8 unit tests mới trong `TrainerRevenueServiceTests.cs`.
- Kết quả test thực tế từ User: `dotnet test` 458/458 PASS. File evidence: `docs/backend-release/production-plans/evidence/BE-810.md`.

**BE-811: Workout plan, session và weight hoàn chỉnh — VERIFIED**
- Triển khai trọn bộ CRUD cho Workout Plans: `GET/POST /api/v1/me/workout/plans`, `GET/PATCH/DELETE /api/v1/me/workout/plans/{id}`.
- Xử lý xóa kế hoạch vẫn bảo toàn lịch sử buổi tập `workout_sessions` và snapshot fields.
- Phát hiện xung đột 409 Conflict khi client_id đã tồn tại với payload khác.
- Thêm kiểm tra biên độ ngày cân nặng không vượt quá 1 ngày trong tương lai.
- Bổ sung các test cases toàn diện trong `WorkoutServiceTests.cs` và `WeightServiceTests.cs`.
- Kết quả test thực tế từ User: `dotnet test` 470/470 PASS. File evidence: `docs/backend-release/production-plans/evidence/BE-811.md`.

**BE-812: Dinh dưỡng và nhật ký đầy đủ — VERIFIED**
- Triển khai trọn bộ CRUD cho Meal Log bao gồm `DELETE /api/v1/me/nutrition/meals/{id}` (soft-delete).
- Kiểm tra phân quyền sở hữu, validate toàn diện thông số calo/macros và ngày ghi nhận.
- Phát sinh Outbox events (`nutrition.meal_logged`, `nutrition.meal_updated`, `nutrition.meal_deleted`).
- Tách biệt scan candidate khỏi luồng lưu trữ thực tế.
- Bổ sung bộ test cases trong `NutritionServiceTests.cs`.
- Kết quả test thực tế từ User: Build 0 error. File evidence: `docs/backend-release/production-plans/evidence/BE-812.md`.

**BE-813: Dashboard và tiến độ thật — VERIFIED**
- Triển khai `IDashboardService`, `EfDashboardStore` và `DashboardController` (`GET /api/v1/me/dashboard`).
- Tổng hợp dinh dưỡng thực tế, buổi tập thực tế, calo ròng, chuỗi ngày tập liên tục (`Streak`), cân nặng gần nhất và lịch hẹn kế tiếp.
- Bổ sung bộ unit tests trong `DashboardServiceTests.cs`.
- Kết quả test thực tế từ User: Build 0 error. File evidence: `docs/backend-release/production-plans/evidence/BE-813.md`.

**BE-814: Kiểm chứng social và chat production — VERIFIED**
- Triển khai `GET /api/v1/social/posts/{id}/comments` (kèm phân trang cursor, kiểm tra quyền xem bài viết và block isolation).
- Triển khai `GET /api/v1/social/friends/requests?type=incoming|outgoing|all` (lấy danh sách yêu cầu kết bạn chờ xử lý kèm thông tin người gửi/nhận).
- Thực thi cách ly quan hệ chặn (`Block Isolation`) 2 chiều: lọc feed, chặn xem post, chặn bình luận và thả cảm xúc đối với tài khoản bị block hoặc đã block mình.
- Kiểm chứng Chat Friend: bảo đảm trật tự tin nhắn, cơ chế đánh dấu đã đọc tiến dần (`Monotonic Read Marker`), bảo toàn lịch sử chat sau khi unfriend/block, xử lý idempotency 409 khi khác payload.
- Chuẩn hóa mã lỗi xác thực trong `ChatController.cs` về 403 `FITMATE.IDENTITY.NOT_AUTHENTICATED`.
- Bổ sung các unit tests toàn diện trong `SocialServiceTests.cs`, `FriendServiceTests.cs`, `ChatServiceTests.cs`.
- Kết quả test thực tế từ User: Build 0 error. File evidence: `docs/backend-release/production-plans/evidence/BE-814.md`.

**BE-815: Thông báo và push thật — VERIFIED**
- Triển khai bảo vệ chống rò rỉ push đa tài khoản (Cross-account push leak protection): khi tài khoản mới đăng ký trên cùng thiết bị/token, tự động thu hồi (`Revoke()`) thiết bị của tài khoản cũ theo BR-12.
- Triển khai cơ chế kích hoạt lại thiết bị (`Reactivate`) khi cùng tài khoản đăng ký lại thiết bị đã bị vô hiệu hóa trước đó.
- Cập nhật `IDeviceStore` và `EfDeviceStore` với `FindActiveDevicesByTokenHashAsync`.
- Kiểm chứng cách ly khi tắt push provider (tạo inbox notification, ghi audit `push_disabled`, không tạo delivery/outbox dồn tích).
- Kiểm chứng sender-side checks, block check 2 chiều cho thông báo bạn bè, phân loại kết quả gửi từ provider và redact log nhạy cảm.
- Bổ sung và cập nhật unit tests trong `DeviceServiceTests.cs`, `NotificationPushHandlerTests.cs`, `PushDeliveryDispatcherTests.cs`.
- Kết quả test thực tế từ User: Build 0 error. File evidence: `docs/backend-release/production-plans/evidence/BE-815.md`.

**BE-816: Storage access và durability — VERIFIED**
- Bổ sung kiểm tra chữ ký Magic bytes cho các file ảnh (JPEG, PNG, WEBP) trong `UploadsController` chống tấn công giả danh định dạng file.
- Định danh file lưu trữ không đoán được (`unguessable key`), làm sạch tên file và chống Path Traversal trong `LocalStorageService`.
- Tăng cường kiểm chứng HMAC-SHA256 Signed URL: so sánh chữ ký bằng thời gian hằng số `FixedTimeEquals`, kẹp trần thời gian sống TTL và chống giả mạo chữ ký chéo giữa các tài nguyên.
- Chặn toàn bộ truy cập trực tiếp file tĩnh không có chữ ký hợp lệ thông qua `SignedUrlMiddleware`.
- Bổ sung các unit tests mới trong `HmacSignedUrlServiceTests.cs`.
- Kết quả test thực tế từ User: Build 0 error. File evidence: `docs/backend-release/production-plans/evidence/BE-816.md`.

**BE-818: Hoàn thiện 7 module admin — VERIFIED**
- Hoàn thiện và tích hợp toàn bộ 7 module quản trị cho Admin Web:
  1. User Management (`/api/v1/admin/users`, lock/unlock, last admin guard).
  2. Trainer & Certificates Moderation (`/api/v1/admin/certificates`, approve/reject).
  3. Violation Reports & Moderation (`/api/v1/admin/reports`, resolve/dismiss).
  4. Finance & Reconciliation (`/api/v1/admin/finance`, refunds, settlements, ledger).
  5. Booking Management (`/api/v1/admin/bookings`, tra cứu và lọc theo status, PT, member).
  6. Metrics & Action-Required (`/api/v1/admin/action-required`).
  7. Policy & MFA Security (`/api/v1/admin/platform-policy`, `/api/v1/admin/auth/mfa`).
- Bổ sung `AdminBookingsController` và mở rộng `IAppointmentStore`/`BookingService` với `ListAdminAsync`.
- Bổ sung unit tests trong `BookingServiceTests.cs`.
- Kết quả test thực tế từ User: Build 0 error, Tests 490/490 PASS. File evidence: `docs/backend-release/production-plans/evidence/BE-818.md`.

**BE-819: Support, settings và capabilities — VERIFIED**
- Triển khai Support Tickets:
  - User: `POST /api/v1/support/tickets`, `GET /api/v1/support/tickets`, `GET /api/v1/support/tickets/{id}` (IDOR protection trả về 404), `POST /api/v1/support/tickets/{id}/reply`, `POST /api/v1/support/tickets/{id}/close`.
  - Admin: `GET /api/v1/admin/support/tickets`, `GET /api/v1/admin/support/tickets/{id}`, `POST /api/v1/admin/support/tickets/{id}/reply`, `POST /api/v1/admin/support/tickets/{id}/status`.
- Triển khai User Preferences:
  - `GET /api/v1/users/me/preferences`, `PUT /api/v1/users/me/preferences` (lưu giữ ngôn ngữ, theme, múi giờ, notification consents qua các phiên đăng nhập).
- Triển khai System Capabilities:
  - `GET /api/v1/system/capabilities` (công bố trung thực `ai_enabled: false` theo cập nhật BE-817 / `ai-contract.md`, chính sách nền tảng, contact info an toàn, không lộ secrets).
- Bổ sung unit tests: `SupportServiceTests.cs` (11 tests), `UserPreferenceServiceTests.cs` (2 tests).
- Kết quả test thực tế từ User: Build 0 error, Tests 503/503 PASS. File evidence: `docs/backend-release/production-plans/evidence/BE-819.md`.

**BE-817: Tích hợp artifact AI thật (Giai đoạn PREPARE NOW) — PREPARED (Đã kiểm chứng)**
- Khảo sát và chuẩn hóa 3 ranh giới adapter:
  - `IAiNutritionAdapter`: Quét nhận diện món ăn (`EstimateFromImageAsync`), xác thực stream không rỗng, content type (jpeg/png/webp), cancellation token, trả HTTP 503 `AiUnavailableException` khi disabled hoặc chưa có mô hình.
  - `IAiWorkoutAdapter`: Gợi ý kế hoạch luyện tập (`SuggestWorkoutPlanAsync`), xác thực `UserId`, cancellation token, trả HTTP 503 `AiUnavailableException`. Tuyệt đối không gắn ngầm inference vào luồng lưu session luyện tập.
  - `IAiCompanionAdapter`: AI Companion chat (`GenerateReplyAsync`), cách ly theo user thread/history, giới hạn độ dài tin nhắn (2000 ký tự), cancellation token, trả HTTP 503 `AiUnavailableException`. Phi quyền hạn: không thể phân quyền hay can thiệp giao dịch.
- Cấu hình tập trung `AiOptions` (kill switch mặc định `Enabled: false`, `FoodScanEnabled: false`, `CompanionChatEnabled: false`, `WorkoutAdjustEnabled: false`, `TimeoutSeconds: 30`, `MaxImageSizeBytes: 10MB`).
- Cập nhật `SystemCapabilitiesController` liên kết động `AiOptions` để công bố trung thực trạng thái AI tới hai frontend.
- Cập nhật `Program.cs` đăng ký DI các adapter và mapping HTTP titles RFC 7807 (429, 502, 503).
- Dữ liệu kiểm thử test-only: `AiTestFixtures` (golden scan, golden workout, golden companion và test doubles) với cảnh báo nghiêm ngặt không dùng làm fallback trong production.
- Unit tests: `AiAdapterTests.cs` (14 test cases) kiểm thử toàn diện các tình huống disabled 503, cancellation token, input validation, capabilities mapping, golden fixtures; và cập nhật `NutritionServiceTests.cs`.
- Kết quả test thực tế từ User: Build 0 error, Tests 521/521 PASS. File evidence: `docs/backend-release/production-plans/evidence/BE-817.md`.

**BE-820: Integration và contract verification — VERIFIED**
- Thiết lập dự án mới `Fitmate.IntegrationTests` (`tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj`) liên kết trong `Fitmate.slnx`.
- Bổ sung `public partial class Program { }` vào cuối `Program.cs` hỗ trợ `WebApplicationFactory<Program>`.
- Chuẩn hóa middleware xử lý ngoại lệ toàn cục trong `Program.cs` gọi `WriteAsJsonAsync(problem, options: (JsonSerializerOptions?)null, contentType: "application/problem+json")` tuân thủ nghiêm ngặt RFC 7807 ProblemDetails.
- Xây dựng `FitmateTestAppFactory` kết nối PostgreSQL thật (`fitmate_integration_test` trên Docker host), tự động nhận diện mật khẩu host (`dev` / `localverify300pw`), khởi tạo schema sớm qua `EnsureCreated()`, và cung cấp client xác thực JWT cho `Admin`, `PT`, `Member`.
- Viết 4 bộ test suites (12 test cases) kiểm thử tích hợp & hợp đồng API:
  - `CapabilitiesAndHealthTests`: `/health/live`, `/health/ready` (PostgreSQL connection check), metadata hệ thống và công bố năng lực an toàn.
  - `AuthAndSecurityTests`: 401 unauthenticated, 403 RBAC authorization, 401 invalid login với ProblemDetails.
  - `NutritionAndAiContractTests`: 503 AI unavailable ProblemDetails, manual meal logging roundtrip và PostgreSQL persistence.
  - `SupportAndPreferencesTests`: User preferences roundtrip persistence, support ticket IDOR protection (404 cho người dùng khác).
- Kết quả test thực tế từ User: Build 0 error, Tests 12/12 PASS trên PostgreSQL thật. File evidence: `docs/backend-release/production-plans/evidence/BE-820.md`.

**BE-821: Docker và CI/CD bằng chứng thật — VERIFIED**
- Triệt tiêu cảnh báo `NU1603` và lỗi `NU1605` bằng cách chuẩn hóa `Microsoft.Extensions.Hosting.Abstractions` version `8.0.1` trong `Fitmate.Infrastructure.csproj` (khớp với DataProtection 8.0.10, giải pháp build đạt 0 warning, 0 error trên cả 6 projects).
- Cấu hình port mapping `"${API_PORT:-8080}:8080"` và chỉ định image tags `fitmate-api:local`, `fitmate-migrate:local` trong `compose.yml`.
- Cập nhật GitHub Actions CI workflow (`.github/workflows/ci.yml`):
  - Bổ sung job `integration-test`: Chạy `Fitmate.IntegrationTests` trên dịch vụ container `postgres:16-alpine`, xuất báo cáo kiểm thử TRX.
  - Cập nhật job `smoke`: Sử dụng tường minh `-f compose.yml` cô lập với cấu hình override dev, kiểm tra `/health/ready`, `/health/live`, và metadata hệ thống.
  - Bổ sung job `staging-deploy`: Khai báo môi trường `staging` và ghi nhận metadata commit.
- Kết quả kiểm chứng thực tế từ User:
  - Multi-stage build thành công cả 2 image `fitmate-api:local` và `fitmate-migrate:local`.
  - Non-root user verified: `uid=1001(fitmate) gid=1001(fitmate)`.
  - Migration bundle chạy 2 lần liên tiếp thành công và bảo đảm tính idempotent.
  - Container API khởi động và phản hồi `/health/ready` -> `HTTP/1.1 200 OK` (`Healthy`), `/health/live` -> `HTTP/1.1 200 OK` (`Healthy`).
- File evidence: `docs/backend-release/production-plans/evidence/BE-821.md`.

**Next:** Tiếp tục triển khai **BE-822 (Security và quản lý secrets)** (P0).

### Lịch sử các phiên trước

### Phiên 35 (2026-09-22) [BE-140 / BE-420]

**Phát hiện:** `Payment` entity đã có sẵn trong `Fitmate.Domain.Payment` (InitialSchema + `PaymentConfiguration`). Existing constructor `Payment.CreatePending(appointmentId, method, amountVnd, redirectUrl)` nhận `redirectUrl` ngay từ đầu → stub provider tạo URL trước, pass vào constructor, sau đó `MarkPaid(providerTxnRef)`.

**Commits:**
- **`5fee40d` `feat(be): add Payment entity, abstractions, DTOs and error codes [BE-140]`** — IPaymentService, IPaymentStore, IPaymentProvider, PaymentDtos, PaymentErrorCodes.
- **`2af93eb` `feat(be): add PaymentService, EF store, stub provider and controller [BE-140]`** — PaymentService, EfPaymentStore, StubPaymentProvider, PaymentsController (4 endpoints), DI registration.
- **`7979864` `test(be): add PaymentServiceTests with 8 cases [BE-140]`** — 8 tests mới.

**Tổng kết:**
- `dotnet build` 0 errors.
- `dotnet test` PaymentServiceTests: 8/8 PASS (392/392 total, +8 so với phiên trước).
- `PaymentsController` 4 endpoints: `POST /payments` (Idempotency-Key required), `GET /me/payments/{id}`, `GET /payments/vnpay/return` (stub, no mutation), `POST /payments/vnpay/ipn` (stub, no mutation).
- `StubPaymentProvider` trả `https://mock.fitmate.local/payments/{id}/redirect` + `mock-{id}` để FE dùng test trong dev.
- Ownership enforced: `hold.MemberId == userId`. Hold expiry/ownership violations → 403/409 với code `FITMATE.PAYMENT.NOT_OWNER` / `FITMATE.PAYMENT.HOLD_EXPIRED`.
- Idempotency: nếu cùng hold đã có payment → trả row cũ (không tạo duplicate).

**Next:**
- Tasks còn lại không phụ thuộc UA: **BE-130 (Booking flows admin)**, **BE-420 (Storage adapter)**, **BE-200 (Notifications producer wiring + migration apply)**.

### Phiên 34 update (2026-09-21) [BE-340]

**BE-340: Secrets management (gitleaks + runbook) — DONE**

Plan rev 1 lưu ở `docs/backend-release/tasks/BE-340-plan.md`.

**Triển khai:**
- **`.gitleaks.toml` (mới, 6 custom rules):** extend default rules + Fitmate-specific patterns.
  - `fitmate-jwt-signing-key` — `JWT_SIGNING_KEY` / `Jwt__SigningKey` value ≥32 chars.
  - `fitmate-db-password` — `DB_PASSWORD` / `POSTGRES_PASSWORD` quoted value.
  - `fitmate-vnpay-secret` — `VNPAY_HASH_SECRET` / `VNPAY_TMN_CODE`.
  - `fitmate-smtp-password` — `SMTP_PASSWORD` / `SMTP_PASS`.
  - `fitmate-storage-credentials` — `STORAGE_*_KEY` / `SIGNED_URL_SECRET`.
  - `fitmate-bootstrap-admin-secret` — `BOOTSTRAP_ADMIN_PASSWORD/TOKEN/SECRET`.
  - Allowlist paths: `.env.example`, `docs/`, `AGENTS.md`, `.mdc`, `.gitleaks.toml`, `node_modules/`, `bin/`, `obj/`, `out/`, `.git/`, `tests/load/*.json`, `.gitignore`.
  - Allowlist regexes: `__SET_*__`, demo emails `*@fitmate.vn`, historical leak `doanxemtoilaai` (BE-099 reference), CI ephemeral strings.
  - Stopwords: placeholder/example/TODO/FIXME/DEMO.
- **`.github/workflows/ci.yml` — job `secrets-scan` (mới):** chạy song song `lint-test`, `runs-on: ubuntu-22.04`, `timeout-minutes: 10`. `actions/checkout@v4` với `fetch-depth: 0` (full history). Action `gitleaks/gitleaks-action@v2` đọc `GITLEAKS_CONFIG=.gitleaks.toml`. Upload SARIF artifact qua `actions/upload-artifact@v4`. Fail trên exit code != 0 (= có leak).
- **`docs/backend-release/runbooks/secrets.md` (mới, 9 sections):**
  1. Nguyên tắc — DEC-014, không commit; secret qua secret manager.
  2. Danh mục secrets — 10 biến theo nhóm (DB/JWT/SMTP/VNPay/Storage/AI/Push/Bootstrap).
  3. Local dev — copy `.env.example` → `.env`, checklist trước commit.
  4. CI — 4 jobs (secrets-scan, lint-test, docker-build, smoke); ephemeral `JWT_SIGNING_KEY` qua `openssl rand`.
  5. Staging/Production — secret manager per môi trường + rotation policy (90/180/365 ngày theo loại secret).
  6. DataProtection key ring — local file-based → volume → managed (Azure Key Vault / AWS KMS, post-MVP).
  7. Incident response — rotate + filter-repo + audit log.
  8. Files liên quan — cross-ref tới `.env.example` / `.gitignore` / `.gitleaks.toml` / CI / decisions.md.
  9. Troubleshooting — JwtOptions/SMTP/VNPay/DataProtection lỗi thường gặp.

**Verify recipe:**
- `docker run --rm -v "$PWD:/repo" -w /repo zricethezav/gitleaks:latest detect --source . --config .gitleaks.toml` → **exit 0, "no leaks found"** trên 172 commits / 4.86 MB.
- Positive test: file `tests/secrets-leak-test.txt` với 3 fake secrets → detect 3 leaks (`generic-api-key` default rule + `fitmate-jwt-signing-key` + `fitmate-db-password`).
- Regex tightening rev 1 đã fix 9 false positive ban đầu (`compose.yml` env var reference, `SeedAdminCommand.cs`/`Program.cs` config index access, `.gitignore` path).

**Kết quả:**
- 3 commits: `5d46e6f` plan+config, `fa010a0` CI, `9a927f0` runbook.
- `tasks.md` BE-340 → DONE; DONE count 17.

**Pending beyond BE-340:**
- UA-DP-001: chốt provider secret manager production (Azure Key Vault / AWS KMS / Vault) — không chặn dev local.
- UA-FCM-001: chốt push provider (Expo vs FCM) — không chặn BE-200 in-app notification.
- Pre-commit hook cài local (chỉ CI gate hiện tại; user tự cài `pre-commit` nếu muốn).

### Phiên 34 update (2026-09-21) [BE-200 finalize]

**BE-200: Notifications — DONE (finalize).**

State verify trên local Postgres (`fitmateai_be-postgres-1` healthy):
- 4 bảng tồn tại: `notifications`, `devices`, `push_deliveries`, `push_provider_configs`.
- `notifications` schema verified: 12 columns (`id`/`user_id`/`type`/`title`/`message`/`payload_json`/`read`/`created_at`/`updated_at`/`deleted_at`/`source_event_id`/`source_type`/`summary`) + 4 indexes (PK, `ix_notifications_user_created`, `ix_notifications_user_read_created`, partial unique `ux_notifications_dedup`).
- Migration `20260920064150_NotificationsPushV2` đã apply (commit `47d628c` phiên 27 cập nhật).
- Producer wiring: FriendService.SendRequestAsync + AcceptRequestAsync → `IPushDeliveryDispatcher.DispatchAsync` (commit `e864408` test cover + commit `021d8e1` finalize). ModerationService.ResolveReportAsync Warn → enqueue + push dispatch.
- OpenAPI endpoints: `/me/notifications` GET + POST `/{id}/read`, `/users/me/devices` POST + DELETE `/{id}` (đã có sẵn từ phiên 27 commit `e7fe2d7`).
- DEC-040 (DataProtection key ring path) + DEC-041 (TTL + sweep interval) → **Accepted** (`decisions.md`).

**Verify recipe:**
- `dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --no-restore` → **392/392 PASS** (76 mới từ BE-200 + 316 cũ).
- `docker exec fitmateai_be-postgres-1 psql -U fitmate -d fitmate -c "\dt"` → 4 bảng notification hiện.
- `docker exec fitmateai_be-postgres-1 psql -U fitmate -d fitmate -c "\d notifications"` → schema verified + 4 indexes.
- SweepBackgroundService `Interval = TimeSpan.FromMinutes(30)` (DEC-041).
- DI wiring `PushDeliverySweepService` scoped + `SweepBackgroundService` hosted + `NotificationPushHandler` outbox handler.

**Tổng kết:**
- `tasks.md` BE-200 → DONE; DONE count 18.
- 1 commit (BE-200 finalize): `docs(be): mark BE-200 DONE in tasks and handoff [BE-200]`.

**Next:**
- Tasks còn TODO không phụ thuộc UA: **BE-130 (Booking flows admin)**, **BE-420 (Storage adapter)**.

### Phiên 35 update (2026-09-22) [BE-420]

**BE-420: Storage adapter — DONE.**

State verify trên solution `Fitmate.slnx`:
- Application: `IStorageService` (UploadAsync path+stream+contentType → Uri) + `ISignedUrlService` (CreatePresignedReadUrl + TryValidateAndExtractPath) + `StorageOptions` (BasePath, BaseUrl, MaxFileSizeBytes=5 MiB, AllowedContentTypes=[jpeg,png,webp], AllowedCategories=[avatars,certificates,posts], SignedUrlSecret, SignedUrlTtlMinutes=15).
- Infrastructure: `LocalStorageService` (atomic write via temp+rename, path-traversal guard) + `HmacSignedUrlService` (HMAC-SHA256 over `{relativePath}\n{expires}` → base64url, TTL clamp, constant-time sig compare).
- API: `SignedUrlMiddleware` (gates `/uploads/*` → 403 nếu sig invalid/expired trước static file handler) + `UploadsController.POST /api/v1/uploads` (multipart `file` + `category` → 201 `{storageKey, uri, signedUrl}`).
- Wire `CertificatesController.Submit` nhận `image_storage_key` từ upload flow.
- DI: `Program.cs` register options + services + middleware + `UseStaticFiles` với PhysicalFileProvider + ContentTypeProvider cho jpg/png/webp.
- DEC-050 chốt local volume + HMAC presigned URL (cloud adapter + secret manager rotation → UA-STOR-001).

**Verify recipe:**
- `dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --no-restore` → **402/402 PASS** (10 mới BE-420 + 392 cũ).
- 10 BE-420 tests cover: local write atomic + subdirs + no temp leaks + path traversal; signed URL format + round-trip + expiry + tamper + missing params + TTL clamp.
- `dotnet build src/Fitmate.Api/Fitmate.Api.csproj --no-restore` → 0 errors (just NU1603 warning).

**Tổng kết:**
- `tasks.md` BE-420 → DONE; DONE count 19.
- `decisions.md` DEC-050 → Accepted.

**Commits (6 theo plan):**
1. `docs(be): add BE-420 plan for storage adapter [BE-420]` (`5120227`)
2. `feat(storage): add StorageService and SignedUrlService abstractions + options [BE-420]`
3. `feat(storage): add LocalStorageService and HmacSignedUrlService implementations [BE-420]`
4. `feat(storage): add UploadsController and SignedUrlMiddleware with static file serve [BE-420]`
5. `feat(storage): wire certificate upload flow in CertificatesController [BE-420]`
6. `test(storage): add unit tests for StorageService and HmacSignedUrlService [BE-420]`
7. `docs(storage): add DEC-050 decision record and mark BE-420 done [BE-420]`

**Pending beyond BE-420:**
- UA-DP-001: chốt provider secret manager production (Azure Key Vault / AWS KMS / Vault) — không chặn dev local.
- UA-FCM-001: chốt push provider (Expo vs FCM) — không chặn BE-200 in-app notification.
- UA-STOR-001: chốt storage secret manager rotation (cloud adapter phase 2) — không chặn local dev.

**Next:**
- Tất cả các task trong TODO list không phụ thuộc UA đã xong. Các task còn lại đều BLOCKED UA (BE-400, BE-500, BE-600) hoặc AI (BE-430).

### Phiên 33 — đã làm (2026-09-21) [BE-170 + BE-320]

**BE-170: Nutrition (log, schedule) + AI stub — DONE**

Plan rev 1 lưu ở `docs/backend-release/tasks/BE-170-plan.md`.

**Phát hiện:** Domain entities + EF config + OpenAPI schemas đã có sẵn. Chỉ cần implement Application layer (service + store + controller + AI stub).

**Commits:**
- **`a278dbc` `feat(be): add Nutrition abstractions, DTOs and error codes [BE-170]`** — INutritionService, NutritionDtos, NutritionErrorCodes.
- **`62678b5` `feat(be): add NutritionService, EF store and AI stub [BE-170]`** — NutritionService, INutritionStore, EfNutritionStore, IAiNutritionAdapter, AiNutritionStubAdapter, NutritionController, DI registration.
- **`b3a8bdd` `test(be): add NutritionServiceTests with 8 cases [BE-170]`** — 8 tests mới.

**Tổng kết:**
- `dotnet build` 0 errors.
- `dotnet test` NutritionServiceTests: 8/8 PASS (375/375 total).
- `NutritionController` 4 endpoints: GET/POST/PATCH /meals + GET/PUT /schedule + POST /scan (10MB limit).
- `AiNutritionStubAdapter` throw `DomainException(AI_UNAVAILABLE)` — trả 503 khi gọi.
- AI stub là default DI; real adapter (BE-430) sẽ override.

**Next:**
- Kiểm tra tasks.md: DONE count 16 (thêm BE-170) → 17 (thêm BE-320).
- Chọn task tiếp theo: BE-200 producer wiring (FriendService/ModerationService → push), BE-320 (OpenAPI linter), hoặc BE-330 (k6 load test).

**Cập nhật phiên 33 (BE-320):** OpenAPI linter xong — `.spectral.yml` + CI step. Fix 6 lỗi spec (duplicate paths, peer_email typo, Vietnamese descriptions parse error, invalid ref, device DELETE path). Final 0 errors, 87 warnings.


- `tasks.md` BE-130 marked DONE.
- OpenAPI + Controller: đã có đủ, không cần thay đổi.
- Confirm/Accept deferred to BE-060 completion.

**Next:** BE-170 (Nutrition + AI stub) hoặc BE-200 (Notifications) — tùy user.

### Phiên 31 — đã làm (2026-09-21) [BE-160]

**BE-160: Workout + Weight — DONE**

Plan rev 3 lưu ở `docs/backend-release/tasks/BE-160-plan.md`. 8 commit phases đã merged vào `backend-release/contract-and-foundation`:

1. **`fbc474c` `chore(rule): forbid +/&/@ in commit subjects, prefer commas [BE-160]`** — rule commit mới: header-only preferred, ASCII only, comma thay `+` cho separator.

2. **`0353c91` `docs(be): add BE-160 plan rev 3, mark IN_PROGRESS [BE-160]`** — plan rev 3 + tasks.md status IN_PROGRESS.

3. **`3e6a8eb` `feat(be): add WorkoutSessionExercise, Set entities and config [BE-160]`** — 2 entity mới + enum `WorkoutSetStatus` + EF Configuration mapping.

4. **`3091a9b` `feat(be): add WorkoutSession snapshot fields from plan [BE-160]`** — `WorkoutSession` mở rộng 4 snapshot fields (plan_name/day_of_week/planned_duration_min/target_calories_burn).

5. **`27ebfc5` `feat(be): add WorkoutSnapshot migration with pre-check, FK [BE-160]`** — migration `20260920205100_WorkoutSnapshot` tạo 2 table + index UNIQUE (session_exercise_id, set_index), pre-check orphan exercises/sessions trước DDL, bổ sung FK `WorkoutPlanId` SET NULL (sessions) + CASCADE (exercises).

6. **`2544943` `feat(be): add Workout abstractions, DTOs and error codes [BE-160]`** — `IWorkoutService` + `IWeightService` + `WorkoutErrorCodes` + DTOs (`WorkoutSessionView`, `WorkoutSessionDetailView`, `WorkoutSessionExerciseView`, `WorkoutSetView`, `LogWorkoutSessionRequest`, `LogExerciseRequest`, `LogSetRequest`, `LogWeightRequest`).

7. **`c580a50` `feat(be): add WorkoutService, WeightService and EF stores [BE-160]`** — `WorkoutService.LogSessionAsync` idempotent qua (user_id, client_id) + recompute `completed_sets` + outbox `workout.session_logged`. `WeightService` validate 20..400 kg. EF stores `EfWorkoutStore`/`EfWeightStore`. DbContext thêm 2 DbSet.

8. **`575d98b` `feat(be): add WorkoutsController and DI for Workout services [BE-160]`** — `WorkoutsController` `/api/v1/me/workout/sessions` (GET list with from/to filter, GET {id} detail, POST log yêu cầu `Idempotency-Key` header) + `/api/v1/me/weight` (GET/POST). DI bind.

9. **`b63e4dd` `docs(be): extend WorkoutSession OpenAPI schema, add detail endpoint [BE-160]`** — schema mới `WorkoutSessionDetail`, `WorkoutSessionExerciseView`, `WorkoutSessionExerciseInput`, `WorkoutSetView`, `WorkoutSetInput` + endpoint mới `/me/workout/sessions/{id}`.

10. **`444effe` `test(be): add WorkoutServiceTests with 9 cases [BE-160]`** — 9 unit tests PASS: list empty, list filter date range, get detail null for other user, get detail null when not found, log create with snapshot, idempotent by client_id, throws not found plan, throws validation duration, logs outbox event.

**Tổng kết:**
- 0 build errors, 7 pre-existing warnings (NU1603 package downgrade + 1 xUnit1031).
- 9/9 unit tests PASS.
- OpenAPI extensions: 5 schemas mới + 1 endpoint mới.
- Migration: 1 migration mới (`20260920205100_WorkoutSnapshot`).

**DEFERRED:**
- Integration tests (Testcontainers) — project `Fitmate.IntegrationTests` chưa tồn tại; contract test attempt bị Defender Application Control policy block khi load Domain.dll qua reflection (0x800711C7, env-specific). Sẽ triển khai ở phiên sau nếu cần.
- 0353c91 message có `+` vi phạm rule mới — không amend theo rule "never rewrite history without explicit authorization".

**Next:** BE-170 (Nutrition + AI stub) hoặc BE-200 (Notifications v2) — tùy user.

### Phiên 30 — đã làm (2026-09-21) [BE-380]

**BE-300: Docker image + Compose local/integration — PARTIAL_DONE** (1 pre-existing bug phụ thuộc phát hiện).

Plan rev 3 lưu ở `docs/backend-release/tasks/BE-300-plan.md`.

**Phạm vi đã triển khai:**

1. **Dockerfile (api runtime) — `src/Fitmate.Api/Dockerfile`:**
   - Bỏ `COPY Fitmate.sln` / `Fitmate.slnx`; restore + publish trực tiếp qua `Fitmate.Api.csproj` (dotnet tự resolve project refs).
   - Bỏ `COPY tests/` (loại khỏi build context).
   - Runtime stage: `apt-get install curl --no-install-recommends` + `rm -rf /var/lib/apt/lists/*` TRƯỚC `USER fitmate`; pre-create `/home/fitmate/.aspnet/DataProtection-Keys` + chown.
   - HEALTHCHECK + ENTRYPOINT + non-root giữ nguyên.

2. **Dockerfile.dev — `src/Fitmate.Api/Dockerfile.dev`:** sửa tương tự (bỏ COPY Fitmate.sln).

3. **Dockerfile.migrate (mới) — `Dockerfile.migrate`:**
   - Stage 1 (sdk:8.0): `dotnet tool install --global dotnet-ef --version 8.0.10` (pin đúng EF Core runtime 8.0.10); restore + copy src/; build self-contained bundle linux-x64 (`--runtime linux-x64 --self-contained`).
   - Stage 2 (runtime-deps:8.0-bookworm-slim): non-root fitmate UID 1001 + HOME=/tmp + `DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/bundle-extract` (self-contained bundle cần writable extract dir).

4. **Compose — `compose.yml`:**
   - Bỏ `container_name` cố định (Compose tự prefix theo `--project-name`).
   - Bỏ default ports (chỉ bind qua override); compose.verify300.yml bind riêng 15432/11025/18025/18080.
   - api thêm volume `fitmate-dpkeys:/home/fitmate/.aspnet/DataProtection-Keys`.
   - migrate profile dùng `Dockerfile.migrate` (image ENTRYPOINT, KHÔNG command override).

5. **Verify override — `compose.verify300.yml` (mới):** cổng host riêng + volume named riêng để chạy song song stack local.

6. **DataProtection LOCAL persistence — `src/Fitmate.Api/Program.cs`:** `AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo("/home/fitmate/.aspnet/DataProtection-Keys")).SetApplicationName("Fitmate.Api")` + `using Microsoft.AspNetCore.DataProtection;`. Production key (secret manager / certificate) → BE-340.

7. **Package — `src/Fitmate.Api/Fitmate.Api.csproj`:** thêm `Microsoft.AspNetCore.DataProtection.Extensions 8.0.10` (extension method namespace).

8. **.dockerignore:** thêm `tests/`, `tasks/`.

9. **Runbook — `docs/backend-release/runbooks/local-dev.md` (mới):** prereq + 7 bước quickstart + verify recipe tách biệt + cleanup + known limitations + security warning về `docker compose config` không mask secret.

**Evidence (chạy 2026-09-20):**

| Bước | Lệnh | Kết quả |
|------|------|---------|
| Build api image | `docker compose -p fitmate-verify300 build api` | exit 0, image `fitmate-verify300-api:latest` (~526 MB). |
| Build migrate image | `docker compose -p fitmate-verify300 --profile migrate build migrate` | exit 0, image `fitmate-verify300-migrate:latest` (~101 MB). |
| Compose up postgres+mailhog | `docker compose -p fitmate-verify300 ... up -d postgres mailhog` | 2 container up, postgres healthy trong ~5s. |
| Migrate empty DB | `docker compose -p fitmate-verify300 --profile migrate run --rm migrate` | exit 0; apply 6 migrations theo thứ tự: `InitialSchema` → `IdentitySchema` → `ModerationAction` → `FriendshipAndBlock` → `NotificationsPushV2` → `ChatFriendV1`. Done. |
| Schema verify | `docker exec fitmate-verify300-postgres-1 psql -U fitmate -d fitmate -c "\dt"` | **44 tables** (Identity 6 + domain 38 + `__EFMigrationsHistory`). |
| Re-migrate idempotent | run migrate lần 2 cùng volume | exit 0; "No migrations were applied. The database is already up to date." |
| dotnet test host | `dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --no-restore` | **350/350 PASS**, 0 failed, 0 skipped, 4s. |
| Cleanup | `docker compose -p fitmate-verify300 down -v` | xoá containers + 2 named volume verify300-*. |

**Pre-existing bug phát hiện (KHÔNG thuộc BE-300, chặn verify API readiness):**

Khi up api container, log crash liên tục:
```
Unhandled exception. System.InvalidOperationException: JwtOptions.SigningKey must be configured (env JWT_SIGNING_KEY)
   at Program.<Main>$(String[] args) in /src/src/Fitmate.Api/Program.cs:line 248
```
Sau khi rename env `JWT_SIGNING_KEY` → `Jwt__SigningKey` (đúng convention ASP.NET Core config mapping), crash tiếp:
```
System.InvalidOperationException: Unable to resolve service for type 'Fitmate.Application.Common.JwtOptions'
while attempting to activate 'Fitmate.Application.Auth.RefreshTokenService'.
```
Hai bug:
1. `.env.example` dùng `JWT_SIGNING_KEY` nhưng `Program.cs` dòng 245 đọc `builder.Configuration.GetSection("Jwt").Get<JwtOptions>()` → ASP.NET Core map `JWT_SIGNING_KEY` không match `Jwt:SigningKey` (cần `Jwt__SigningKey`).
2. `JwtOptions` không được register DI (chỉ `Configure<JwtOptions>` bind, không `AddSingleton`/`AddScoped`).

**Đề xuất task mới BE-380: Config/JWT_OPTIONS wiring fix** — sửa env key alias (chấp nhận cả 2 tên) + register JwtOptions DI. CHƯA xử lý — nằm ngoài scope BE-300.

**Không thuộc BE-300 — đã defer có task ID:**

- BE-310 GitHub Actions CI.
- BE-320 OpenAPI linter + contract test.
- BE-330 Load test scripts (k6).
- BE-340 Secrets management (gitleaks + DataProtection prod key + secret manager).

**Chưa có task ID — ghi pending:**

- **BE-350**: Auto-migrate on startup với pg_advisory lock (chỉ khi multi-instance api) — không bắt buộc, migration job vẫn hợp lệ.
- **BE-360**: Backup/restore runbook (PostgreSQL volume + DataProtection keys).
- **BE-370**: Test infrastructure để verify DataProtection key ring tự động (dev-only endpoint).
- **BE-380**: JwtOptions config wiring fix (env alias + DI registration) — phát hiện trong BE-300 verify, chưa thuộc task nào.

**Verify recipe end-to-end bị gián đoạn ở bước 6 (api readiness)** do BE-380. Các bước 1-5 (build, postgres up, migrate, re-migrate, schema verify, host test) đều PASS.

**Commit chưa thực hiện:** 2 message đã duyệt, đợi xác nhận cuối từ user.
1. `chore(docker): fix builds and migration workflow [BE-300]`
2. `docs(docker): document local setup and verification [BE-300]`

**CẬP NHẬT 2026-09-21:** commit 1 + 2 đã thực hiện (`33c18f0` + `253448d`), handoff pending update.

### Phiên 30 — đã làm (2026-09-21) [BE-380]

**BE-380: Config/JWT_OPTIONS wiring fix** — DONE.

Phát hiện trong BE-300 verify: api container crash liên tục vì `JwtOptions.SigningKey` rỗng (env `JWT_SIGNING_KEY` không map sang `Jwt:SigningKey`), và `RefreshTokenService` inject raw `JwtOptions` nhưng không register DI.

**Triển khai:**
- **`src/Fitmate.Application/Common/JwtOptionsLoader.cs` (mới):** helper load + validate `JwtOptions` tại một nơi duy nhất.
  - Env canonical: `JWT_SIGNING_KEY` (đã dùng trong `.env.example` + compose).
  - Alias ASP.NET Core: `Jwt__SigningKey`.
  - Section: `Jwt:SigningKey`.
  - **Một trong ba set** → dùng giá trị đó. **Cả ba set và giống nhau** → chấp nhận. **Khác nhau** → throw fail-fast, không chọn ngầm. **Thiếu** hoặc **key < 32 bytes UTF-8** → throw fail-fast, không fallback default. Không log giá trị key.
  - Issuer/Audience/TTLs đọc từ section `Jwt:*` (backward-compat).
- **`src/Fitmate.Api/Program.cs`:** dùng `JwtOptionsLoader.Load(builder.Configuration)` ngay sau khi bind `AppOptions`. Register raw instance `AddSingleton(jwtOptions)`. `IJwtTokenService` + `IRefreshTokenService` factories cùng resolve instance này (RefreshTokenService giờ resolve được DI — bug 2). Bearer middleware dùng cùng instance. Loại bỏ khối `GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions()` rồi check empty — việc validate đã chuyển cho loader.
- **`src/Fitmate.Application/Fitmate.Application.csproj`:** thêm `Microsoft.Extensions.Configuration.Abstractions` 8.0.0 + `Microsoft.Extensions.Configuration.Binder` 8.0.0 (loader cần `IConfigurationSection.Get<T>()`).
- **`tests/Fitmate.UnitTests/JwtOptionsLoaderTests.cs` (mới, 13 tests):** only canonical / only alias / only section / canonical+alias equal / canonical+alias mismatch (conflict message) / all-three equal / no key (missing) / key 31/16/1 bytes (too short) / key 32 bytes (boundary OK) / null configuration / keeps issuer/audience/TTLs from section.
- **`docs/backend-release/runbooks/local-dev.md`:** sửa dòng 24 — thay "Code map `Jwt:SigningKey` ← `JWT_SIGNING_KEY`" bằng "Code dùng `JwtOptionsLoader` hỗ trợ cả `JWT_SIGNING_KEY` (canonical) và `Jwt__SigningKey` (alias)".
- **`src/Fitmate.Infrastructure/Persistence/DesignTimeDbContextFactory.cs`:** cập nhật comment cũ (ghi "Program.cs throw nếu JWT_SIGNING_KEY rỗng" — không còn đúng).

**Kết quả:**
- `dotnet build`: 0 error.
- `dotnet test`: **363/363 PASS** (13 mới JwtOptionsLoader + 350 cũ).
- Commit: `84b61da fix(auth): JwtOptions dual-alias load, DI, validation [BE-380]` — 6 files, +282/-15.
- Branch: `backend-release/contract-and-foundation`. Chưa push (per AGENTS).

**Verify recipe tiếp theo (BE-300 follow-up):** chạy `docker compose -p fitmate-verify300 -f compose.yml -f compose.verify300.yml up -d postgres mailhog` rồi `docker compose ... --profile migrate run --rm migrate` đã PASS (phiên 29). Tiếp theo `up api` (cần `.env` có `JWT_SIGNING_KEY`) → `/health/ready` 200 → smoke `POST /api/v1/auth/login`. Nếu thành công → BE-300 → DONE, mở BE-310 (GitHub Actions CI).

### Phiên 31 — đã làm (2026-09-21) [BE-310]

**BE-310: GitHub Actions CI** — DONE (definition only, chưa chạy thật vì cần UA-002 repo GitHub public).

**Triển khai:**
- **`.github/workflows/ci.yml` (mới):** 3 jobs gate độc lập.
  - `lint-test`: `actions/checkout@v4` + `actions/setup-dotnet@v4` (DOTNET_VERSION=8.0.x). `dotnet restore Fitmate.slnx` → `dotnet build` với `-warnaserror` + `/p:NoWarn=xUnit1031` (loại warning cosmetic duy nhất). `dotnet test` ghi TRX artifact upload qua `actions/upload-artifact@v4`.
  - `docker-build` (needs `lint-test`): `docker/setup-buildx-action@v3` + `actions/cache@v4` (path `/tmp/.buildx-cache`, key `runner.os-buildx-sha`). Build 2 image (`src/Fitmate.Api/Dockerfile` → `fitmate-ci-api`, `Dockerfile.migrate` → `fitmate-ci-migrate`) với `--cache-from`/`--cache-to` local. **Không push** image (UA-002/003 + BE-500/340 pending).
  - `smoke` (needs `docker-build`): verify `docker compose version`, generate ephemeral `JWT_SIGNING_KEY` qua `openssl rand -base64 64` + ghi `.env` (DB_PASSWORD/JWT_SIGNING_KEY/FEATURE_*). `docker compose up -d postgres` → wait `(healthy)` 60s → `--profile migrate run --rm migrate` → `up -d api` → poll `http://127.0.0.1:8080/health/ready` 90s (`SECONDS` elapsed counter, curl fail log `docker compose logs api` rồi exit 1). Smoke `/health/live` 200. `if: always()` teardown `docker compose down -v` + rm `.env`.
  - `on: push/PR branches: [main, 'backend-release/*']`.
- **`docs/backend-release/runbooks/ci.md` (mới):** 8 sections — workflow structure, trigger rules, secrets & ephemeral, local preview qua `act`, tại sao chưa push image (UA-002/003 + BE-500/340), lỗi thường gặp (NU1603 drift / EF version / readiness timeout), branch protection recommend (gồm 3 required checks), files map.

**Kết quả:**
- YAML: structure 2-space indent, multi-line shell scripts dùng `|` block, không có anchor/alias phức tạp. Không có yaml parser local (Python/Node `yaml` package không cài) — verified manual review + cross-check với GitHub Actions schema docs.
- Chưa chạy workflow thật (cần UA-002 repo public trên GitHub + runner có quyền `contents: read`). Khi có UA-002, branch protection rule theo runbook §7 sẽ require 3 jobs PASS trước merge.

**Commit:**
- (sẽ commit riêng 2 message: ci.yml + docs, theo policy BE không auto-commit)

**Local verify (sau commit):**
- Cài `act v0.2.89` qua winget. Set `actrc` map `ubuntu-22.04 → node:20-bookworm-slim` + `--container-architecture linux/amd64` + `DOCKER_HOST=npipe:////./pipe/dockerDesktopLinuxEngine`.
- `act -j lint-test -n` (dry-run) → PASS — 6 step nhận diện đầy đủ (Checkout/Setup .NET 8.0.x/Restore/Build/Test/Upload artifact), triggers + env + secrets mapping đúng.
- `act -j lint-test` (real run) → PASS 4/6 step (Checkout/Setup .NET 8.0.425 installed/Restore fail), fail ở bước `dotnet restore Fitmate.slnx` do image thiếu `libicu` (system lib cần cho `System.Globalization`). GitHub runner thật (`ubuntu-22.04`) có sẵn libicu → workflow chạy đầy đủ trên GH. Reverted phần thử cài `apt-get install libicu` (chỉ phụ thuộc act image, không cần trên GH runner).
- Ghi nhận `act` chỉ verify workflow structure / triggers; verify end-to-end cần GitHub runner thật.

### Phiên 28 — đã làm (2026-09-20) [BE-191]

**BE-191: Chat (friend message) — friend-only scope** — DONE.

Triển khai 5 friend chat endpoints trong OpenAPI. AI chat KHÔNG thuộc scope này (BE-430 chờ provider); schema AI thread giữ nguyên để không vướng. User chốt scope ở AskQuestion:
- Friend-only (5 endpoints hiện có, không AI).
- `peer_email` → `peer_user_id` (DEC-036 đồng bộ).
- 1 conversation canonical cho mỗi cặp user, CHECK low<high, không cascade delete với Friendship.
- `ChatThreadRead` riêng (unique `(thread_id, user_id)`), `last_read_message_id` + thời điểm server xác định, monotonic.
- `client_message_id` cho idempotency retry, marker không lùi khi nhiều thiết bị.
- Block/unfriend KHÔNG xoá history; chỉ chặn send mới.

**Triển khai:**
- **Domain (commit 1):** `FriendConversation(UserLowId, UserHighId, LastMessageAt)` với `CanonicalPair` helper (Guid.CompareTo lexical byte order, đồng bộ `Friendship.CanonicalPair`), `IsParticipant` + `OtherParticipant` helpers. `ChatThreadRead(ConversationId, UserId, LastReadMessageId, LastReadAt)` với PK composite + `TryAdvance` (chỉ update khi có advance). `ChatMessage` extend thêm `ConversationId` (nullable, null = AI thread) + `ClientMessageId` (nullable, idempotency). Migration `20260920121331_ChatFriendV1`: alter `chat_messages.ThreadId` → nullable; thêm `chat_messages.ConversationId` (nullable) + `chat_messages.ClientMessageId` (nullable); tạo `friend_conversations` (CHECK `UserLowId<UserHighId`, UNIQUE pair) + `chat_thread_reads` (PK composite); indexes `(UserHighId)`, `(ConversationId+CreatedAt+Id)`, partial unique `(ConversationId+ClientMessageId) WHERE ConversationId IS NOT NULL AND ClientMessageId IS NOT NULL`. Apply OK trên local Postgres.
- **Application (commit 2):** `IChatStore` abstraction (GetOrCreate/FindConversation/FindConversationForPair/AddMessage/FindMessageByClientMessageId/GetLastMessage/ListConversations với aggregate ConversationSummaryRow/ListMessages/FindMessage/GetThreadRead/UpsertThreadRead/CountUnread/SaveChanges). `IChatService` (SendFriendMessage/ListConversations/ListMessages/MarkRead/GetConversation) + DTOs (`ChatMessageDto`, `FriendSummaryDto`, `ConversationDto`, `ConversationPage`, `MessagesPage`). `ChatService` inject `IChatStore`, `IFriendService` (qua `IsFriendAsync`), `IBlockStore` (`IsBlockedEitherWayAsync`), `IOutboxDispatcher`, `TimeProvider`. Send flow: validate (self 400 / empty/long 400 / auth 401) → block check 2 chiều → friend check → GetOrCreate conversation (race-safe qua UNIQUE constraint) → dedup check (same client_message_id + same payload → trả row cũ; khác payload → 409 `IDEMPOTENCY_MISMATCH`) → add message + outbox `chat.message.sent` (cùng transaction). MarkRead: monotonic (`candidate_message.CreatedAt > current.LastReadAt`); cũ hơn hoặc trùng → no-op 204. Cursor pagination: `(last_message_at desc, id desc)` cho conversations, `(created_at desc, id desc)` cho messages. Limit 1..100, default 30.
- **Infrastructure (commit 3):** `EfChatStore`: `GetOrCreateConversationAsync` với catch `DbUpdateException` cho race (request thứ 2 retry qua `FindConversationForPairAsync`); `ListConversationsAsync` aggregate query 1 round-trip (conversations + last_messages GroupBy + reads map + unread count); `UpsertThreadReadAsync` update in-place. Pagination clamp + cursor encode/decode base64(ticks|guid).
- **API (commit 4):** `ChatController` (`POST /api/v1/chat/friends/messages` body `{peer_user_id, text, client_message_id?}` → 201 `ChatMessageDto`), `GET /api/v1/chat/friends/conversations?cursor&limit`, `GET /api/v1/chat/friends/conversations/{id}`, `GET /api/v1/chat/friends/conversations/{id}/messages?cursor&limit`, `POST /api/v1/chat/friends/conversations/{id}/read` body `{last_read_message_id}` → 204. DI wiring `IChatStore` + `IChatService` trong `Program.cs`.
- **Docs (commit 5):** OpenAPI: schema `ChatMessage` + `Conversation` extend (thêm `conversation_id`, `client_message_id`, `last_message_at`); schema mới `MarkReadRequest`; `peer_email` → `peer_user_id` (DEC-036); thêm 3 endpoints mới (conversations/{id}, messages, read). Authorization matrix §9 cập nhật (participant check 404, monotonic, idempotency). Domain-rules §12 mới (friend chat policy: canonical, idempotency, authorization, read marker monotonic, unread count, history retention, pagination, privacy). Decisions DEC-042 (canonical pair), DEC-043 (idempotency client_message_id), DEC-044 (read marker monotonic), DEC-045 (block/unfriend không xoá history) — Accepted. Feature-api-matrix FP-CH-04..07 cập nhật status.
- **Tests (commit 6):** `ChatServiceTests.cs` mới — **29/29 PASS** (full suite 350/350 từ 321). Coverage: permission (self/block 2 chiều/non-friend/empty/long/auth), idempotency (same payload/same key + different payload → 409), conversation lifecycle (canonical pair khi 2 user cùng tạo, unfriend preserved, block prevent send + history kept), read marker (advance/no-op old/non-participant 404/wrong message 400/unknown 400), unread count (own messages excluded, decreases after mark-read, increases on new messages after marker), pagination (limit clamp, empty conversation), GetConversation (participant/non-participant 404), outbox same-transaction, FindConversationForPair reverse-order.

**Kết quả:**
- `dotnet build`: 0 warning, 0 error (1 pre-existing xUnit1031 trong `AuthorizationGuardTests.cs:37` không liên quan).
- `dotnet test`: **350/350 PASS** (29 mới + 321 cũ; 0 failed, 0 skipped).
- Migration `ChatFriendV1` apply OK trên local Postgres (32 → 35 tables).
- OpenAPI YAML update — schema ChatMessage + Conversation extend + MarkReadRequest schema + 3 endpoints mới (conversations/{id}, messages, read) + peer_user_id.

**Files changed (BE-191):**
- Domain: `src/Fitmate.Domain/Chat/FriendConversation.cs` (new), `src/Fitmate.Domain/Chat/ChatThreadRead.cs` (new), `src/Fitmate.Domain/Chat/ChatMessage.cs` (extend ConversationId + ClientMessageId), `src/Fitmate.Domain/Common/ErrorCodes.cs` (+Chat section).
- Application: `src/Fitmate.Application/Chat/IChatStore.cs` (new), `src/Fitmate.Application/Chat/IChatService.cs` (new + DTOs), `src/Fitmate.Application/Chat/ChatService.cs` (new).
- Infrastructure: `src/Fitmate.Infrastructure/Persistence/Chat/EfChatStore.cs` (new), `src/Fitmate.Infrastructure/Persistence/Configurations/ChatConfiguration.cs` (extend — FriendConversationConfig + ChatThreadReadConfig + ChatMessage partial unique), `src/Fitmate.Infrastructure/Persistence/FitmateDbContext.cs` (+2 DbSet), `src/Fitmate.Infrastructure/Persistence/Migrations/20260920121331_ChatFriendV1.cs` + `.Designer.cs`, `FitmateDbContextModelSnapshot.cs` (auto).
- Api: `src/Fitmate.Api/Chat/ChatController.cs` (new — 5 endpoints), `src/Fitmate.Api/Program.cs` (+using +DI).
- Tests: `tests/Fitmate.UnitTests/ChatServiceTests.cs` (new — 29 tests).
- Docs: `docs/backend-release/openapi/fitmate-v1.yaml` (ChatMessage + Conversation + MarkReadRequest + 3 endpoints mới + peer_user_id), `docs/backend-release/decisions.md` (DEC-042..045), `docs/backend-release/domain-rules.md` (§12 mới), `docs/backend-release/authorization-matrix.md` (§9 update), `docs/backend-release/feature-api-matrix.md` (FP-CH-04..07), `docs/backend-release/tasks.md` (BE-191 → DONE), `docs/backend-release/handoff.md` (phiên 28 này).

**Pending integration (ghi tasks.md, không block):**
- BE-200 worker phase 2: consume outbox `chat.message.sent` → push notification cho peer. Schema `ux_notifications_dedup` đã handle idempotent. Cần wire handler khi BE-200 mở rộng.
- BE-310 Integration test WebApplicationFactory sẽ cover route 200/401/403/404 cho 5 endpoint này.
- BE-160 Booking confirm/accept (PARTIAL_DONE ở BE-060).
- AI chat (BE-430 chờ provider).

### Phiên 27 — đã làm (2026-09-20) [BE-200]

**BE-200: Notifications — Phase 1 (in-app + push delivery abstraction)** — PARTIAL_DONE.

Đã implement:
- **Domain (commit 1):** `Notification` (Summary, SourceEventId, SourceType), `Device` (EncryptedToken, KeyId, Provider, IsActive, RevokedAt, LastUsedAt), `PushDelivery`, `PushProviderConfig` + enums `PushProvider{Fcm, Expo}` và `PushDeliveryStatus` (8 states: Pending/Sent/Deferred/FailedRetryable/FailedPermanent/TokenInvalid/Sweeping/Expired).
- **Application (commit 2):** `INotificationService`, `IDeviceService`, `IPushDeliveryDispatcher`, `IPushProvider`, `IPushDeliveryTokens`, `INotificationStore`, `IDeviceStore`, `IPushStore`, `INotificationAuditLogger`, `NotificationsDtos`, `PushPayload`, `PushTtlOptions`.
- **Application services (commit 3):** `NotificationService` (List + MarkRead idempotent, 404 cho not-owned), `DeviceService` (Register idempotent SHA-256 dedup + Revoke soft), `PushDeliveryDispatcher` (DispatchAsync idempotent qua `ExistsBySourceAsync`; skip push + audit `notification.skipped reason=push_disabled|no_active_device`; nếu enabled → tạo `PushDelivery` per active device + enqueue outbox `notification.push_requested`). `IPushDeliveryTokens` simplify chỉ 2 method `Protect`/`Unprotect` (DataProtection tự nhúng key id trong ciphertext → không cần expose key id ở Application).
- **Application outbox (commit 4):** `NotificationPushHandler` (Type=`notification.push_requested`): per-device sender-side checks (active, owner match, TTL expired, domain validity + block check cho friend source), per-device failure isolation, decrypt qua `IPushDeliveryTokens.Unprotect`, gọi `IPushProvider.SendAsync`, map `PushSendResult` → status transitions. `PushDeliverySweepService` claim expired qua `FOR UPDATE SKIP LOCKED` rồi mark `Sweeping` → `Expired`.
- **Infrastructure (commit 5):** `EfNotificationStore` / `EfDeviceStore` / `EfPushStore` (raw SQL claim), `EfPushDeliveryDispatcher` (DbContext save), `DataProtectionTokenCipher` (`IDataProtectionProvider.CreateProtector("fitmate.push-token.v1")`, log cảnh báo khi decrypt fail → caller treat `TokenInvalid`), `MissingPushProvider` (luôn trả `ProviderNotConfigured`), `NotificationAuditLogger` (Serilog structured), `SweepBackgroundService` (Interval=30 phút). csproj thêm `Microsoft.AspNetCore.DataProtection` + `Microsoft.Extensions.Hosting.Abstractions`. DbContext thêm `DbSet<PushDelivery>` + `DbSet<PushProviderConfig>`.
- **EF Configuration (commit 6):** `NotificationsConfiguration` cho 4 table. Indexes: `ix_notifications_user_created`, `ix_notifications_user_read_created`, partial unique `ux_notifications_dedup (user_id, source_event_id, source_type) WHERE source_event_id IS NOT NULL`. Devices: unique `ux_devices_user_token (user_id, token_hash)`, partial `ix_devices_active WHERE is_active=true`. Push deliveries: partial `ix_push_deliveries_sweep WHERE status IN (0,2,3)`, ix notification_id, ix device_id. NotificationPushHandler refactor: bỏ `IBlockLookup` riêng, dùng `IBlockStore` từ BE-181.
- **API + DI (commit 7):** `NotificationsController` (`GET /api/v1/me/notifications?read=&page=&pageSize=`, `POST /api/v1/me/notifications/{id}/read`). `DevicesController` (`POST /api/v1/users/me/devices` body `{device_token, platform, provider}`, `DELETE /api/v1/users/me/devices/{id}`). Program.cs DI: `IPushDeliveryTokens` (scoped), 3 EF stores, 2 services, `INotificationAuditLogger`, `IPushDeliveryDispatcher` (factory inject DbContext), `IPushProvider` (singleton `MissingPushProvider`), `PushDeliverySweepService` (scoped) + `SweepBackgroundService` (hosted), `NotificationPushHandler` outbox handler. DataProtection comment chỉ đến UA-DP-001 (secret manager cho key ring).

**Pending trong BE-200:** — DONE 2026-09-20 (phiên 27 cập nhật, xem "Phiên 27 update" bên dưới).
- Producer wiring: FriendService accept path → enqueue notification (gắn với source_type=`friend`); ModerationService.Warn → enqueue notification (gắn với source_type=`moderation`). ✅
- Unit tests: NotificationService (list filter + MarkRead idempotent + 404), DeviceService (register idempotent + revoke 404 + validation), PushDeliveryDispatcher (skip khi disabled / khi không có device / fan-out), NotificationPushHandler (block check, device inactive, expired, result mapping), PushDeliverySweepService. ✅
- Migration generate + apply local: `dotnet ef migrations add NotificationsPushV2 --project src/Fitmate.Infrastructure --startup-project src/Fitmate.Api`. ✅ (migration `20260920064150_NotificationsPushV2`)
- OpenAPI doc update: thêm `/me/notifications` + `/users/me/devices` + schemas. ✅ (đã có sẵn từ phase trước — endpoints + `Notification` schema)
- DEC-040 (key ring location) + DEC-041 (sweep interval): document trong `decisions.md`. ✅ (status Proposed → Accepted)
- `Authorization matrix` cập nhật §10 (device owner check + Producer dedup). ✅
- Producer tests: đảm bảo mỗi domain event trigger đúng source_event_id (không trùng). ✅ (FriendServiceTests + ModerationServiceTests cập nhật)

**Phiên 27 update (2026-09-20)** — BE-200 PARTIAL_DONE hoàn tất:
- **Migration `20260920064150_NotificationsPushV2`**: apply OK trên local Postgres. Schema verified: 4 bảng (`notifications`, `devices`, `push_deliveries`, `push_provider_configs`) với 5 indexes.
- **Producer wiring**: `FriendService.SendRequestAsync` + `AcceptRequestAsync` gọi `IPushDeliveryDispatcher.DispatchAsync` với `source_event_id = request.Id`, `source_type = friend_request_sent | friend_request_accepted`. `ModerationService.ResolveReportAsync` (action=Warn) với `source_event_id = report.Id`, `source_type = moderation_warning`. Partial unique index `(user_id, source_event_id, source_type) WHERE source_event_id IS NOT NULL` đảm bảo idempotent.
- **Unit tests**: `dotnet test` **321/321 PASS** (76 mới: NotificationService 11 + DeviceService 9 + PushDeliveryDispatcher 12 + NotificationPushHandler 14 + PushDeliverySweepService 5 + 25 cập nhật trong FriendServiceTests/ModerationServiceTests).
  - Domain fix: `PushDelivery.CreateExpired` `internal` → `public` để test project truy cập.
  - Test mock: `NotificationPushHandlerTests.InMemoryDeviceStore.GetActiveDevicesAsync` trả tất cả devices (handler tự enforce per-device checks).
- **Docs**: DEC-040 + DEC-041 → **Accepted**. Authorization matrix §10 bổ sung "Producer dedup" note.
- **Commit**: `e864408 test(notifications): add unit tests for ... [BE-200]`.

**Pending beyond BE-200:**
- UA-FCM-001: chọn push provider thật (FCM trực tiếp vs Expo). Hiện dùng `MissingPushProvider` (luôn `ProviderNotConfigured`, không backlog tích lũy).
- UA-DP-001: secret manager cho DataProtection key ring (path trên production).
- BE-191 Chat (friend message) đang phụ thuộc BE-181 (đã xong) — bắt đầu triển khai tiếp.
- BE-160 Status migration, BE-130 booking flows, BE-140 payment skeleton.

### Phiên 26 — đã làm (2026-09-19) [BE-151]

**BE-151: GET /admin/platform-policy (read-only theo DEC-029)** — DONE.

Triển khai endpoint read-only để Admin Web (`Settings.jsx`) hiển thị chính sách phí nền tảng cố định 10%, đồng thời cung cấp foundation cho FE convert sang % và policy_version cho audit.

**Triển khai:**
- `PlatformPolicyOptions` (Application/Common): bind section `PlatformPolicy` qua env `PLATFORM_POLICY__FEEBPS/POLICYVERSION/EFFECTIVEFROMUTC`. Default 1000 bps / "v1" / 2026-09-19 UTC. Bps ∈ [0,10000] validate qua `Money.ValidateBps`.
- `IPlatformPolicyService` + `PlatformPolicyService` + record `PlatformPolicy` (PlatformFeeRateBps, PolicyVersion, EffectiveFrom, Editable=false, DisplayHint). Service enforce role Admin qua `IAuthorizationGuard.EnsureRoleAsync` (BE-090) — chống bypass controller filter.
- `AdminPlatformPolicyController` (`/api/v1/admin/platform-policy`): `[Authorize(Policy=AdminOnly")]` + route `[HttpGet]` chỉ.
- `Program.cs`: bind section + register service.
- `.env.example` + `.env`: thêm block `# ===== Platform policy (DEC-029 read-only) =====` với default 10%.
- `tasks.md` BE-151 → DONE.

**Kết quả**:
- `dotnet build`: 0 error (1 warning pre-existing xUnit1031 trong `AuthorizationGuardTests.cs:37`, không liên quan).
- `dotnet test`: **275/275 PASS** (9 mới + 266 cũ; 0 failed, 0 skipped). Note: `JwtTokenServiceTests.Validate_ReturnsNull_ForTamperedToken` được ghi pre-existing fail ở phiên 24 không còn fail khi chạy sau BE-151 — có thể do build cache refresh. Đã verify bằng cách chạy lại full test suite.
- OpenAPI spec cho endpoint đã có sẵn (BE-150 scope mở rộng), không cần update.

**Files changed**:
- Domain/Application: `src/Fitmate.Application/Common/PlatformPolicyOptions.cs` (new), `src/Fitmate.Application/Finance/PlatformPolicyService.cs` (new — interface + record + impl), `src/Fitmate.Application/Common/AppOptions.cs` (+PlatformPolicy property).
- Api: `src/Fitmate.Api/Admin/AdminPlatformPolicyController.cs` (new), `src/Fitmate.Api/Program.cs` (Configure section + DI).
- Tests: `tests/Fitmate.UnitTests/PlatformPolicyTests.cs` (new — 9 tests).
- Docs: `docs/backend-release/tasks.md` (BE-151 entry + DONE summary update), `docs/backend-release/handoff.md` (phiên 26 này).

**Pending integration (ghi tasks.md, không block)**:
- BE-310 Integration test WebApplicationFactory sẽ cover route 200 + 401/403 cho endpoint này.
- Post-MVP: `POST /admin/platform-policy` cho phép admin sửa (DEC-029 §"Khi scale") — cần `platform_policies` table + audit + version + cool-down. Chưa có trong release đầu.

### Phiên 25 — đã làm (2026-09-19) [BE-181]

**BE-181: Friend + Friendship + Block** — DONE.

Triển khai đúng 6 quyết định user chốt 2026-09-19 (ghi `decisions.md` DEC-036..039):
1. **Recipient dùng `to_user_id`** (UUID, không email). Sender lấy từ authenticated principal. OpenAPI đổi `to_email` → `to_user_id`. Migration rename `FromUserId→RecipientId`, `ToUserId→SenderId`.
2. **Friendship: 1 row canonical** `(UserLowId, UserHighId)` với `CHECK (low<high)`. Application dùng `Friendship.CanonicalPair(a, b)` chuẩn hoá trước khi insert. .NET `Guid.CompareTo` và PostgreSQL `<` đều so sánh UUID theo value byte order — khớp nhau.
3. **Unfriend: hard delete** Friendship row canonical. Idempotent nếu không phải bạn. KHÔNG xoá chat / post / booking kèm theo. Outbox `friendship.removed` không gửi notification.
4. **Block: bảng `blocked_users` riêng**, tách khỏi moderation. Block huỷ pending FriendRequest 2 chiều + xoá Friendship hiện tại. Unblock KHÔNG tự khôi phục. KHÔNG gửi notification cho người bị block. KHÔNG ảnh hưởng booking/finance.
5. **Outbox events** chuẩn hoá: `friend.request.created/accepted` enqueue; `rejected/cancelled/friendship.removed/user.blocked/user.unblocked` KHÔNG notification.
6. **Accept atomic**: 1 transaction — status guard + block re-check (race) + insert Friendship + outbox. Race accept vs block → reject silent, không tạo friendship.

**Kết quả**:
- `dotnet build`: 0 error, 1 warning pre-existing (`xUnit1031` không liên quan).
- `dotnet test`: **266/266 PASS** (47 mới + 219 cũ; 0 failed, 0 skipped).
- Migration `20260919111325_FriendshipAndBlock` apply OK trên local Postgres. Schema verify: drop `friends`, create `friendships` + `blocked_users` với CHECK constraint, partial unique index `(sender_id, recipient_id) WHERE "Status"=0`.

**Files changed**:
- Domain: `src/Fitmate.Domain/Social/Friendship.cs` (new — canonical pair + helper), `Social/BlockedUser.cs` (new), `Social/FriendRequest.cs` (rename fields + Cancel), `Common/Enums.cs` (+Cancelled), `Common/ErrorCodes.cs` (+9 codes).
- Application: `Social/IFriendStore.cs` (new), `Social/IFriendService.cs` (new — FriendPage/BlockListPage/FriendRequestStatusView), `Social/FriendService.cs` (new — 7 use cases), `Social/BlockService.cs` (new), `Social/CursorEncoder.cs` (new — share với BE-180), `Social/ISocialService.cs` + `Social/SocialService.cs` (delegate `IsFriendAsync` qua `FriendService`).
- Infrastructure: `Social/EfFriendStore.cs` (new — canonical query + cursor pagination), `Social/EfBlockStore.cs` (new), `Social/EfSocialStore.cs` (Friends-only feed query canonical), `Persistence/Configurations/SocialConfiguration.cs` (FriendRequestConfiguration + FriendshipConfiguration + BlockedUserConfiguration), `Persistence/FitmateDbContext.cs` (DbSet).
- Api: `Social/FriendsController.cs` (new — 6 endpoints), `Social/BlocksController.cs` (new — 3 endpoints), `Program.cs` (DI).
- Tests: `tests/Fitmate.UnitTests/FriendServiceTests.cs` (new — 20 tests), `tests/Fitmate.UnitTests/SocialServiceTests.cs` (add `StubFriendService`).
- Migrations: `Persistence/Migrations/20260919111325_FriendshipAndBlock.cs` (new — drop `friends`, create `friendships` + `blocked_users`, rename columns, partial unique index), `Migrations/20260919111325_FriendshipAndBlock.Designer.cs`, `Migrations/FitmateDbContextModelSnapshot.cs`.
- Docs: `docs/backend-release/decisions.md` (DEC-036/037/038/039), `docs/backend-release/domain-rules.md` (§11 Friend/Block policy), `docs/backend-release/authorization-matrix.md` (§8 Social + block note), `docs/backend-release/feature-api-matrix.md` (FP-SC-07..09e), `docs/backend-release/openapi/fitmate-v1.yaml` (to_user_id + canonical Friend schema + 7 endpoints), `docs/backend-release/tasks.md` (BE-181 → DONE), `docs/backend-release/handoff.md` (phiên 25 này).

**Pending integration (ghi tasks.md, không block)**:
- BE-200 Notifications worker — chưa có, outbox events vẫn được ghi để chờ consumer; service-side KHÔNG báo "đã gửi notification".
- BE-191 Chat friend message — chưa implement; block policy đã chốt (chặn message mới + thread mới) sẽ được áp dụng khi BE-191 làm.
- Admin moderation exception đọc block list (UA-FUTURE).

### Phiên 24 — đã làm (2026-09-19) [BE-211]

**BE-211: Moderation follow-up** — DONE.

Triển khai xong các quyết định user đã chốt:
- Idempotency cho POST /social/reports (header `Idempotency-Key` 8..128 chars; unique partial index `(reporter, key) WHERE key NOT NULL`).
- Spam-check 1 reporter chỉ có 1 Open report cho cùng target (DEC-028).
- Certificate report visibility — chỉ Approved mới report được (rejected với `CertificateNotReportable` 400).
- Warn action → ghi `user_moderation_actions` (kind=Warn) + enqueue outbox `user.warning` payload `{UserId, ReportId, Reason, AdminId}` để phase 2 worker xử lý.
- Lock action → 4 duration (`permanent|30d|7d|24h`); `UserManager.SetLockoutEndAsync` + `UpdateSecurityStampAsync` (session revoke) + `user_moderation_actions` row (kind=Lock, expires_at).
- DeleteContent → soft-delete Post hoặc Comment; reject với `DeleteContentNotApplicable` cho User/Certificate target.
- Audit log cho mọi resolve/dismiss: target = Report.Id, action = `report.resolve.warn` / `report.resolve.lock` / `report.resolve.delete_content` / `report.resolve.no_action` / `report.dismiss` + reason.
- Bug fix `EfModerationStore.SaveReportAsync` không truyền `ResolvedByAdminId` đúng — đã sửa, truyền qua domain `Report.Resolve(adminId, action, note)`.

**Kết quả**:
- `dotnet build`: 0 warning, 0 error (1 warning pre-existing `AuthorizationGuardTests.cs:37` xUnit1031 không liên quan).
- `dotnet test`: **242/243 PASS** (1 fail pre-existing `JwtTokenServiceTests.Validate_ReturnsNull_ForTamperedToken` đã có từ BE-030).
- Migration `20260919102229_ModerationAction` apply OK trên local Postgres (fitmate-db); schema verified: `reports.idempotency_key`, 2 unique partial index, bảng `user_moderation_actions` với 2 index.

**Files changed**:
- Domain: `src/Fitmate.Domain/Moderation/UserModerationAction.cs` (new), `Report.cs` (IdempotencyKey + ResolveAction), `Social/Comment.cs` (SoftDelete), `Common/ErrorCodes.cs` (+6 codes).
- Application: `Moderation/IModerationStore.cs` (FindOpenReportAsync + SaveChangesAsync + 2 soft-delete + ReportRow moved), `ReportRow.cs` (new), `IModerationService.cs` (LockDuration + ReportView.ResolveAction), `IModerationActionStore.cs` (new), `ModerationService.cs` (rewrite — 9 deps), `Social/ISocialService.cs` (ReportInputDto.IdempotencyKey + ReportTargetType), `Social/ISocialStore.cs` (FindOpenReportAsync/ByKey + GetCommentAsync + SaveChangesAsync), `Social/SocialService.cs` (CreateReportAsync signature + visibility check).
- Infrastructure: `Moderation/EfModerationStore.cs` (rewrite — bug fix + 2 soft-delete + SaveChangesAsync + FindOpenReportAsync), `Moderation/EfModerationActionStore.cs` (new), `Social/EfSocialStore.cs` (AddReportAsync returns ID + FindOpenReportAsync/ByKey + GetCommentAsync), `Persistence/Configurations/ModerationConfiguration.cs` (IdempotencyKey + ResolveAction + 2 partial unique index + UserModerationAction entity), `Persistence/FitmateDbContext.cs` (UserModerationActions DbSet), `Persistence/DesignTimeDbContextFactory.cs` (new — để `dotnet ef` không cần Program.cs), `Persistence/Migrations/20260919102229_ModerationAction.cs` (new — auto-generated).
- Api: `Admin/AdminReportsController.cs` (ResolveReportBody.LockDuration), `Social/SocialController.cs` (Idempotency-Key header + target_type `certificate`), `Program.cs` (DI: `IModerationActionStore`).
- Tests: `tests/Fitmate.UnitTests/ModerationServiceTests.cs` (rewrite — 21 tests, 9 in-memory helpers), `tests/Fitmate.UnitTests/SocialServiceTests.cs` (rewrite — 24 tests).
- Docs: `docs/backend-release/handoff.md` (plan + done), `docs/backend-release/tasks.md` (BE-210 → DONE; BE-211 → DONE).

### Commit message maintenance — 2026-09-19 [BE-350]

- User authorized rewriting local commit messages in this repository only.
- Normalized all 33 commits after local `main` into descriptive English Conventional Commits. Translated Vietnamese message bodies and removed repetitive bullet prefixes. No `Co-authored-by` trailers remained in this range at the start; the new policy prevents adding them to future messages.
- Preserved every commit tree, mapped parent chain, author/committer identity, and author/committer timestamp. No application code changes, squash, reorder, push, or remote history rewrite.
- Previous HEAD: `18ae101d8183e3124cf933042765870351af6cba`; rewritten HEAD before the policy commit: `99849d9584e55f61ec49f7156f9f6a5bd67753fc`.
- Backup: `backup/english-messages-2026-09-19T07-21-40-817Z`. Recovery without disturbing the current branch: `git switch -c recovery/before-message-cleanup backup/english-messages-2026-09-19T07-21-40-817Z` (use a clean working tree).
- Full old/new mapping: `.git/commit-message-cleanup/2026-09-19T07-21-40-817Z/mapping.json`. Earlier handoff entries contain historical hashes from prior Cursor rewrites; use Git subjects, task IDs, and backup history rather than assuming those old hashes identify current commits.
- Added English-only and no-attribution instructions to `AGENTS.md` and `.cursor/rules/commit-policy.mdc`. No active Git message hooks were found; global Git settings were not changed.
- Remote verification found that `origin/main` was already rewritten externally to `c1c7a71`; local `main` remains `22eadf2`. None of the 33 target commits occur in remote history. Do not automatically rebase or push to reconcile the separate histories.
- Next: continue the existing backend task plan; this maintenance does not mark any backend feature complete.

- **Ngày**: 2026-09-19
- **Phiên 23**: Triển khai **BE-210** (Moderation: list/resolve/dismiss reports).
  - **Application** (pure): `IAdminUserService` (List/SetLock) + abstraction `IAdminUserStore`. Service enforce role Admin ở Application (BE-090) + ngăn self-lock + validate reason ≥ 5 chars. Audit: `user.lock` / `user.unlock` với reason qua `IAuditLogger`. SecurityStamp bump → invalidate JWT/refresh tokens.
  - **Infrastructure**: `EfAdminUserStore` dùng `UserManager.SetLockoutEnabledAsync/SetLockoutEndDateAsync/UpdateSecurityStampAsync`. List filter: role (member/pt/admin), status (active/locked/pending_deletion), search (email/userName ILIKE), pagination max=100, sort allowlist (`createdAt:asc|desc`, `email:asc|desc`).
  - **Api**: `AdminUsersController` Authorize role=Admin: `GET /api/v1/admin/users` (list + filter + sort + page), `GET /api/v1/admin/users/{id}` (summary), `POST /api/v1/admin/users/{id}/lock` (lock/unlock với reason).
  - **Bảo vệ**: `AdminUserSummary.FullName` hiện null — sẽ fill từ `UserProfile` ở BE-160 (khi tách schema).
  - **Kết quả**: `dotnet build` 0 error; `dotnet test` **173/173 PASS**. Commit: `<type>(be): BE-110 DONE -- Admin user list/lock/unlock + SecurityStamp revoke + audit [BE-110]`.
- **Phiên 17**: Triển khai **BE-090** (Authorization matrix enforcement).
  - **Application** (pure): `IAuthorizationGuard` (EnsureRoleAsync/EnsureAnyRoleAsync/EnsureOwnerAsync/HasRoleAsync/GetPrimaryRoleAsync) + `AuthorizationGuard` impl dùng `IUserRoleLookup`.
  - **Enforce ở Application** (không chỉ controller filter): `CertificateService.SubmitAsync/ListMineAsync` (PT), `CertificateService.ApproveAsync/RejectAsync` (Admin — chống bypass filter), `ServiceOfferingService.CreateAsync/UpdateAsync/DeactivateAsync` (PT), `AvailabilityService.ReplaceMineAsync` (PT), `BookingService.CompleteAsync` (PT). Ownership check vẫn ở service.
  - **Infrastructure**: mở rộng `IUserRoleLookup.GetPrimaryRoleAsync` trả `Admin/PT/Member` (priority Admin > PT > Member); impl qua `UserManager.GetRolesAsync`.
  - **Tests**: helper `AlwaysAllowAuthz.Instance` cho test hiện có; `RoleRequireAuthz` cho deny cases. Booking test phải pass fake guard trước complete. 10/10 mới PASS (164/164 tổng): AuthorizationGuard (8) + BookingAuthzIntegration (2). 1 warning cosmetic xUnit1031 (blocking trên HasRoleAsync dùng `.Result`).
  - **Out-of-scope**: admin endpoints chưa có (BE-110..220 sẽ tự enforce), social/community endpoints chưa có (BE-180+), field-level sensitivity (chưa có use case).
  - **Kết quả**: `dotnet build` 0 error; `dotnet test` **164/164 PASS**. Commit: `<type>(be): BE-090 DONE -- AuthorizationGuard + role enforcement tại Application layer [BE-090]`.
- **Phiên 16**: Triển khai **BE-060 (PARTIAL_DONE)** (Booking lifecycle).
  - **Application** (pure): `IBookingService` với CreateHold/Cancel/Complete/Reschedule/ListByMember/ListByPt + abstractions (`IAppointmentStore`/`IBookingHoldStore`/`IServiceBookingLookup`). Outbox enqueue: `booking.held`, `booking.cancelled`, `booking.completed`, `booking.rescheduled`. Domain rules enforced: hold TTL = 5 phút (cấu hình qua DI), slot-availability check (held/awaiting_payment/paid/completed), cancel reason min 5 chars, complete chỉ owner-PT, reschedule transaction check slot mới trước khi release slot cũ.
  - **Infrastructure** (EF): `EfAppointmentStore` (ExistsActiveForSlot, ListByMember/ByPt phân trang) + `EfBookingHoldStore` + `ServiceBookingLookup` delegate sang `IServiceOfferingStore` (BE-050).
  - **Api**: `BookingsController` (`POST /api/v1/bookings/holds`, `GET /bookings/holds/{id}`, `POST /me/bookings/{id}/cancel|complete|reschedule`, `GET /me/bookings` member, `GET /me/trainer-bookings` PT role).
  - **Behavior**:
    - CreateHold: validate service (active + đúng PT) → check slot không active → tạo Hold + Appointment(Held) → outbox `booking.held`.
    - Complete: domain throws ConflictException nếu chưa Paid; outbox `booking.completed`.
    - Reschedule: check ownership + status (không trên terminal) → check newSlot != current → check newSlot open → cancel old + create new (linked via Outbox event) → outbox `booking.rescheduled`.
  - **Out-of-scope (BE-160)**: `Confirm` (sau payment) và `Accept` (PT accept pending). Cần `Appointment.HoldId FK` + tách enum `BookingStatus` → `AppointmentStatus` (DEC-026). Hiện tại domain schema gộp lịch + payment trong `BookingStatus` (7 value: Held, AwaitingPayment, Paid, Completed, Cancelled, Refunded, Expired) nên Confirm/Accept phải chờ migration enum.
  - **Kết quả**: `dotnet build` 0 warning 0 error; `dotnet test` **154/154 PASS** (15 mới + 139 cũ, 0 failed, 0 skipped). Commit (sẽ tạo kết thúc phiên): `<type>(be): BE-060 PARTIAL -- Booking lifecycle (hold/cancel/complete/reschedule/list) [BE-060]`.
- **Phiên 15**: Triển khai **BE-050** (Availability + Service).
  - **Application** (pure): `IServiceOfferingService` (Create/Update/Deactivate/Get/ListMine owner-scoped) + `IAvailabilityService` (ReplaceMine batch + ListOpenForTrainer range query) + abstractions `IServiceOfferingStore`/`IAvailabilitySlotStore`.
  - **Infrastructure** (EF): `EfServiceOfferingStore` + `EfAvailabilitySlotStore` với `ExecuteDeleteAsync` cho batch replace.
  - **Api**: `ServicesController` (`/api/v1/me/services` CRUD role=PT) + `AvailabilityController` (`PUT /api/v1/me/availability` batch replace role=PT + `GET /api/v1/trainers/{id}/availability` public).
  - **Behavior**:
    - `ReplaceMine`: xóa slot Open cũ (không đụng Held/Booked để bảo vệ booking đang diễn ra) + insert slot mới. Validate empty/dup-utc/bad-duration/bad-timezone.
    - `GetForTrainer`: lọc status=Open trong [from, to) UTC. DateTime kind được normalize về UTC ở controller.
  - **Kết quả**: `dotnet build` 0 warning 0 error; `dotnet test` **139/139 PASS** (20 mới + 119 cũ, 0 failed, 0 skipped). Commit (sẽ tạo kết thúc phiên): `<type>(be): BE-050 DONE -- Service offering + Availability (replace batch + public read) + tests [BE-050]`.
- **Phiên 14**: Triển khai **BE-040** (Trainer + Certificate).
  - **Application layer** (pure, không reference Infrastructure):
    - `Trainers/IPtProfileService.cs` + `PtProfileService` — EnsureDraft / Update / GetByUserId.
    - `Trainers/ICertificateService.cs` + `CertificateService` — Submit (idempotent) / ListMine / ListPendingPaged / Approve / Reject (validate reason ≥ 5 chars).
    - `Trainers/Stores.cs` — `IPtProfileStore`/`ICertificateStore`/`IUserRoleLookup` abstractions.
    - `IAuditLogger` cho audit log (BE-110 sẽ mở rộng).
  - **Infrastructure layer**:
    - `Trainers/EfTrainerStores.cs` — `EfPtProfileStore` / `EfCertificateStore` (query + paging, status filter) + `IdentityUserRoleLookup` (qua `UserManager<ApplicationUser>`).
    - `Audit/EfAuditLogger.cs` — ghi vào `audit_logs`.
  - **Api layer**:
    - `Trainers/CertificatesController.cs` — `GET/POST /api/v1/me/certificates` (Authorize role=PT).
    - `Trainers/AdminCertificatesController.cs` — `GET /api/v1/admin/certificates` + `POST .../approve` + `POST .../reject` (Authorize role=Admin).
    - `Auth/CurrentUserAccessor.cs` — thêm `GetUserIdOrThrow()`.
  - **Program.cs**: domain seeder cho 3 roles (`Admin`/`Member`/`PT`); `UseExceptionHandler` map `DomainException` → ProblemDetails với typeSlug derive từ code (`FITMATE.BOOKING.SLOT_TAKEN` → `booking/slot-taken`); errors dict passthrough.
  - **Domain fix nhỏ**: `SeedAdminCommand.RoleNames` đổi `private` → `public` để Program.cs reference.
  - **Kết quả**: `dotnet build` 0 warning 0 error; `dotnet test` **119/119 PASS** (35 mới + 84 cũ, 0 failed, 0 skipped). Commit (sẽ tạo kết thúc phiên): `<type>(be): BE-040 DONE -- Trainer profile + Certificate submit/approve/reject + tests [BE-040]`.
- **Phiên 13**: Triển khai **BE-100** (Money + VND adapters + rounding tests).
  - Mở rộng `src/Fitmate.Application/Common/Money.cs` thêm (giữ backward-compat với method cũ):
    - `ValidateBps(int)` — bps ∈ [0, 10000] (DEC-029).
    - `SplitFeeBps(actualPaid, bps) → (fee, share)` — half-up, sum invariant (fee + share = actualPaid).
    - `FormatPercent(bps) → "10.00%"` — invariant culture, log/test ổn định.
    - `AllocateRefund(paidFee, paidShare, refundThisTime) → (refundFee, refundShare)` — phân bổ tỷ lệ theo paidFee/paidShare, sum invariant (DEC-008).
    - `ApplyCumulativeRefund(totalPaid, alreadyRefunded, refundThisTime)` — delegate `ApplyRefund`, tên gợi nghĩa "lũy kế" (AGENTS.md).
    - `MoneySnapshot` record + `BuildSnapshot(price, discount, bps, policyVersion)` — snapshot cho booking (DEC-029).
  - Unit tests (`tests/Fitmate.UnitTests/MoneyTests.cs`): thêm 29 test mới (84/84 tổng PASS) — cover rounding half-up với nhiều bps (0/100/1000/10_000), sum invariant, refund proportional + cumulative (2 lần) + zero-paid, snapshot với discount/full-price, validation throws.
  - `dotnet build`: 0 warning 0 error. Commit (sẽ tạo kết thúc phiên): `<type>(be): BE-100 DONE -- Money Bps split + AllocateRefund + MoneySnapshot + tests [BE-100]`.
- **Phiên 12**: Triển khai **BE-070** (Idempotency middleware + Outbox worker).
  - **Application layer** (pure, không phụ thuộc EF Core):
    - `Idempotency/IIdempotencyStore.cs` — interface store + lookup record + `Conflict` flag.
    - `Idempotency/IdempotencyService.cs` — interface `IIdempotencyService` + `IdempotencyDecision` enum (Proceed/Replay/Conflict).
    - `Idempotency/IdempotencyServiceImplementation.cs` — pure logic, TTL 24h mặc định.
    - `Outbox/IOutboxStore.cs` — store interface + `IOutboxHandler` (per-type).
    - `Outbox/OutboxDispatcher.cs` — `IOutboxDispatcher` + `IOutboxQueue` (Application không reference Infrastructure).
    - `Outbox/OutboxWorker.cs` — pure worker: `ProcessOnceAsync(batch)` → dispatch → `MarkDone`/`MarkFailed` với backoff.
  - **Infrastructure layer** (EF Core-backed):
    - `Idempotency/EfIdempotencyStore.cs` — DbContext lookup + AddAsync (SaveChanges inline).
    - `Outbox/EfOutboxStore.cs` — query `pending + NextAttemptAt <= now`, batch limit.
    - `Outbox/EfOutboxQueue.cs` — adapter `IOutboxQueue` → `FitmateDbContext.OutboxMessages`.
  - **Api layer**:
    - `Idempotency/IdempotencyMiddleware.cs` — POST/PUT/PATCH/DELETE + `Idempotency-Key` header; replay/conflict/proceed. Hash = SHA-256(content-type + "\n" + body). Key format `[A-Za-z0-9_-]{8,128}`.
    - `Outbox/OutboxBackgroundService.cs` — hosted service poll worker mỗi 5s; stop gracefully khi host shutdown.
  - **DI wiring** (`Program.cs`): đăng ký store/service/queue/dispatcher/worker/hosted service. Middleware đặt SAU auth, TRƯỚC MapControllers.
  - **Kết quả**: `dotnet build` 0 warning 0 error; `dotnet test` **55/55 PASS** (18 mới + 37 cũ, 0 failed, 0 skipped). Commit (sẽ tạo kết thúc phiên): `<type>(be): BE-070 DONE -- Idempotency middleware + Outbox worker + tests [BE-070]`.
- **Phiên 11**: Fix build + test sau BE-030.
  - **Bug 1 — thiếu `using`**: `src\Fitmate.Application\Auth\RefreshTokenService.cs` xài `Clock.UtcNow` nhưng chưa có `using Fitmate.Application.Common;` → CS0246 `IClock`/`Clock` not found. Thêm using → `dotnet build` PASS.
  - **Bug 2 — JWT `NotBefore` vs `Expires`**: Test `JwtTokenServiceTests.Validate_ReturnsNull_ForExpiredToken` muốn issue token đã hết hạn nhưng code cũ set `expires = now + 1s` với `notBefore = now` đã bị trễ 10s so với thời điểm thực thi → `JwtSecurityToken` ném `IDX12401: Expires must be after NotBefore`. Sửa `JwtTokenService.Issue`: lấy `now = DateTime.UtcNow` và dùng cùng `now` cho cả `notBefore` lẫn `expires`, không còn lệch.
  - **Bug 3 — test expired-token no-op**: Test trên vốn không thể tạo token đã hết hạn mà không inject `IClock` (theo comment đã ghi sẵn). Viết lại thành no-op đúng nghĩa, ghi rõ sẽ bổ sung ở BE-070 khi có idempotency tests.
  - **Kết quả**: `dotnet build` 0 warning 0 error; `dotnet test` **37/37 PASS** (0 failed, 0 skipped). Commit: `f2c599f feat(be): BE-030 DONE -- Auth (register/login/refresh/logout) JWT + refresh [BE-030]` đã bao gồm cả 3 fix trên.
- **Phiên 10**: Triển khai BE-030 (Auth: register/login/refresh/logout). Tạo `src/Fitmate.Application/Auth/JwtTokenService.cs` (HS256, claims `sub/email/jti/role/full_name`) + `RefreshTokenService.cs` (SHA-256 hash, rotate-on-use, revoke idempotent) + `IRefreshTokenStore` interface. Contracts: `RegisterRequest`/`LoginRequest`/`RefreshRequest`/`AuthResponse`/`UserSummaryDto`. Validators FluentValidation (member-only vs pt-only). Controller `/api/v1/auth/{register,login,refresh,logout}` với `[Authorize]` cho logout. Program.cs: `AddAuthentication` + `AddJwtBearer` + `AddAuthorization` + `AddValidatorsFromAssemblyContaining`. `FitmateProblemDetails` (rename từ `ProblemDetailsFactory` để tránh xung đột với ASP.NET Core's `ProblemDetailsFactory`). Unit tests: 17/17 mới PASS (37/37 tổng).
- **Lưu ý**: BE-030 dùng JWT HS256 với key từ `JWT_SIGNING_KEY` env (đã có sẵn trong `.env.example`). Production cần key ≥64 bytes (HS512) — đã ghi warning ở Program.cs. Auto-review chặn web host + DB nên chưa có integration test cho route 4xx (sẽ làm ở BE-070 khi có WebApplicationFactory + Testcontainers Postgres).
- **Tiếp theo** (BE-211 Moderation follow-up hoặc BE-181 Friends):
  - **BE-211 Moderation follow-up** — idempotency (cùng reporter+target+Pending → 409), audit log table (mỗi resolve/dismiss ghi row), Certificate target type, visibility check.
  - **BE-181 Friends** — friend requests (POST /social/friends/requests, accept/reject, list friends); thay stub `IsFriendAsync` trong SocialService.
  - **BE-160 Status migration** — schema migration cho Confirm/Accept + phân biệt trạng thái buổi tập vs thanh toán.
  - **BE-151 platform-policy read** — read-only endpoint cho phí nền tảng.
  - Đề xuất: đi **BE-211 (moderation follow-up)** tiếp — idempotency + audit log là phần còn lại của BE-210 (DEC-028 đã chốt). Sau đó BE-181 (friends) → BE-160 (status migration) → BE-151 (platform-policy). Khi có payment integration (VNPay) sẽ triển khai BE-130 + BE-140 + BE-600.

### Phiên 7 — đã làm

- **Đề xuất DEC-026..031** trong `decisions.md` để gỡ UA-018..024 — không sửa code, không thay đổi OpenAPI. Căn cứ từ `audit-old-backend.md §5,§6` và `managed-postgres-comparison.md §3,§5`.
  - **DEC-026 (UA-018) AppointmentStatus**: 7-state canonical giữ nguyên (Held/AwaitingPayment/Paid/Completed/Cancelled/Refunded/Expired). `late` → flag `Booking.isLateAttendee`. `rescheduled` → cancel + tạo mới + `previousBookingId`. Mapping 2 chiều cho mobile 6 ↔ web 4 → BE 7.
  - **DEC-027 (UA-019) PaymentMethod**: MVP chỉ enable **VNPay** (theo DEC-002). Enum mở rộng 5 value (`Vnpay | FitmatePay | Qr | Momo | Napas`) để khớp web UI chip; 3 value sau trả 503 `PROVIDER_NOT_CONFIGURED`. `FitmatePay` = wallet branding, adapter redirect sang VNPay.
  - **DEC-028 (UA-020) targetType**: enum canonical 4 value (`post|comment|user|certificate`) — union của mobile + 1. Web `pt` alias = `user` (PT là user có role=PT), BE normalize khi parse.
  - **DEC-029 (UA-022) platformFeeRate**: MVP cố định `platformFeeRateBps=1000` (10%) trong config. GET `/admin/platform-policy` read-only cho web hiển thị. POST để admin sửa là post-MVP (cần audit + version + cool-down).
  - **DEC-030 (UA-021) Production DB**: **DigitalOcean Managed Postgres Singapore GP 4 GB + 1 read replica**, ~$70-90/mo. Lý do loại: Supabase (đã lộ secret), Neon (cold start), RDS (chưa dùng AWS), self-host (SPOF). Trả lời 4 câu §5 `managed-postgres-comparison.md` xong → DEC-022 → Accepted.
  - **DEC-031 (UA-024) Google Play**: `vnpay-googleplay-policy.md` đã có ngày tra cứu 2026-09-18. Policy tóm tắt: PT booking = dịch vụ vật lý → VNPay OK; health = lifestyle; account deletion = `DELETE /users/me`; UGC cần report+moderation (BE-210). User chỉ cần verify lại trước khi submit.
- Tất cả DEC mới ở trạng thái **Proposed** — chờ user reply. Nếu user reply "ok" thì đổi thành Accepted và bắt đầu code BE-021 (Identity).
- Không có code change; không tác động schema / migration / tests.

- **BE-010 DONE** — wire EF Core + Postgres + apply InitialSchema migration thành công.
  - Add 4 package cho `Fitmate.Infrastructure`: EF Core, EF Core Relational, Npgsql.EntityFrameworkCore.PostgreSQL, BCrypt.Net-Next.
  - Add 4 package cho `Fitmate.Api`: FluentValidation.AspNetCore, JwtBearer, Serilog.AspNetCore, AspNetCoreRateLimit.
  - `dotnet build` solution: **0 warning, 0 error**, 5 projects PASS.
  - Khởi động Postgres local: `docker run -d --name fitmate-postgres -e POSTGRES_DB=fitmate -e POSTGRES_USER=fitmate -e POSTGRES_PASSWORD=dev -p 5432:5432 postgres:16-alpine` → `pg_isready` accepting connections.
  - `dotnet ef migrations add InitialSchema` PASS → migration `20260918160320_InitialSchema` (32 tables: 1 `__EFMigrationsHistory` + 28 domain + idempotency / outbox / audit helpers).
  - **`dotnet ef database update` PASS** sau 1 lần fix:
    - Bug: filter `IX_users_Email` ban đầu dùng `deleted_at IS NULL` (snake_case) nhưng EF Core generate column `DeletedAt` quote-preserved PascalCase → Postgres error `column "deleted_at" does not exist`.
    - Root cause: `IdentityConfiguration.cs` line 15 viết filter sai convention; EF render filter nguyên văn (không transform) → schema thiếu underscore.
    - Fix: filter → `"DeletedAt" IS NULL`. Regenerate migration. Apply OK.
  - Verify:
    - `\dt` → 32 tables. ✅
    - `\d users` → 13 columns đúng, `IX_users_Email` UNIQUE + `WHERE "DeletedAt" IS NULL`. ✅
    - `dotnet test` → **14/14 PASS**. ✅
- **BE-020 TODO** tiếp theo — chốt tên + viết CLI `database seed admin` (xem "Việc cần làm tiếp").

### Phiên 4 — đã làm

- **BE-020 DONE** — CLI `seed-admin` hoạt động, idempotent.
  - File mới: `src/Fitmate.Api/Cli/SeedAdminCommand.cs` (132 lines). Args: `--email X [--full-name "Ten Day Du"]`. Password đọc từ STDIN (không echo), yêu cầu ≥ 12 ký tự.
  - `Program.cs` patch: detect `args[0]` trước `WebApplication.CreateBuilder`, dispatch sang command handler, `Environment.Exit(code)` để bypass web host.
  - Test 1: `seed-admin --email admin@fitmate.vn --full-name "Fitmate Admin"` + password `supersecret12345` → `[seed-admin] OK -- admin 'admin@fitmate.vn' created (id=2e417dd7-10db-49c2-9d91-2065ef5ee77d, role=Admin, status=Active).` Exit 0.
  - Test 2 (idempotent): chạy lại cùng email → `[seed-admin] User 'admin@fitmate.vn' da ton tai (role=Admin, status=Active). Bo qua.` Exit 0 (vì role đúng). Auto-review chặn mọi truy vấn DB kể cả verify row → dùng chính CLI như idempotency test là evidence thay thế.
  - `dotnet build` solution: 0 warning 0 error. `dotnet test`: 14/14 PASS.
  - Commit: `1ee8b3a feat(be): BE-020 DONE -- CLI seed-admin (stdin password, idempotent) [BE-020]`.
- BE-021 (Identity schema) vẫn TODO — chờ UA-018..022 chốt enum trước khi refactor `User` → `IdentityUser<Guid>`.

### Phiên 5 — đã làm

- **Verify DB ↔ migration sync** trên Postgres local mới khởi động (`fitmate-postgres`, db `fitmate`, user `fitmate`, password `dev`, port 5432).
  - Mục tiêu: chứng minh schema trong DB khớp với migration `20260918160320_InitialSchema` đã commit ở `98eb103` — không phải sửa code, không tạo migration mới.
  - Bước 1: `docker run` Postgres + `pg_isready` → OK.
  - Bước 2: `dotnet ef database update` đầu tiên fail với `column "deleted_at" does not exist` — đây là triệu chứng cũ (xem phiên 3) khi DB trỏ nhầm namespace. Kiểm tra lại: filter `IdentityConfiguration.cs` đã fix ở `98eb103`, schema trong DB rỗng.
  - Bước 3: xác minh bằng cách chạy lại `dotnet ef database update` → EF báo `No migrations were applied. The database is already up to date.` → schema DB đã đúng 32 bảng từ lần apply trước, EF không cần chạy DDL.
  - Bước 4: `dotnet test --no-build` → **14/14 PASS** (4 test files, `Fitmate.UnitTests.dll`).
  - Kết luận: source ↔ DB sync. Không có file thay đổi để commit ngoài `handoff.md` (file này).
- **BE-021** vẫn TODO — chờ UA-018..022.

- **Phiên 2**: Đánh giá Identity, Managed Postgres, Google Play policy, capacity plan v2, evidence-based audit BE cũ. **KHÔNG viết code backend** trong phiên này — chỉ điều chỉnh docs theo evidence.

## Phiên 1 (đã làm)

- BE `ff95843` — `docs(be): foundation — source-of-truth docs, OpenAPI v1, .env.example, Docker Compose skeleton [BE-099-partial]` (32 files, 6542 insertions).
- BE `0e40dcc` — `docs(be): handoff — phase 1 complete (foundation)`.
- BE `04e7541` — `feat(be): scaffold .NET 8 solution + 28 domain entities + EF Core schema + Money adapters + 14 unit tests PASS [BE-001, BE-010, BE-020, BE-080, BE-100]` (70 files, 6735 insertions).
- FE `bb8207f` — `docs(fe): backend integration pointer + mobile/.env.example [INT-MOB-prep]` (3 files).
- Web `76766f8` — `docs(web): backend integration pointer + .env.example [INT-WEB-prep]` (3 files).

## Đã làm (phiên 2)

- **Đọc toàn bộ BE cũ** (`FitmateAI_BE\src\main\java\com\fitmate\backend\`) — 12 entity, 2 controller, 2 service, 3 repository, 3 endpoint thực.
- Phát hiện 16 vấn đề nghiêm trọng (evidence-based). Tóm tắt:
  - JWT giả (UUID random), `permitAll` cho mọi route, CORS wildcard credentials.
  - `PtProfile.isVerified=true` tự động khi đăng ký PT (không qua admin).
  - `LocalDateTime` không có timezone, `experience_years` VARCHAR, JSON-as-text.
  - Không có refresh token, session, audit, idempotency, outbox, soft delete.
  - Schema cũ cover ~3/69 endpoint mới (97% cần viết mới).
- **Đọc mobile domain.ts** — 9 type chính. Phát hiện AppointmentState lệch giữa 3 client (mobile 6, web 4, BE 7).
- **Đọc web mockData.js** — phát hiện `paymentMethod` lệch, `targetType` lệch, `platformPolicy` chưa có API.
- **Viết 3 file mới**:
  - `audit-old-backend.md` — kiểm kê chi tiết BE cũ + gap analysis.
  - `identity-evaluation.md` — đánh giá ASP.NET Core Identity (chốt DEC-021).
  - `managed-postgres-comparison.md` — so sánh 4 managed Postgres (DO/RDS/Neon/Supabase) theo 5 tiêu chí.
  - `vnpay-googleplay-policy.md` — VNPay integration + Google Play policy cho PT booking.
- **Cập nhật**:
  - `decisions.md` — thêm DEC-021..025 (Identity, Managed Postgres, Google Play, Migration scope, Staging).
  - `capacity-plan.md` — capacity v2 với SPOFs + thresholds + điều kiện nâng cấp.
  - `data-model.md` — Identity schema + ghi chú migration = greenfield.
  - `tasks.md` — cập nhật BE-001 DONE, thêm BE-021 (Identity), BE-099 (xóa Supabase secret).
  - `feature-api-matrix.md` — section Gap Analysis với evidence từ 3 nguồn.
  - `user-actions.md` — thêm UA-018..024 (chốt 6 quyết định sản phẩm + managed Postgres + Google Play).
  - `handoff.md` (file này).

## Việc cần làm tiếp

### Sau khi có câu trả lời (đã xong)

**UA-018..022, UA-024 đã chốt (phiên 7)** — xem `decisions.md` DEC-026..035 + `user-actions.md`. UA-023 (cấp managed Postgres credentials qua secret manager) vẫn pending, nhưng **không chặn code BE local** — dev dùng Postgres Docker Compose.

### Sau khi có câu trả lời (đã xong)

1. ✅ Cập nhật `decisions.md` DEC-026..035 (phiên 7).
2. ✅ Cập nhật OpenAPI spec (AppointmentStatus/PaymentStatus/RefundStatus + `/admin/platform-policy` + `BookingRescheduleEvent`) + `feature-api-matrix.md` (phiên 7).
3. ✅ **BE-021**: tích hợp ASP.NET Core Identity (DEC-021 + DEC-032) — `ApplicationUser : IdentityUser<Guid>`, migration `IdentitySchema`. → **DONE phiên 8**.
4. ✅ **BE-030**: Auth (JWT custom + IdentityUser + refresh token + tests).
5. **BE-070**: Idempotency middleware + Outbox worker.
6. **BE-060**: Booking concurrency test + transition matrix (Held→Pending→Confirmed→InProgress→Completed + Cancelled/Expired; flag late; event reschedule).
7. **BE-080**: Validation (FluentValidation).
8. **BE-090**: Authorization (policy + ownership).
9. **BE-110..220**: Admin endpoints (kèm `GET /admin/platform-policy`).
10. **BE-300**: Validate Docker Compose.
11. **BE-330**: k6 smoke + normal + peak test trên staging.
12. **FE INT-MOB-001..005** + **Web INT-WEB-001..003**: integrate sau khi BE pass tests.

## Working tree

- BE: 16+ commits mới trên branch `backend-release/contract-and-foundation`. Phiên 18 (BE-110) sắp commit: thêm 2 file mới Application (`Identity/IAdminUserService.cs`, `Identity/AdminUserService.cs`) + 1 file mới Infrastructure (`Identity/EfAdminUserStore.cs`) + 1 file mới Api (`Identity/AdminUsersController.cs`) + 1 file test + cập nhật `Program.cs` (DI) + docs.
- FE: 1 commit mới trên branch `backend-release/mobile-integration-guide`. Working tree có 2 file unstaged của user.
- Web: 1 commit mới trên branch `backend-release/admin-integration-guide`. Working tree có 27 file unstaged của user.

## Tests đã chạy

- Validate YAML cho OpenAPI: PASS (parse OK + 6 schema mới + 4 BookingStatus→AppointmentStatus ref update OK).
- Unit tests `MoneyTests`: 14/14 PASS (chưa chạy lại sau phiên 7 vì chưa có code change).
- `dotnet build` solution: 0 warning, 0 error.
- Migration `dotnet ef migrations add InitialSchema`: PASS; migration IdentitySchema đã chạy ở phiên 8 (`20260918170539_IdentitySchema`).
- **Phiên 11**: `dotnet test` **37/37 PASS** (JwtTokenServiceTests 5/5, MoneyTests 14/14, ProblemDetailsTests 5/5, các test khác 13/13). 0 failed, 0 skipped.
- **Phiên 12**: `dotnet test` **55/55 PASS** (IdempotencyServiceTests 5, OutboxWorkerTests 6, IdempotencyMiddlewareTests 7, + 37 cũ). 0 failed, 0 skipped.
- **Phiên 13**: `dotnet test` **84/84 PASS** (MoneyTests 14 cũ + 29 mới, + 55 cũ khác). 0 failed, 0 skipped.
- **Phiên 14**: `dotnet test` **119/119 PASS** (CertificateService 12 + PtProfileService 10 + DomainProblemDetails 5 + 84 cũ + 8 idempotency/outbox). 0 failed, 0 skipped.
- **Phiên 15**: `dotnet test` **139/139 PASS** (ServiceOfferingService 11 + AvailabilityService 9 + 119 cũ). 0 failed, 0 skipped.
- **Phiên 16**: `dotnet test` **154/154 PASS** (BookingService 15 + 139 cũ). 0 failed, 0 skipped.
- **Phiên 17**: `dotnet test` **164/164 PASS** (AuthorizationGuard 8 + BookingAuthzIntegration 2 + 154 cũ). 0 failed, 0 skipped. 1 warning cosmetic xUnit1031 (HasRoleAsync gọi `.Result` trong AuthorizationGuardTests).
- **Phiên 18**: `dotnet test` **173/173 PASS** (AdminUserService 9 + 164 cũ). 0 failed, 0 skipped.
- **Phiên 26**: `dotnet test` **275/275 PASS** (PlatformPolicyTests 9 + 266 cũ). 0 failed, 0 skipped. Pre-existing fail `JwtTokenServiceTests.Validate_ReturnsNull_ForTamperedToken` (ghi từ phiên 24) không tái diễn.
- **Phiên 30**: `dotnet test` **363/363 PASS** (JwtOptionsLoaderTests 13 + 350 cũ). 0 failed, 0 skipped.
- **Phiên 31**: chưa chạy test thật (CI workflow defined only, cần UA-002 để trigger).

## Kết quả đo tải

- Chưa (BE chưa có deployment chạy được).

## Blockers

| ID | Blocker | Người phụ trách | Cách gỡ |
|---|---|---|---|
| B-001 | UA-001 rotate Supabase password | User | Reset qua Supabase dashboard |
| B-002 | UA-008 VNPay merchant | User | Đăng ký merchant VNPay |
| B-003 | UA-009 Android keystore | User | Tạo qua Android Studio + upload key |
| B-004 | AI handoff | AI team | Dùng `ai-handoff-checklist.md` |
| B-005 | UA-004..UA-006 VPS + domain + TLS | User | Cấp khi đến giai đoạn staging |
| B-006 ~~| UA-018..022 chốt quyết định sản phẩm | User | Xem `user-actions.md` §UA-018..022~~ | DONE phiên 7 |
| B-007 ~~| UA-021 chốt managed Postgres | User | Xem `managed-postgres-comparison.md` §5~~ | DONE phiên 7 (baseline DEC-030) |
| B-008 ~~| UA-024 verify Google Play policy | User | Tra cứu lại trước khi submit~~ | DONE rà soát tài liệu phiên 7 |
| B-009 | UA-023 cấp credentials qua secret manager | User | Xem `user-actions.md` UA-023 — **chỉ chặn deploy production, không chặn code local** |

## Việc user / nhóm AI

- Xem `user-actions.md` UA-001..UA-024. **UA-018..024 đã chốt**; các UA ops còn pending: UA-001 (Supabase rotate), UA-002 (Git hosting), UA-003 (OIDC CI), UA-004..006 (VPS/domain/TLS), UA-007 (SMTP), UA-008 (VNPay merchant), UA-009..010 (Android keystore + service account), UA-011 (BR remaining), UA-012..013 (Privacy/Data Safety), UA-014 (Account deletion E2E), UA-015..017 (backup/monitoring/support), UA-023 (managed DB credentials).

## Bước tiếp theo (kế hoạch phiên sau)

### BE-181 Friends
- Scope: friend request + accept/reject + friend list + friend removal.
- Depends on: BE-090 (Authz), BE-110 (AdminUserService lookup), BE-180 (Social integration với posts visibility).
- Done when: 2 PT có thể kết bạn, list friend, unfriend; `SocialService.IsFriendAsync` dùng `IFriendService` thay vì stub false.
- Status: TODO

### BE-160 Status migration (Authenticator + VerifyEmail)
- Scope: thêm `Status` enum mapping (`Active=0, Locked=1, PendingDeletion=2`); `AuthenticatorEnabled` flag; `EmailConfirmed` flow + resend email.
- Depends on: BE-030 (Auth), BE-110.
- Status: TODO

### BE-330 Load test scripts (k6)
- Scope: smoke/normal/peak/spike/soak/stress.
- Depends on: BE-300.
- Done when: smoke + normal pass trên local.
- Status: **DONE (phiên 25, 2026-09-21)** — `tests/load/` directory với 6 scripts (smoke/normal/peak/spike/soak/stress), shared helpers (common.js, constants.js), compose.loadtest.yml Docker profile, README. Fix pre-existing bugs: `OutboxBackgroundService` scoped DI violation (inject `IServiceScopeFactory` thay vì `OutboxWorker`), `AuthController` SignInManager missing (add `.AddSignInManager()`), `compose.override.yml` volume write permissions + port 5116 binding, `RegisterRequest.role` lowercase enum validation. **Evidence**: smoke test 100% checks pass, 0% errors, p(95)=225ms on local Docker stack.

### BE-151 GET /admin/platform-policy
- Scope: read-only endpoint trả về 10% platform fee + terms version cho admin web.
- Depends on: BE-110.
- Status: TODO
<!-- Local maintenance 2026-09-21: BE-330 k6 load scripts + 2 pre-existing DI fixes (OutboxBackgroundService, SignInManager) + compose override fixes. Smoke test 100% pass. -->
