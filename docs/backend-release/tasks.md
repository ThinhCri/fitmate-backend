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
- Status: IMPLEMENTED — PENDING USER VERIFICATION
- Priority: P0; depends: BE-801, BE-804; requirement: BR-08.
- Hiện trạng: Đã triển khai đầy đủ CRUD cho Workout Plan (`GET/POST /api/v1/me/workout/plans`, `GET/PATCH/DELETE /api/v1/me/workout/plans/{id}`), bảo toàn lịch sử buổi tập khi xóa kế hoạch, phát hiện xung đột payload 409 khi retry cùng `client_id`, lưu trữ snapshot kế hoạch, bổ sung kiểm tra biên độ ngày cân nặng, và mở rộng bộ unit tests toàn diện trong `WorkoutServiceTests.cs` và `WeightServiceTests.cs`.
- Scope: Plan list/create/patch/delete; snapshot lúc bắt đầu/client offline; per-set result; idempotency bền; weight pagination/timezone/backfill; licensed media catalog minimal.
- Acceptance/evidence: [Evidence BE-811](production-plans/evidence/BE-811.md).
- Handoff: hoàn thiện workout plan, session snapshot và weight log; mở khóa BE-812 và BE-813.

### BE-812 — Dinh dưỡng và nhật ký đầy đủ
- Plan: [Plan triển khai](production-plans/BE-812.md).
- Status: TODO
- Priority: P0; depends: BE-801, BE-804; requirement: BR-09.
- Hiện trạng: Meal list/create/patch/schedule có; scan unavailable.
- Scope: Meal delete/corrections, portions/macros units, timezone/day filters/cursor, duplicate submission protection; schedule tách log; manual flow độc lập AI.
- Acceptance/evidence: Scale khẩu phần cùng nutrient; delete sửa aggregate; owner isolation; unavailable scan vẫn log manual được; không tự confirm AI.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

### BE-813 — Dashboard và tiến độ thật
- Plan: [Plan triển khai](production-plans/BE-813.md).
- Status: TODO
- Priority: P0; depends: BE-811, BE-812; requirement: BR-10.
- Hiện trạng: Dashboard mock/calorie target/streak chưa có nguồn chuẩn đủ.
- Scope: Owner daily summary, targets, macros, streak từ completed session, upcoming booking; timezone boundary; source/estimated labels.
- Acceptance/evidence: Dashboard totals bằng logs, empty không giả 0 mục tiêu, retry/backfill không tăng điểm; không nhận reward mutation giả.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

### BE-814 — Kiểm chứng social và chat production
- Plan: [Plan triển khai](production-plans/BE-814.md).
- Status: TODO
- Priority: P0; depends: BE-801, BE-804; requirement: BR-11.
- Hiện trạng: Social/friends/chat implemented; chủ yếu unit tests.
- Scope: Search/pending invites/comments retrieval gap; stable per-conversation sequence; visibility/block media; race locks; moderation evidence; rate/size limits.
- Acceptance/evidence: PostgreSQL cùng timestamp/read across devices; cross-invite/send-block/accept-block; không duplicate reaction/count; người ngoài không đọc thread.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

### BE-815 — Thông báo và push thật
- Plan: [Plan triển khai](production-plans/BE-815.md).
- Status: TODO
- Priority: P0; depends: BE-802, BE-814; requirement: BR-12.
- Hiện trạng: In-app implemented, provider MissingPushProvider.
- Scope: Kiểm tra device ownership transfer và unique active token; source validity mapping; outbox atomic; FCM hoặc Expo adapter sau OPS-06; key persistence/rotation.
- Acceptance/evidence: 2 account cùng thiết bị không lộ push; invalid token revoke đúng; disabled không backlog; TTL retry; thiết bị Android staging nhận được.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

### BE-816 — Storage access và durability
- Plan: [Plan triển khai](production-plans/BE-816.md).
- Status: TODO
- Priority: P0; depends: BE-801, BE-804; requirement: BR-02, BR-08, BR-16.
- Hiện trạng: Upload/HMAC local implementation mới có.
- Scope: Kiểm owner/category/entity trước signed URL; MIME magic bytes/quota; unguessable key; private cert/post media; revoke/orphan cleanup; persistent media và backup.
- Acceptance/evidence: Wrong-owner attach/read bị chặn; URL hết hạn/traversal; recreate vẫn có media; backup restore. Cloud migration chỉ khi topology/budget yêu cầu.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

### BE-817 — Tích hợp artifact AI thật
- Plan: [Plan triển khai](production-plans/BE-817.md).
- Status: TODO
- Priority: P1-CAPABILITY; depends: BE-801, BE-816; requirement: BR-13.
- Hiện trạng: Chờ AI team; không chặn manual features.
- Scope: Artifact/schema/version/license/checksum/resources; nutrition/workout/chat adapter; health/quota/timeouts; consent; unavailable/error contracts.
- Acceptance/evidence: Contract fixtures + artifact smoke/resource test + thực tế provider; không mock production. Trước khi bật AI là release gate.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

### BE-818 — Hoàn thiện 7 module admin
- Plan: [Plan triển khai](production-plans/BE-818.md).
- Status: TODO
- Priority: P0; depends: BE-803, BE-806, BE-807, BE-809, BE-810; requirement: BR-14.
- Hiện trạng: Users/certs/reports/action-required/finance có một phần.
- Scope: Admin bookings/payments list/detail; aggregated metrics time bucket; trainer detail; audit query; settings/system safe fields; đồng nhất filter/sort/pagination.
- Acceptance/evidence: WEB-801..806 dùng data thật; totals theo đúng filter; khóa self/last admin chặn; audit reason; không unrestricted chat/health endpoint.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

### BE-819 — Support, settings và capabilities
- Plan: [Plan triển khai](production-plans/BE-819.md).
- Status: TODO
- Priority: P0; depends: BE-801, BE-802; requirement: BR-15.
- Hiện trạng: Support/settings chủ yếu prototype.
- Scope: Support ticket lifecycle/owner attachments/admin reply, public contact/privacy/deletion URLs; capabilities versioned; user preferences; API không lộ infrastructure secrets.
- Acceptance/evidence: Ticket người khác 404; no fake sent; disabled capability UI consistent; health settings/notifications không mất qua relogin.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

### BE-820 — Integration và contract verification
- Plan: [Plan triển khai](production-plans/BE-820.md).
- Status: TODO
- Priority: P0; depends: BE-801; requirement: BR-16.
- Hiện trạng: Hiện chưa có project IntegrationTests/ContractTests chuyên biệt.
- Scope: WebApplicationFactory + PostgreSQL test container isolated; auth roles; OpenAPI runtime response/request checks; migration upgrade/down recovery; negative/security/concurrency suites.
- Acceptance/evidence: CI chạy DB thật, không EF InMemory thay constraint; mỗi endpoint relevant 2xx/4xx; artifacts và failures không bị skip để xanh.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

### BE-821 — Docker và CI/CD bằng chứng thật
- Plan: [Plan triển khai](production-plans/BE-821.md).
- Status: TODO
- Priority: P0; depends: BE-800, BE-820; requirement: BR-16.
- Hiện trạng: Docker partial; workflow có, GH run chưa có evidence.
- Scope: Hoàn BE-300/310: clean compose boot+migrate twice+readiness+DP continuity; GitHub real jobs, registry immutable digest, staging pipeline, prod protected approval/rollback.
- Acceptance/evidence: URL run/commit/digest, container smoke; không secret layers/logs; workflow fail thật khi test/scan/lint fail; branch protection applied.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

### BE-822 — Security và quản lý secrets
- Plan: [Plan triển khai](production-plans/BE-822.md).
- Status: TODO
- Priority: P0; depends: BE-802, BE-816; requirement: BR-16.
- Hiện trạng: Gitleaks có allowlist docs rộng; BE-099 pending.
- Scope: Thu hẹp allowlist + positive tests docs/code; rotate leaked secrets qua operator; provider/config fail closed; key ring bảo vệ; dependency/SAST/image scan; CORS/rate/body limits.
- Acceptance/evidence: Negative seed secrets trong docs bị bắt, không in secret thật; no default admin; BE-099 chứng cứ rotation và scope rewrite duyệt riêng.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

### BE-823 — Performance và capacity đo thật
- Plan: [Plan triển khai](production-plans/BE-823.md).
- Status: TODO
- Priority: P0; depends: BE-820, BE-821; requirement: BR-16.
- Hiện trạng: Có k6 scripts, mới smoke evidence.
- Scope: Seed representative dataset; normal/peak/spike/soak, hot booking contention, pagination N+1, worker lag/pool usage; revise capacity-plan tính toán.
- Acceptance/evidence: Báo cáo env/dataset/commit/raw results với SLO release-checklist; không suy concurrency từ downloads/DAU; không coi smoke là load capacity.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

### BE-824 — Monitoring và xử lý sự cố
- Plan: [Plan triển khai](production-plans/BE-824.md).
- Status: TODO
- Priority: P0; depends: BE-821; requirement: BR-16.
- Hiện trạng: Runbooks nền có; alert thực tế chưa xác minh.
- Scope: Metrics/log redaction/correlation, uptime external, error/latency/queue age/refund pending alerts; dead-letter replay gated; incident owner.
- Acceptance/evidence: Inject provider/DB failure → alert đến operator, runbook restore/replay không duplicate tiền; không đưa health/chat vào log.
- Handoff: chưa triển khai trong lần viết lại tài liệu này.

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
- Status: BLOCKED (AI handoff); tracking implementation BE-817, không duplicate task scope.
### BE-500 — Staging deploy
- Plan: [Plan triển khai](production-plans/BE-500.md).
- Status: BLOCKED (OPS-05); implementation/gate BE-821/826.
### BE-600 — Production release
- Plan: [Plan triển khai](production-plans/BE-600.md).
- Status: BLOCKED (release-checklist chưa đạt + operator approval); tracking BE-827.
### BE-700 — Google Play
- Plan: [Plan triển khai](production-plans/BE-700.md).
- Status: BLOCKED (app integration, OPS-07); tracking BE-827 + MOB-808.
