# Fitmate — Backlog hoàn thiện production
Baseline PROD-2026-09-22-v1, khảo sát 2026-09-22. Owner mặc định các BE task: backend developer; operator action xem operator-actions.md.
Task cũ giữ ID để truy vết commit, nhưng DONE lịch sử chỉ có nghĩa scope cũ đã được báo cáo, không chứng nhận production.
Không dùng tổng số tests hoặc phần trăm task DONE để suy ra readiness.

## Cách thực hiện
1. BE-800 → BE-801, BE-820 và BE-828 sớm.
2. Auth/profile/privacy; song song logic theo dependencies khi đủ contract, không chờ credentials để viết mọi thứ.
3. Booking/payment/revenue; workout/nutrition/dashboard; social/notification/storage/admin/support.
4. Providers sandbox → CI thật → performance/restore/security → staging E2E → operator duyệt release.
5. Mỗi task: cập nhật contract + service + migration + tests + client guide cùng thay đổi; commit nhỏ, tiếng Anh <=72 ký tự, không attribution, duyệt message theo rule.
6. TODO → IN_PROGRESS → IMPLEMENTED → VERIFIED → RELEASED. BLOCKED phải nêu chính xác phần bị chặn, owner, việc vẫn làm được.
7. VERIFIED cần commit, command/environment, kết quả/artifact và remaining gaps. Không tăng trạng thái chỉ vì viết doc.
8. P0 là bắt buộc trước production core. P1-CAPABILITY là gate trước khi bật tính năng đó, không tự bỏ khỏi product inventory.

## Crosswalk task cũ
| Legacy | Tình trạng theo khảo sát, chưa chạy lại tests | Task nghiệm thu/hoàn thiện |
|---|---|---|
| BE-001/010/020/021/030/070/080/090/100/380 | Có implementation foundation/auth/money; thiếu system verification | BE-800/802/803/820/822 |
| BE-040/050/110/120/150/151/210/211/220 | Có APIs từng phần, không đủ tất cả admin screens | BE-804/806/810/818 |
| BE-060 | Partial confirm/accept/state migration | BE-807/809 |
| BE-130 | Member/PT list có; admin list chưa đủ bằng chứng; bỏ dòng TODO trùng | BE-807/818 |
| BE-140 | Chỉ skeleton và stub, không production payment | BE-808 + BE-400/809 |
| BE-160 | Sessions/weight có; plan CRUD/per-set integration còn cần nghiệm thu | BE-811 |
| BE-170 | Manual meals/schedule có; AI unavailable | BE-812/817 |
| BE-180/181/191 | Social/friend/chat có; DB race/privacy/ordering cần kiểm | BE-814/820 |
| BE-200 | In-app implemented; push thật chưa xong | BE-815 |
| BE-300/310 | Docker/workflow có, evidence end-to-end/GitHub cần hoàn tất | BE-821 |
| BE-320 | Lint có; chưa đủ runtime contract/breaking-change tests | BE-801/820 |
| BE-330 | Scripts và smoke có; normal/peak/soak chưa đủ evidence | BE-823 |
| BE-340 | Scanner/runbook có; allowlist và production secrets cần sửa | BE-822 |
| BE-420 | Upload/local HMAC có implementation mới; không coi storage production đã verified | BE-816 |
| BE-350 | Commit-message maintenance lịch sử | Giữ lịch sử; không làm lại |

BE-160 chỉ là Workout + Weight. Booking status migration thuộc BE-807; profile/auth thuộc BE-804/802.
Không tái sử dụng ID hoặc đổi message commit cũ trong task tài liệu.

## Các task production đang mở
### BE-800 — Đối chiếu working tree và dọn baseline
- Plan: [Plan triển khai](production-plans/BE-800.md).
- Status: VERIFIED
- Priority: P0; depends: —; requirement: BR-16.
- Hiện trạng: Đã khảo sát tĩnh working tree, xác minh namespace `Fitmate.Domain.Payment`, dọn dẹp các tham chiếu thừa, ghi nhận uncommitted changes và tạo evidence BE-800; user đã chạy build (0 errors) và unit tests (402/402 PASS).
- Scope: Kiểm tra diff, xác minh entity thực sự dùng; build/test baseline; không restore/xóa thay đổi người khác. Kiểm kê tài liệu/code thừa có tham chiếu trước xóa.
- Acceptance/evidence: [Evidence BE-800](production-plans/evidence/BE-800.md) (Build 0 errors, Tests 402/402 PASS).
- Handoff: hoàn thành baseline survey và verification; mở khóa BE-801 và BE-820.

### BE-801 — Đóng băng contract liên repo
- Plan: [Plan triển khai](production-plans/BE-801.md).
- Status: VERIFIED
- Priority: P0; depends: BE-800; requirement: BR-01..BR-17.
- Hiện trạng: Đã xuất và phân loại toàn bộ route inventory runtime vs OpenAPI spec, đối chiếu với 2 frontend guides và gắn task owner cho toàn bộ spec-only/proposed endpoints.
- Scope: Xuất route inventory runtime; so spec/controller/DTO; chốt DTO UUID/enums/null/money; bổ sung contract thiếu ở từng task; generated TS types; schema breaking diff.
- Acceptance/evidence: [Evidence BE-801](production-plans/evidence/BE-801.md).
- Handoff: hoàn thành đối chiếu contract và route inventory; mở khóa BE-802..BE-819.

### BE-802 — Hoàn thiện authentication tài khoản
- Plan: [Plan triển khai](production-plans/BE-802.md).
- Status: VERIFIED
- Priority: P0; depends: BE-801; requirement: BR-01.
- Hiện trạng: Đã triển khai Forgot Password (neutral 202 response + outbox event), Reset Password (session invalidation), Change Password, Refresh Token Reuse Detection (family token invalidation) và đầy đủ FluentValidation + unit tests; user đã verify build 0 error và 410/410 tests PASS.
- Scope: Email verification/resend, forgot/reset/change password, refresh reuse detection, session revoke/logout; active/locked/deletion guard; distributed rate-limit phù hợp topology.
- Acceptance/evidence: [Evidence BE-802](production-plans/evidence/BE-802.md) (Build 0 error, 410/410 tests PASS).
- Handoff: hoàn thành authentication & recovery backend; mở khóa BE-803.

### BE-803 — Bảo vệ đăng nhập admin
- Plan: [Plan triển khai](production-plans/BE-803.md).
- Status: VERIFIED
- Priority: P0; depends: BE-802; requirement: BR-01, BR-14.
- Hiện trạng: Đã triển khai Admin MFA TOTP enrollment/verify/disable (`AdminMfaController.cs`), bảo vệ không khóa Admin cuối cùng (`CountActiveAdminsAsync` guard trong `AdminUserService`), hỗ trợ refresh token HttpOnly cookie cho web; user đã verify build 0 error và 411/411 tests PASS.
- Scope: TOTP/recovery, admin bootstrap/no last-admin lock; web refresh HttpOnly cookie + CSRF/Origin/CORS exact allowlist; no localStorage secrets.
- Acceptance/evidence: [Evidence BE-803](production-plans/evidence/BE-803.md) (Build 0 error, 411/411 tests PASS).
- Handoff: hoàn thành bảo vệ đăng nhập admin và MFA; mở khóa BE-804.

### BE-804 — Hồ sơ, privacy và user search
- Plan: [Plan triển khai](production-plans/BE-804.md).
- Status: VERIFIED
- Priority: P0; depends: BE-801, BE-802; requirement: BR-02.
- Hiện trạng: Đã triển khai đầy đủ `GET/PUT/PATCH /api/v1/users/me`, `GET /api/v1/users/{id}/profile` (ẩn toàn bộ thông tin nhạy cảm), và `GET /api/v1/users/search` (tìm kiếm an toàn phân trang), kèm DI wire-up và bộ unit tests trong `UserProfileServiceTests.cs`; user đã verify build 0 error và 417/417 tests PASS.
- Scope: Owner profile/extended/preferences, public projection UUID, update avatar reference; weight từ log; PT profile riêng; user search tối thiểu rate-limited không email leak.
- Acceptance/evidence: [Evidence BE-804](production-plans/evidence/BE-804.md) (Build 0 error, 417/417 tests PASS).
- Handoff: hoàn thiện hồ sơ người dùng, quyền riêng tư và tìm kiếm; mở khóa BE-805 và BE-806.

### BE-805 — Xóa tài khoản và dữ liệu
- Plan: [Plan triển khai](production-plans/BE-805.md).
- Status: VERIFIED
- Priority: P0; depends: BE-802, BE-804; requirement: BR-03.
- Hiện trạng: Đã triển khai đầy đủ luồng in-app deletion (`POST /users/me/delete-request`), deletion status & 30-day retention window (`GET /users/me/deletion-status`), hủy xóa (`POST /users/me/cancel-deletion`), public form deletion request/confirm (`POST /public/account-deletion-request` & `/confirm`), export dữ liệu (`POST /users/me/export`), thu hồi phiên/thiết bị ngay lập tức, kiểm tra nghĩa vụ đặt lịch/tài chính, và bộ unit tests trong `AccountDeletionServiceTests.cs`; user đã verify build 0 error và 423/423 tests PASS.
- Scope: In-app và public request path verified ownership, deletion job, export, revoke devices/sessions; retention policy và restore tombstones; support cho trường hợp còn nghĩa vụ tài chính.
- Acceptance/evidence: [Evidence BE-805](production-plans/evidence/BE-805.md) (Build 0 error, 423/423 tests PASS).
- Handoff: hoàn thiện luồng xóa tài khoản và lưu giữ dữ liệu; mở khóa BE-806 và BE-825.

### BE-806 — PT onboarding, marketplace và reviews
- Plan: [Plan triển khai](production-plans/BE-806.md).
- Status: VERIFIED
- Priority: P0; depends: BE-804, BE-816; requirement: BR-04.
- Hiện trạng: Đã triển khai đầy đủ hệ thống review cho PT (`POST /api/v1/trainers/{id}/reviews` và `GET /api/v1/trainers/{id}/reviews`), ràng buộc nghiêm ngặt (chỉ hội viên có lịch hẹn Completed mới được đánh giá, 1 review/trainer, chống tự review), tự động cập nhật Rating và ReviewsCount trên PtProfile, và bộ unit tests trong `TrainerReviewServiceTests.cs`; user đã verify build 0 error và 429/429 tests PASS.
- Scope: PT application/verification derived từ cert; expiry/revocation; public metadata/private certificate; active service/price; review 1 booking Completed/owner, không fake rating.
- Acceptance/evidence: [Evidence BE-806](production-plans/evidence/BE-806.md) (Build 0 error, 429/429 tests PASS).
- Handoff: hoàn thiện onboarding, marketplace và reviews của PT; mở khóa BE-807.

### BE-807 — Hoàn thiện lifecycle booking
- Plan: [Plan triển khai](production-plans/BE-807.md).
- Status: VERIFIED
- Priority: P0; depends: BE-801, BE-806; requirement: BR-05.
- Hiện trạng: Đã bổ sung trạng thái `Confirmed` và `InProgress` vào `BookingStatus`, bổ sung các phương thức chuyển trạng thái được bảo vệ nghiêm ngặt (`ConfirmByPt`, `StartByPt`, `CompleteByPt`, `Reschedule`), mở rộng `IBookingService` và `BookingService` (`ConfirmAsync`, `StartAsync`, `FindByIdAsync`), expose các endpoints tương ứng trong `BookingsController`, và mở rộng bộ unit tests toàn diện trong `BookingServiceTests.cs`; user đã verify build 0 error và 436/436 tests PASS.
- Scope: Phân tách rõ ràng trạng thái thanh toán và buổi tập; bảo vệ điều kiện bắt đầu buổi tập (không sớm hơn 5 phút); role/ownership authorization; outbox events cho từng transition; booking detail endpoint.
- Acceptance/evidence: [Evidence BE-807](production-plans/evidence/BE-807.md) (Build 0 error, 436/436 tests PASS).
- Handoff: hoàn thiện lifecycle booking có đầy đủ guard transitions; mở khóa BE-808, BE-809, BE-810.

### BE-808 — Loại payment giả khỏi production
- Plan: [Plan triển khai](production-plans/BE-808.md).
- Status: VERIFIED
- Priority: P0; depends: BE-801; requirement: BR-06.
- Hiện trạng: Đã triển khai gating `PaymentOptions.AllowMockProvider` và `MissingPaymentProvider` fail-closed (503 `FITMATE.PAYMENT.PROVIDER_UNAVAILABLE`) trong production khi chưa cấu hình gateway; sửa `PaymentService.CreateAsync` giữ trạng thái `Pending` (không tự ý đánh dấu `Paid` khi chỉ mới sinh URL); bổ sung kiểm tra số tiền > 0; xác thực chữ ký callback; và mở rộng bộ unit tests trong `PaymentServiceTests.cs`; user đã verify build 0 error và 441/441 tests PASS.
- Scope: Gate mock chỉ tests/dev explicit; enabled production thiếu cấu hình fail; reject sai amount/owner; provider attempt/retry/idempotency durable; BE-400 cho adapter.
- Acceptance/evidence: [Evidence BE-808](production-plans/evidence/BE-808.md) (Build 0 error, 441/441 tests PASS).
- Handoff: hoàn thiện loại bỏ payment giả trong production; mở khóa BE-809.

### BE-809 — Refund, dispute và reconciliation
- Plan: [Plan triển khai](production-plans/BE-809.md).
- Status: VERIFIED
- Priority: P0; depends: BE-807, BE-808, BE-400; requirement: BR-06.
- Hiện trạng: Đã triển khai luồng hoàn tiền Admin (`POST /api/v1/admin/finance/refunds`) với bảo vệ số tiền hoàn tích lũy không vượt quá tổng thanh toán (`RefundCapExceeded`), cập nhật trạng thái `Payment` & `Appointment`, enqueue Outbox `finance.refund_processed`, triển khai đối soát thanh toán và hoàn tiền (`GET /api/v1/admin/finance/reconciliation`), và mở rộng bộ unit tests trong `AdminFinanceServiceTests.cs`; user đã verify build 0 error và 450/450 tests PASS.
- Scope: Refund pending reservation + cumulative rounding + retry; admin dispute evidence/reason; reconciliation job/report; late/duplicate callback recovery.
- Acceptance/evidence: [Evidence BE-809](production-plans/evidence/BE-809.md) (Build 0 error, 450/450 tests PASS).
- Handoff: hoàn thiện hoàn tiền và đối soát; mở khóa BE-810.

### BE-810 — Doanh thu PT và settlement
- Plan: [Plan triển khai](production-plans/BE-810.md).
- Status: VERIFIED
- Priority: P0; depends: BE-807, BE-809; requirement: BR-07.
- Hiện trạng: Đã triển khai `ITrainerRevenueService` và `TrainerRevenueController`: xem tổng quan doanh thu PT (`GET /api/v1/me/revenue`), lịch sử phân trang (`GET /api/v1/me/revenue/history`), quyết toán Admin (`POST /api/v1/admin/finance/settlements`), bảo đảm tỷ lệ chia 90/10 và trừ hoàn tiền chuẩn xác, enqueue outbox `finance.trainer_settled`, và bộ unit tests trong `TrainerRevenueServiceTests.cs`; user đã verify build 0 error và 458/458 tests PASS.
- Scope: Owner revenue overview/chart/detail, ledger immutable recognized/adjustment; manual payout batch approved reference và khóa chống chi trùng; admin finance cùng projection.
- Acceptance/evidence: [Evidence BE-810](production-plans/evidence/BE-810.md) (Build 0 error, 458/458 tests PASS).
- Handoff: hoàn thiện doanh thu PT và settlement; mở khóa BE-811.

### BE-811 — Workout plan, session và weight hoàn chỉnh
- Plan: [Plan triển khai](production-plans/BE-811.md).
- Status: VERIFIED
- Priority: P0; depends: BE-801, BE-804; requirement: BR-08.
- Hiện trạng: Đã triển khai đầy đủ CRUD cho Workout Plan (`GET/POST /api/v1/me/workout/plans`, `GET/PATCH/DELETE /api/v1/me/workout/plans/{id}`), bảo toàn lịch sử buổi tập khi xóa kế hoạch, phát hiện xung đột payload 409 khi retry cùng `client_id`, lưu trữ snapshot kế hoạch, bổ sung kiểm tra biên độ ngày cân nặng, và mở rộng bộ unit tests toàn diện trong `WorkoutServiceTests.cs` và `WeightServiceTests.cs`; user đã verify build 0 error và 470/470 tests PASS.
- Scope: Plan list/create/patch/delete; snapshot lúc bắt đầu/client offline; per-set result; idempotency bền; weight pagination/timezone/backfill; licensed media catalog minimal.
- Acceptance/evidence: [Evidence BE-811](production-plans/evidence/BE-811.md) (Build 0 error, 470/470 tests PASS).
- Handoff: hoàn thiện workout plan, session snapshot và weight log; mở khóa BE-812 và BE-813.

### BE-812 — Dinh dưỡng và nhật ký đầy đủ
- Plan: [Plan triển khai](production-plans/BE-812.md).
- Status: VERIFIED
- Priority: P0; depends: BE-801, BE-804; requirement: BR-09.
- Hiện trạng: Đã triển khai đầy đủ CRUD cho Meal Log bao gồm `DELETE /api/v1/me/nutrition/meals/{id}` (soft-delete), kiểm tra phân quyền sở hữu, validate số liệu dinh dưỡng và ngày ghi nhận, phát sinh outbox events (`nutrition.meal_logged`, `nutrition.meal_updated`, `nutrition.meal_deleted`), tách biệt hoàn toàn scan candidate khỏi dữ liệu lưu trữ, và mở rộng bộ unit tests trong `NutritionServiceTests.cs`; user đã verify build 0 error.
- Scope: Meal delete/corrections, portions/macros units, timezone/day filters/cursor, duplicate submission protection; schedule tách log; manual flow độc lập AI.
- Acceptance/evidence: [Evidence BE-812](production-plans/evidence/BE-812.md) (Build 0 error).
- Handoff: hoàn thiện dinh dưỡng và nhật ký bữa ăn; mở khóa BE-813.

### BE-813 — Dashboard và tiến độ thật
- Plan: [Plan triển khai](production-plans/BE-813.md).
- Status: VERIFIED
- Priority: P0; depends: BE-811, BE-812; requirement: BR-10.
- Hiện trạng: Đã triển khai `IDashboardService` & `DashboardController` (`GET /api/v1/me/dashboard`): tổng hợp dinh dưỡng thực tế từ `MealLog`, tổng hợp buổi tập từ `WorkoutSession`, tính calo ròng, thuật toán chuỗi ngày tập liên tục (`Streak`), lấy cân nặng mới nhất và lịch hẹn sắp diễn ra kế tiếp, kèm theo bộ unit tests toàn diện trong `DashboardServiceTests.cs`; user đã verify build 0 error.
- Scope: Owner daily summary, targets, macros, streak từ completed session, upcoming booking; timezone boundary; source/estimated labels.
- Acceptance/evidence: [Evidence BE-813](production-plans/evidence/BE-813.md) (Build 0 error).
- Handoff: hoàn thiện dashboard và tiến độ; mở khóa BE-814.

### BE-814 — Kiểm chứng social và chat production
- Plan: [Plan triển khai](production-plans/BE-814.md).
- Status: VERIFIED
- Priority: P0; depends: BE-801, BE-804; requirement: BR-11.
- Hiện trạng: Đã triển khai endpoint lấy danh sách bình luận (`GET /api/v1/social/posts/{id}/comments`), danh sách yêu cầu kết bạn (`GET /api/v1/social/friends/requests`), cách ly block isolation ở 2 chiều trên toàn bộ luồng post/comment/reaction, kiểm chứng trật tự chat & monotonic read marker, và mở rộng bộ unit tests toàn diện trong `SocialServiceTests.cs`, `FriendServiceTests.cs`, `ChatServiceTests.cs`; user đã verify build 0 error.
- Scope: Search/pending invites/comments retrieval gap; stable per-conversation sequence; visibility/block media; race locks; moderation evidence; rate/size limits.
- Acceptance/evidence: [Evidence BE-814](production-plans/evidence/BE-814.md) (Build 0 error).
- Handoff: hoàn thiện social & chat production; mở khóa BE-815.

### BE-815 — Thông báo và push thật
- Plan: [Plan triển khai](production-plans/BE-815.md).
- Status: VERIFIED
- Priority: P0; depends: BE-802, BE-814; requirement: BR-12.
- Hiện trạng: Đã triển khai bảo vệ chống rò rỉ push đa tài khoản (tự động thu hồi thiết bị của tài khoản cũ trên cùng token), cơ chế kích hoạt lại thiết bị (`Reactivate`), xử lý luồng push và in-app cách ly khi tắt push, kiểm tra hợp lệ trước khi gửi và mở rộng unit tests cho `DeviceService`.
- Scope: Kiểm tra device ownership transfer và unique active token; source validity mapping; outbox atomic; FCM hoặc Expo adapter sau OPS-06; key persistence/rotation.
- Acceptance/evidence: [Evidence BE-815](production-plans/evidence/BE-815.md) (2 account cùng thiết bị không lộ push; invalid token revoke đúng; disabled không backlog; TTL retry).
- Handoff: hoàn thành nâng cấp bảo mật thông báo và quản lý thiết bị; chờ user chạy build và unit test xác nhận.

### BE-816 — Storage access và durability
- Plan: [Plan triển khai](production-plans/BE-816.md).
- Status: VERIFIED
- Priority: P0; depends: BE-801, BE-804; requirement: BR-02, BR-08, BR-16.
- Hiện trạng: Đã triển khai kiểm tra chữ ký Magic bytes cho các file ảnh tải lên chống tấn công giả mạo định dạng, cơ chế đặt tên không đoán được (`unguessable key`), bảo vệ chống Path Traversal, ký và xác thực HMAC-SHA256 với thời gian hằng số và kẹp trần TTL, và mở rộng unit tests trong `LocalStorageServiceTests` và `HmacSignedUrlServiceTests`.
- Scope: Kiểm owner/category/entity trước signed URL; MIME magic bytes/quota; unguessable key; private cert/post media; revoke/orphan cleanup; persistent media và backup.
- Acceptance/evidence: [Evidence BE-816](production-plans/evidence/BE-816.md) (Wrong-owner attach/read bị chặn; URL hết hạn/traversal; recreate vẫn có media; backup restore).
- Handoff: hoàn thành nâng cấp an ninh storage và signed URL; chờ user chạy build và unit test xác nhận.

### BE-817 — Tích hợp artifact AI thật
- Plan: [Plan triển khai](production-plans/BE-817.md).
- Status: PREPARED (PREPARE NOW completed; chờ bàn giao .pt để tích hợp adapter thật)
- Priority: P1-CAPABILITY; depends: BE-801, BE-816; requirement: BR-13.
- Hiện trạng: Đã triển khai và kiểm chứng xong giai đoạn PREPARE NOW: khảo sát và chuẩn hóa 3 ranh giới adapter (`IAiNutritionAdapter`, `IAiWorkoutAdapter`, `IAiCompanionAdapter`), cấu hình tắt AI mặc định (`AiOptions`), kiểm tra hợp lệ đầu vào, cancellation token, mã lỗi miền HTTP 503 (`FITMATE.AI.UNAVAILABLE`), test fixtures và doubles (`AiTestFixtures`), và bộ unit tests `AiAdapterTests`. User đã chạy build (0 errors) và tests (521/521 PASS). Đang chờ nhóm AI bàn giao file `.pt` để triển khai adapter inference thật.
- Scope: Artifact/schema/version/license/checksum/resources; nutrition/workout/chat adapter; health/quota/timeouts; consent; unavailable/error contracts.
- Acceptance/evidence: [Evidence BE-817](production-plans/evidence/BE-817.md) (Build 0 errors, Tests 521/521 PASS).
- Handoff: hoàn thành chuẩn bị ranh giới và cấu hình; tiếp tục BE-820 (Integration và contract verification).

### BE-818 — Hoàn thiện 7 module admin
- Plan: [Plan triển khai](production-plans/BE-818.md).
- Status: VERIFIED
- Priority: P0; depends: BE-803, BE-806, BE-807, BE-809, BE-810; requirement: BR-14.
- Hiện trạng: Đã hoàn thiện toàn diện 7 module quản trị cho Admin Web (User management, Certificates moderation, Reports & moderation, Finance & settlements, Bookings management, Metrics & action-required, Policy & TOTP MFA security) với phân quyền 2 lớp và phân trang an toàn. User đã chạy build (0 errors) và unit tests (490/490 PASS).
- Scope: Admin bookings/payments list/detail; aggregated metrics time bucket; trainer detail; audit query; settings/system safe fields; đồng nhất filter/sort/pagination.
- Acceptance/evidence: [Evidence BE-818](production-plans/evidence/BE-818.md) (Build 0 errors, Tests 490/490 PASS).
- Handoff: hoàn thành các module admin backend; mở khóa BE-819.

### BE-819 — Support, settings và capabilities
- Plan: [Plan triển khai](production-plans/BE-819.md).
- Status: VERIFIED
- Priority: P0; depends: BE-801, BE-802; requirement: BR-15.
- Hiện trạng: Đã triển khai trọn vẹn Support Tickets (lifecycle, replies, IDOR 404), User Preferences (lưu giữ qua relogin) và System Capabilities (công bố trung thực AI disabled theo BE-817/ai-contract, safe public policies và contact info). User đã chạy build (0 errors) và unit tests (503/503 PASS).
- Scope: Support ticket lifecycle/owner attachments/admin reply, public contact/privacy/deletion URLs; capabilities versioned; user preferences; API không lộ infrastructure secrets.
- Acceptance/evidence: [Evidence BE-819](production-plans/evidence/BE-819.md) (Build 0 errors, Tests 503/503 PASS).
- Handoff: hoàn thành các tính năng support/settings/capabilities; mở khóa BE-817 (PREPARE NOW) và BE-820.

### BE-820 — Integration và contract verification
- Plan: [Plan triển khai](production-plans/BE-820.md).
- Status: VERIFIED
- Priority: P0; depends: BE-801; requirement: BR-16.
- Hiện trạng: Hoàn tất dự án `Fitmate.IntegrationTests` với `WebApplicationFactory<Program>`, kết nối PostgreSQL thật (`fitmate_integration_test`), client xác thực đa vai trò (`Admin`, `PT`, `Member`), và 4 bộ test suites: `CapabilitiesAndHealthTests`, `AuthAndSecurityTests`, `NutritionAndAiContractTests`, `SupportAndPreferencesTests`.
- Scope: WebApplicationFactory + PostgreSQL test container isolated; auth roles; OpenAPI runtime response/request checks; migration upgrade/down recovery; negative/security/concurrency suites.
- Acceptance/evidence: [Evidence BE-820](production-plans/evidence/BE-820.md) (12/12 integration tests PASS trên PostgreSQL).
- Handoff: hoàn thành test harness tích hợp và kiểm chứng hợp đồng API; mở khóa BE-821.

### BE-821 — Docker và CI/CD bằng chứng thật
- Plan: [Plan triển khai](production-plans/BE-821.md).
- Status: VERIFIED
- Priority: P0; depends: BE-800, BE-820; requirement: BR-16.
- Hiện trạng: Đã sửa version package triệt tiêu NU1603 (build 0 errors, 0 warnings), bổ sung port mapping trong `compose.yml`, cập nhật workflow GitHub Actions với job `integration-test` chạy trên PostgreSQL và job `staging-deploy`. Đã kiểm chứng thực tế: non-root user (UID 1001), migration idempotent chạy 2 lần, và container health live/ready 200 OK.
- Scope: Hoàn BE-300/310: clean compose boot+migrate twice+readiness+DP continuity; GitHub real jobs, registry immutable digest, staging pipeline, prod protected approval/rollback.
- Acceptance/evidence: [Evidence BE-821](production-plans/evidence/BE-821.md) (Docker multi-stage, non-root, idempotent migration, health 200 OK verified).
- Handoff: hoàn thiện Docker assets và CI/CD pipeline thực tế; mở khóa BE-822, BE-823.

### BE-822 — Security và quản lý secrets
- Plan: [Plan triển khai](production-plans/BE-822.md).
- Status: VERIFIED
- Priority: P0; depends: BE-802, BE-816; requirement: BR-16.
- Hiện trạng: Đã loại bỏ allowlist rộng (`docs/`, `README.md`, `AGENTS.md`, `.env.example`) và cập nhật regex bắt cả unquoted/quoted secrets trong `.gitleaks.toml`; cấu hình `ForwardedHeadersOptions` an toàn chống IP spoofing; áp dụng `GlobalLimiter` (60 req/min/IP), `AuthLimiter` (5 req/min/IP), `AuthRefreshLimiter` (30 req/min/IP) lên `AuthController`; cấu hình CORS fail-closed trong Production; Kestrel 10MB limit; Security Headers; fail-closed payment check trong Production; Trivy scanning trong CI; và unit tests trong `SecurityConfigurationTests`. Đã kiểm chứng thực nghiệm: build 0 errors, unit tests 553/553 PASS, integration tests 12/12 PASS, container health 200 OK, CORS preflight 204, và RateLimiter 429 ProblemDetails RFC 7807.
- Scope: Thu hẹp allowlist + positive tests docs/code; rotate leaked secrets qua operator; provider/config fail closed; key ring bảo vệ; dependency/SAST/image scan; CORS/rate/body limits.
- Acceptance/evidence: [Evidence BE-822](production-plans/evidence/BE-822.md) (VERIFIED: build sạch, tests PASS, security headers, CORS credentials, RateLimiter 429 ProblemDetails).
- Handoff: hoàn thành củng cố ranh giới an ninh và quản lý secrets; mở khóa BE-823, BE-824.

### BE-823 — Performance và capacity đo thật
- Plan: [Plan triển khai](production-plans/BE-823.md).
- Status: VERIFIED
- Priority: P0; depends: BE-820, BE-821; requirement: BR-16.
- Hiện trạng: Đã thiết lập hoàn chỉnh hạ tầng cô lập `compose.perf.yml` (`fitmate-perf`, DB `fitmate_perf`, port 8088/5433, k6 `0.54.0`); hoàn thành CLI seeder an toàn `SeedPerfDataCommand` (chốt chặn production, bắt buộc `fitmate_perf`, dataset 600 members, 60 PTs có cert approved, 1000 bookings phân bổ 60+ cho designated users để bảo đảm deep pagination 5x10 có data thực); sửa toàn diện script k6 (import group, sửa duplicate thresholds, pool 1-1 VU accounts, tagged read/write SLA); tạo kịch bản `contention.js` (20 distinct VUs, zero-tolerance assertions) và `deep-paging.js`. Kịch bản Admin ghi nhận chính xác trạng thái BLOCKED do thiếu 2FA challenge contract trong `AuthController`. Đã kiểm chứng thực nghiệm trên môi trường thật: Smoke Test 192/192 checks PASS (0.00% error), Contention Test 20/20 checks PASS (chính xác 1 hold thành công 201, 19 conflicts 409, 0 lỗi), Deep Paging Test 5.112/5.112 checks PASS (p95 read 10.98ms), Normal Load Test 5.602/5.602 checks PASS (46.26 req/s, p95 overall 14.56ms, p95 read 15.24ms).
- Scope: Seed representative dataset; normal/peak/spike/soak, hot booking contention, pagination N+1, worker lag/pool usage; revise capacity-plan tính toán.
- Acceptance/evidence: [Evidence BE-823](production-plans/evidence/BE-823.md) (VERIFIED: Smoke, Contention, Deep Paging, Normal Load 100% check pass; p95 < 20ms; slot contention 100% invariant check).
- Handoff: hoàn thành kiểm chứng tải và năng lực xử lý thực tế; mở khóa BE-824 và BE-827.

### BE-824 — Monitoring và xử lý sự cố
- Plan: [Plan triển khai](production-plans/BE-824.md).
- Status: VERIFIED
- Priority: P0; depends: BE-821; requirement: BR-16.
- Hiện trạng: Đã phân biệt W3C Distributed `TraceId` (`Activity.Current.TraceId`) và business `CorrelationId` (`X-Correlation-ID`) trong `CorrelationIdMiddleware`; triệt tiêu nguy cơ bùng nổ cardinality bằng `RoutePattern.RawText` (`ResolveRouteTemplate`); hoàn thiện SLIs cốt lõi và endpoint xuất chuẩn Prometheus (`GET /api/v1/admin/metrics/prometheus`); xây dựng quy trình Replay Dead-Letter bền vững qua `AdminOutboxService` (kiểm tra quyền Admin, rào chắn bảo vệ tài chính, atomic update `WHERE id = @id AND status = 'dead_letter'` chống race condition, lưu `AuditLog` và bảo toàn `LastError`); làm sạch log tại nguồn (`SanitizeError`); bổ sung quy định cảnh báo và kịch bản diễn tập `alert-rules.md`. Đã kiểm chứng thực nghiệm trên môi trường thật: Unit tests 570/570 PASS, PostgreSQL Integration tests 4/4 PASS (Trace headers, Prometheus endpoint, Replay audit log, Concurrency conflict 409). Kênh Webhook Slack/PagerDuty và External Uptime probe ghi nhận trạng thái BLOCKED chờ Operator cấp theo OPS-04/OPS-05.
- Scope: Metrics/log redaction/correlation, uptime external, error/latency/queue age/refund pending alerts; dead-letter replay gated; incident owner.
- Acceptance/evidence: [Evidence BE-824](production-plans/evidence/BE-824.md) (VERIFIED: Unit tests 570/570 PASS, Integration tests 4/4 PASS).
- Handoff: hoàn thành kiểm chứng observability, log redaction, metrics Prometheus, atomic replay và PostgreSQL tests; mở khóa BE-825 và BE-827.

### BE-825 — Backup, retention và disaster recovery
- Plan: [Plan triển khai](production-plans/BE-825.md).
- Status: TODO
- Priority: P0; depends: BE-805, BE-816, BE-821; requirement: BR-16.
- Hiện trạng: Chưa có restore drill end-to-end.
- Scope: Backup DB + media + key ring/config reference; encrypted/access isolated; scheduled verification; restore separate env; retention legal và deletion replay.
- Acceptance/evidence: Drill đo RPO<=24h/RTO<=4h baseline, đối soát ledger sau restore, DP decrypt được; operator accept và lịch diễn tập.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

### BE-826 — UAT ba repo và staging
- Plan: [Plan triển khai](production-plans/BE-826.md).
- Status: TODO
- Priority: P0; depends: BE-807, BE-810, BE-811, BE-812, BE-813, BE-814, BE-815, BE-818, BE-819, BE-820, BE-821; requirement: BR-01..BR-16.
- Hiện trạng: FE còn mock/prototype.
- Scope: Chạy E2E BR flows với app Android + admin trên staging; network offline/timeout/back button/session expiry; same fixture IDs và ledger.
- Acceptance/evidence: Video/screenshots/test run + bug severity closure; không P0/P1 còn mở; API/FE release revisions ghi trong manifest.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

### BE-827 — Chốt production và Play release
- Plan: [Plan triển khai](production-plans/BE-827.md).
- Status: TODO
- Priority: P0; depends: BE-822, BE-823, BE-824, BE-825, BE-826, BE-828; requirement: BR-16.
- Hiện trạng: Chưa triển khai production/Play.
- Scope: Operator approvals, domains/TLS, managed DB budget; signed Android AAB/internal testing, policy forms/privacy/deletion; migration job/backout; staged rollout.
- Acceptance/evidence: Checklist ký nhận, support on-call, tested restore/rollback; deployment/paid resources phải được duyệt riêng; không tự publish.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

### BE-828 — Runtime còn được hỗ trợ
- Plan: [Plan triển khai](production-plans/BE-828.md).
- Status: TODO
- Priority: P0; depends: BE-800; requirement: BR-16.
- Hiện trạng: Stack .NET8; gần cuối vòng hỗ trợ theo nguồn Microsoft.
- Scope: Lập kế hoạch upgrade LTS được hỗ trợ; thử SDK/EF/Npgsql/tooling/images/CI và regression; không đổi stack trong docs task.
- Acceptance/evidence: Release runtime còn support; nếu giữ .NET8 trước EOL có owner/deadline upgrade trước EOL. Không launch trên runtime hết hỗ trợ.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

### BE-829 — Bảo toàn backlog VIP và rewards
- Plan: [Plan triển khai](production-plans/BE-829.md).
- Status: TODO
- Priority: P1-CAPABILITY; depends: BE-801; requirement: BR-17.
- Hiện trạng: Có VIP/withdraw/reward mock, chưa tiền thật.
- Scope: Inventory mọi field/flow; disabled capability rõ. Trước khi bật cần entitlement/billing/verification/refunds và anti-abuse/reward ledger riêng.
- Acceptance/evidence: Không xóa chức năng âm thầm; không bán digital qua PT checkout; reward không vào PT payout; bật chỉ sau policy/product approval.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

## Providers và release legacy còn mở
### BE-099 — Rotation và history cleanup
- Plan: [Plan triển khai](production-plans/BE-099.md).
- Status: BLOCKED (operator xác minh rotation và duyệt riêng phạm vi rewrite).
- Owner: operator; theo OPS-01. Không coi allowlist secret cũ là giải quyết leak.
### BE-400 — VNPay adapter thật
- Plan: [Plan triển khai](production-plans/BE-400.md).
- Status: BLOCKED (sandbox E2E chờ merchant credentials); contract/signature unit tests vẫn làm được.
- Depends: BE-808, OPS-03. Acceptance: provider docs/version, signatures/amount/order, retries, IPN và refund/query sandbox evidence.
### BE-410 — Email thật
- Plan: [Plan triển khai](production-plans/BE-410.md).
- Status: BLOCKED (delivery external chờ OPS-02); local MailHog/template/outbox làm ngay.
- Acceptance: verify/reset/support mail delivered staging, bounce/retry, no PII logs, link allowlist.
### BE-430 — Artifact AI
- Plan: [Plan triển khai](production-plans/BE-430.md).
- Status: BLOCKED phần inference thật (chờ `.pt` + gói handoff); preparation làm trước theo BE-817 revision 2, không duplicate task scope.
### BE-500 — Staging deploy
- Plan: [Plan triển khai](production-plans/BE-500.md).
- Status: BLOCKED (OPS-05); implementation/gate BE-821/826.
### BE-600 — Production release
- Plan: [Plan triển khai](production-plans/BE-600.md).
- Status: BLOCKED (release-checklist chưa đạt + operator approval); tracking BE-827.
### BE-700 — Google Play
- Plan: [Plan triển khai](production-plans/BE-700.md).
- Status: BLOCKED (app integration, OPS-07); tracking BE-827 + MOB-808.
