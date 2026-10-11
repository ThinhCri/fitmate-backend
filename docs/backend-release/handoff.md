> Chỉ mục đầu tiên là trạng thái bàn giao hiện tại. Các mục bên dưới được giữ làm lịch sử và không thay thế source-of-truth hiện hành.

## 2026-10-11 — Kết luận tranh chấp buổi PT có chứng cứ [BE-807/809/819]
- Admin có endpoint riêng `POST /api/v1/admin/finance/booking-incidents/{ticketId}/resolve`, bắt buộc Admin role, Idempotency-Key, finance TOTP, lý do 20–1000 ký tự và tham chiếu chứng cứ nội bộ. Không thể đóng phiếu liên kết buổi tập theo đường trạng thái chung khi chưa có kết luận.
- Trong một transaction khóa payment/appointment/ticket, PT vắng được hủy buổi và tạo yêu cầu hoàn tiền thủ công đúng phân bổ của buổi; tiền vẫn Paid cho tới khi Admin duyệt và ghi nhận chuyển khoản thật. Buổi đã diễn ra hoặc hội viên vắng được ghi nhận Completed và ledger 10%/90%; không tạo payout trực tiếp. Gói 4 buổi giữ nguyên các buổi không liên quan.
- Lưu kết luận/audit/outbox và migration cột phiếu hỗ trợ; không apply lên DB ứng dụng. Unit 965/965, OpenAPI parity 11/11, integration trọng tâm 5/5 trên PostgreSQL cô lập. Full integration chế độ quiet 90/91 hai lần vì test tranh slot đồng thời không ổn định; test đó pass khi chạy riêng và full run với detailed logging pass, cần xác minh lại tính ổn định. Web Admin có form kết luận với TOTP/idempotency; test Web 127/127 và build PASS. Mobile support repository 10/10 và typecheck PASS.
- Chưa tự cấp dấu phạt PT từ một phiếu báo cáo, chưa mở checkout gói cho người dùng và chưa xác nhận provider/thiết bị thật. Không quảng bá đây là release approval hoặc thực hiện chuyển tiền tự động.

## 2026-10-11 — Liên kết phiếu hỗ trợ với buổi tập [BE-807/819]
- Mobile gửi `booking_id` khi mở phiếu từ chi tiết lịch; BE kiểm tra người gửi là participant và trả `booking_id` ở danh sách/chi tiết để Member/Admin tra cứu đúng buổi. Phiếu loại `booking_dispute` không có mã buổi bị từ chối thay vì tạo case mồ côi.
- Bổ sung migration có thể chạy lại an toàn cho cột `support_tickets."BookingId"` còn thiếu trong chuỗi migration; không apply lên DB ứng dụng. Sửa transaction khóa buổi tập qua EF execution strategy, tránh HTTP 500 khi tạo phiếu liên kết trên PostgreSQL.
- Kiểm thử: SupportService unit 21/21 trước bổ sung guard cuối, PostgreSQL SupportAndPreferences integration 3/3, Mobile support repository 9/9, Admin reports API 4/4, Mobile và Web typecheck PASS. Cần chạy lại suite sau các chỉnh sửa cuối và nghiệm thu trên thiết bị.
- Liên kết phiếu không tự đưa ra phán quyết vắng mặt, hoàn tiền hay dấu phạt. Checkout gói PT vẫn internal-only cho tới khi workflow quyết định và provider/operator UAT hoàn tất.

## 2026-10-10 — Quyết định yêu cầu dừng gói PT [BE-807/809]
- Đổi stop request từ quan hệ một-một sang lịch sử nhiều request/gói, nhưng DB chỉ cho một request Pending/Approved tại một thời điểm. Hội viên có thể rút khi còn chờ trong 24 giờ, giữ bản ghi cũ và gửi lại nếu thay đổi kế hoạch.
- PT được phân công có thể chấp nhận sớm; PT không có quyền tự bác yêu cầu. Nếu chưa được chấp nhận/rút khi hết 24 giờ, booking worker duyệt theo timeout. Mỗi chuyển trạng thái khóa payment, audit/outbox atomic; request lặp và sweep lặp không tạo quyết định/notification thứ hai. Test PostgreSQL đã kiểm quyền, lịch sử và cuộc đua PT accept với member withdraw.
- Migration `AllowRepeatedPackageStopRequests` chỉ thêm cột quyết định và filtered unique index; đã loại `UpdateData` exercise seed ngoài scope. Chưa apply vào app DB.
- **Approved chỉ là đủ điều kiện bước xử lý tiếp**, chưa hủy lịch hay hoàn tiền. Cần finalizer tạo pending refund theo snapshot, khóa/chuyển trạng thái từng buổi, Admin manual transfer, Mobile/Admin UI và kiểm thử end-to-end. Package checkout vẫn internal-only.

## 2026-10-10 — Gửi yêu cầu dừng gói PT nội bộ [BE-807/809]
- `SessionPackageStopService` kiểm quyền chủ gói ở Application layer, chuẩn hóa lý do 10–1000 ký tự; `EfSessionPackageStopStore` khóa payment theo cùng thứ tự với finance, khóa từng appointment, kiểm phân bổ gói và ghi snapshot/audit/outbox trong một transaction.
- Gửi lại cùng lý do trả request cũ, không phát thêm sự kiện. Chặn nếu buổi dự kiến hoàn đã có pending/succeeded refund hoặc PT đã được chi; trường hợp này chuyển Admin đối soát, không tự tính trùng. PostgreSQL integration đã kiểm ownership, replay và refund conflict.
- Service vẫn **internal-only**: chưa route Mobile và chưa cho PT phản hồi / timeout 24h / tạo refund từ snapshot. Không mở checkout gói hoặc gọi luồng dừng gói production-ready.

## 2026-10-10 — PT package stop request snapshot foundation [BE-807/809]
- Thêm snapshot bền vững cho yêu cầu dừng gói: lý do, thời điểm gửi, hạn PT phản hồi sau 24 giờ, số tiền dự kiến hoàn theo từng buổi; xét cửa sổ 48 giờ đúng lúc hội viên gửi yêu cầu, không tính lại sau khi PT chờ phản hồi.
- `SessionPackage.RequestStop` chỉ nhận gói Active, đủ ảnh chụp trạng thái/slot của toàn bộ buổi; Completed và buổi dưới 48 giờ không tự đủ điều kiện hoàn. Migration chỉ thêm `session_package_stop_requests` và `session_package_stop_sessions`, `Down` chặn xóa bản ghi tài chính chưa đối soát.
- Chưa có public API/UI để gửi yêu cầu: cần tiếp tục store transaction/idempotency, PT accept/24h timeout, kiểm tra refund/payout từng buổi, admin manual transfer và thông báo. Không coi tính năng dừng gói đã sẵn sàng hoặc mở checkout gói production.
- Test domain + PostgreSQL persistence đã qua; không apply migration vào DB ứng dụng hoặc deploy production.
- EF `has-pending-model-changes` vẫn báo khác biệt seed `exercise_catalog` không ổn định (probe chỉ sinh `UpdateData` cho seed, không sinh thay đổi schema); migration của phiên này đã bỏ toàn bộ `UpdateData` ngoài phạm vi. Cần xử lý seed diff ở task riêng, không trộn vào nghiệp vụ PT.

## 2026-10-03 — Bổ sung tài liệu luồng nghiệp vụ xuyên ba repository
- Thêm `docs/backend-release/business-flows.md` mô tả bằng tiếng Việt các luồng đăng ký Member/PT, duyệt PT, dịch vụ/slot, hold và PayOS, state machine booking, hủy trước 24 giờ, xác nhận/khiếu nại, doanh thu, refund và payout thủ công.
- Tài liệu giải thích trách nhiệm riêng của Backend, Mobile App và Admin Web, đồng thời liệt kê các trường hợp UAT quan trọng để tránh frontend tự suy đoán trạng thái hoặc tiền.
- Đây là thay đổi tài liệu; không sửa runtime code và chưa tự commit.

## 2026-10-03 — Hoàn thiện luồng booking thực tế và đồng bộ ba repository
- Hold booking là 10 phút; thanh toán được PayOS xác minh sẽ tự chuyển booking sang `Confirmed` vì slot PT đã công bố được xem là chấp thuận trước.
- Hội viên chỉ được tự hủy khi còn ít nhất 24 giờ trước giờ bắt đầu; dưới 24 giờ API trả conflict và yêu cầu liên hệ hỗ trợ.
- PT kết thúc buổi tập sẽ chuyển sang `AwaitingMemberConfirmation`. Hội viên có 24 giờ để xác nhận hoặc chuyển sang `Disputed`; hết hạn không tranh chấp thì background sweep tự xác nhận `Completed`.
- Sổ cái chỉ ghi nhận doanh thu khi booking đã `Completed`. Manual payout bị giữ thêm 24 giờ kể từ `CompletedAtUtc`, đồng thời vẫn chặn khi có refund hoặc dispute.
- OpenAPI, Mobile mapping/repository/UI và Admin Web enum/label đã đồng bộ các trạng thái mới. Mobile baseline SHA256 hiện là `C3500F5D8436BABDAE9180847A7D1FEEF1694235572EA0C87FEB45AC450B9568`.
- Migration `20261003010000_PracticalBookingLifecycle` chỉ thêm ba timestamp của vòng đời xác nhận và partial index phục vụ sweep; migration được EF phát hiện. Chưa apply lên local DB trong phiên này vì PostgreSQL `localhost:5432` không chạy.
- Verification: backend build PASS 0 warning/0 error; unit tests PASS 885/885; Admin Web typecheck PASS và tests PASS 122/122; Mobile typecheck PASS và targeted booking/contract tests PASS 25/25.
- Integration tests không chạy được trong phiên này vì PostgreSQL local không lắng nghe ở `localhost:5432`; 57 test phụ thuộc fixture DB fail lúc provision, 4 test không cần DB pass. Đây là giới hạn môi trường, không phải assertion nghiệp vụ thất bại.

## 2026-10-02 — Dọn tài liệu và artifact local sau contract freeze
- Contract vẫn là `FROZEN-V1`; không sửa OpenAPI hoặc runtime code.
- Chuyển bộ review ngày 2026-09-29 từ `docs/review-handoff/` sang `docs/archive/review-handoff-2026-09-29/` và gắn nhãn non-authoritative.
- Xóa patch rỗng và báo cáo review untracked đã lỗi thời; Git history vẫn giữ toàn bộ tài liệu tracked trước khi archive.
- Sửa `AGENTS.md` dùng đúng `Fitmate.slnx` và làm rõ named volume không mất khi xóa/recreate container.
- Thêm `/AI/` vào `.gitignore`; model local không bị xóa và không thể bị `git add .` nhầm.
- Các tệp Trainer controller dirty do line ending được giữ nguyên, không stage.

## 2026-10-02 — Hoàn tất verification sau review và đóng băng lại Mobile API contract
- **Trạng thái**: `FROZEN-V1`.
- Sửa UAT Journey 08 gửi canonical request field `user_id` thay cho contract cũ `userId`; test riêng pass 1/1.
- `dotnet test tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj -c Release --no-restore`: PASS, 61/61, 0 failed, 0 skipped.
- Build Release: PASS, 0 warnings, 0 errors. Unit tests: PASS, 879/879.
- Frozen OpenAPI revision: `74b223497f2a075f295644afcf5f9bcad897b66e`.
- Frozen OpenAPI SHA256 (raw file bytes): `4637c6ad3a323df062ba4b4b5c97ef3d7beb75c475ba44fc1f4b54b8fd827804`.
- Mobile client được phép generate từ đúng revision/hash trên. Không dùng hash stabilization cũ `3f513f3f...`.

## 2026-10-02 — Mở lại verification Mobile API contract sau review
- **Trạng thái**: `CONTRACT_STABILIZATION_IN_PROGRESS`; thay thế tuyên bố `FROZEN-V1` ngay bên dưới cho tới khi integration suite chạy pass.
- **Đã sửa**:
  1. `AppointmentResponse` fail closed khi `PlatformFeeRate` ngoài miền `0..1`; không còn clamp âm thầm dữ liệu tài chính sai.
  2. Bỏ `slot_start_local` khỏi Appointment wire response vì domain không lưu timezone tương ứng; `slot_start_utc` là thời điểm canonical.
  3. Bổ sung `MobileWireContractIntegrationTests` kiểm tra raw HTTP JSON qua `WebApplicationFactory` cho booking, workout, nutrition, social, chat và trainer revenue, gồm owner boundary `403` và anonymous `401`.
  4. Ghi OpenAPI SHA256 thật trong evidence BE-801. Revision schema: `a712c29b54f08f3c2cb5be2cf63bf5d3db1ead94`; SHA256: `3f513f3f1d1f1c993341d583a736f72f3a2a0630fe9fc558df41250d26b925b7`.
- **Verification đã chạy**:
  - `dotnet build Fitmate.slnx -c Release --no-restore`: PASS, 0 warnings, 0 errors.
  - `dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj -c Release --no-restore`: PASS, 879/879.
  - Filter `AppointmentContractTests`: PASS, 9/9.
  - Integration project compile: PASS.
- **Blocker còn lại**:
  - HTTP integration execution chưa thể hoàn tất vì máy không có PostgreSQL đang nghe và Docker daemon/Docker Desktop không khả dụng. Fixture fail closed trước khi chạy test với lỗi không thể provision isolated integration database.
  - Không được đổi lại `FROZEN-V1` hoặc generate production client cho tới khi chạy pass toàn bộ `Fitmate.IntegrationTests` trên PostgreSQL cô lập và cập nhật evidence.
- **Lệnh tiếp tục khi PostgreSQL test khả dụng**:
  - `dotnet test tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj -c Release --no-restore`

## 2026-10-02 — Hoàn tất Kiểm chứng và Đóng băng Backend Mobile API Contract (FROZEN-V1)
- **Trạng thái**: FROZEN-V1 (BE-801..BE-820).
- **Phạm vi kiểm chứng và chuẩn hóa**:
  1. **OpenAPI Strict Validation (Phase III)**: Thay thế parser regex bằng parser chuẩn `Microsoft.OpenApi.Readers 1.6.31`, loại bỏ duplicate keys/YAML errors, kiểm tra $ref resolution, path parameter parity và response description bắt buộc (`RouteOpenApiParityTests.cs`).
  2. **Booking Hold Wire Contract (Phase IV)**: Chuẩn hóa `CreateHoldRequest` wire DTO snake_case (`pt_id`, `service_id`, `slot_start_utc`), `CreateBookingHoldResponse` và `BookingHoldStatusResponse` (`BookingHoldContractTests.cs`).
  3. **Appointment Wire DTO (Phase V)**: Tách biệt hoàn toàn `AppointmentResponse` và `AppointmentPageResponse` khỏi Domain entity `Appointment`; chuẩn hóa `platform_fee_rate_bps` thành basis points (1000 cho 10%); ẩn toàn bộ audit/internal notes; kiểm chứng serialization (`AppointmentContractTests.cs`).
  4. **Reschedule Wire Contract (Phase VI)**: Chuẩn hóa canonical route `POST /api/v1/bookings/{id}/reschedule-requests`, DTOs `CreateRescheduleRequestBody` (`new_slot_start_utc`, `reason`), `RejectRescheduleRequestBody` (`rejection_reason`), và response DTO `BookingRescheduleResponse` (`BookingRescheduleContractTests.cs`).
  5. **Workout Wire Contract (Phase VII)**: Chuẩn hóa 15 wire DTOs trong `WorkoutDtos.cs` sang snake_case qua `JsonPropertyName`, custom converters `SnakeCaseLower` cho enums, chuẩn hóa list responses `{ items: [...] }` (`WorkoutContractTests.cs`).
  6. **Nutrition Wire Contract (Phase VIII)**: Chuẩn hóa 8 wire DTOs trong `NutritionDtos.cs` sang snake_case, meal sources enum converter (`manual`, `dataset`, `ai_estimate`) (`NutritionContractTests.cs`).
  7. **Social Wire Contract (Phase IX)**: Chuẩn hóa wire DTOs cho feed, comments, reactions, friends, blocks; đồng bộ OpenAPI pagination `{ items, nextCursor }` (`SocialContractTests.cs`).
  8. **Chat Wire Contract (Phase X)**: Chuẩn hóa wire DTOs `ChatMessageDto`, `ConversationDto`, `ChatPeer`, `SendMessageBody`, `MarkReadBody`; đồng bộ OpenAPI schema (`ChatContractTests.cs`).
  9. **Trainer Revenue Wire Contract (Phase XI)**: Chuẩn hóa `TrainerRevenueOverview`, `TrainerRevenueChartPoint`, `TrainerRevenueHistoryPage`, `TrainerRevenueItem` sang snake_case; đồng bộ OpenAPI spec (`RevenueContractTests.cs`).
- **Kết quả kiểm thử tự động**:
  - `dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj`: **877/877 tests PASS** (100% green, 0 failed, 0 skipped).
  - Tất cả contract serialization tests và OpenAPI schema reflection tests đều pass.
  - Tại thời điểm chạy, các tệp dirty của user và review artifacts cũ được bảo toàn; review artifacts sau đó đã chuyển vào `docs/archive/review-handoff-2026-09-29/`.
  - Mobile App được phép generate client từ baseline revision này.

## 2026-10-01 — Hoàn tất Ổn định Backend Contract cho Mobile App (BE-801..BE-820)
- **Mục tiêu**: Ổn định contract giữa Backend và Mobile App (`FitmateAI_FE\mobile`), loại bỏ phỏng đoán về route, DTO, enum, pagination và error response.
- **Tiến độ hoàn thành 14 Phases/Commits**:
  1. `88aabc2`: `test(contract): add route and OpenAPI parity tests [BE-801]` — Bộ test phản xạ `RouteOpenApiParityTests.cs` (7 test cases kiểm tra route/OpenAPI parity, security parity, duplicate operationId, path params).
  2. `4a5731d`: `fix(contract): stabilize JSON wire conventions [BE-801]` — Bộ test `DtoSerializationContractTests.cs` (14 test cases), chuẩn hóa `[property: JsonPropertyName("...")]` snake_case cho domain fields và camelCase cho pagination metadata.
  3. `f801429`: `feat(auth): align authentication and session contracts [BE-802]` — Bỏ route aliases trùng trong `AuthController.cs`, gắn `/api/v1/users/me/password` alias, chuẩn hóa OpenAPI spec.
  4. `7fcecea`: `feat(users): align profile and settings contracts [BE-804]` — Khai báo `PUT /users/me`, `GET /users/{id}/profile`, `GET /users/search`, `GET/PUT /users/me/preferences`, luồng account deletion và export dữ liệu.
  5. `cc28808`: `feat(trainers): align trainer marketplace contracts [BE-806]` — Chuẩn hóa `GET /trainers`, `GET /trainers/{id}`, `GET /trainers/{id}/availability`, thêm review endpoints (`/trainers/{id}/reviews`) và schemas.
  6. `0dd2548`: `feat(bookings): align booking and payment contracts [BE-808]` — Thêm `GET /bookings/holds/{id}`, `POST /bookings/{id}/confirm`, `/me/bookings/{id}/confirm`, `/me/bookings/{id}/start`, `/me/bookings/{id}`, `GET /payments/vnpay/ipn`.
  7. `d12d5cf`: `feat(bookings): add reschedule request contracts [BE-806]` — Thêm hợp đồng dời lịch hẹn 2 bên Branch A (`POST /bookings/{id}/reschedule-requests`, `/bookings/reschedule-requests/{requestId}/accept`, `reject`, `cancel`) và schemas `BookingRescheduleRequest`.
  8. `1c31b5e`: `feat(workout): align workout and weight contracts [BE-812]` — Bổ sung `GET/PATCH/DELETE /me/workout/plans/{id}` và schema `UpdateWorkoutPlanInput`.
  9. `115e03c`: `feat(nutrition): align nutrition contracts [BE-814]` — Bổ sung `DELETE /me/nutrition/meals/{id}` vào OpenAPI spec.
  10. `0ea646b`: `feat(social): align social and chat contracts [BE-816]` — Khai báo `GET /social/posts/{id}/comments`, `GET /social/friends/requests`, chuẩn hóa `{user_id}` sang `{userId}`.
  11. `724fe8e`: `feat(support): align notifications and support contracts [BE-818]` — Bổ sung `/support/tickets` (GET, POST), `/support/tickets/{id}/*`, `/uploads` (POST), `GET /me/services/{id}`, `GET /me/dashboard`, `GET /me/revenue/history` (thay thế phantom `/me/revenue/{bookingId}`).
  12. `401daaa`: `test(security): add contract integration security coverage [BE-820]` — Bộ test `ContractSecurityCoverageTests.cs` (4 test suites kiểm tra Admin role/policy enforcement, bảo vệ IDOR trên `/me/*`, cấm anonymous trên sensitive mutations, và kiểm tra toàn bộ số tiền VNĐ là integer). Đồng bộ các endpoint Admin trong `fitmate-v1.yaml` và `RouteOpenApiParityTests.cs`.
  13. `7d0b81c`: `chore(contract): freeze mobile v1 contract baseline [BE-801]` — Gắn frozen baseline tag vào `fitmate-v1.yaml` và `api-contract.md`.
  14. (Commit này): `docs(handoff): update contract baseline status [BE-801]` — Hoàn tất tài liệu bàn giao.
- **Kết quả kiểm thử**:
  - `dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj`: **816/816 tests PASS** (100% green, 0 failed, 0 skipped).
  - Parity discrepancies: **0 missing routes, 0 phantom operations, 0 security mismatches**.
  - Tại thời điểm chạy, các tệp dirty của user và review artifacts cũ được giữ nguyên; review artifacts sau đó đã được archive.

## 2026-10-01 — Hoàn tất thực thi Kế hoạch Plan V9.1 (Baseline PROD-2026-09-22-v1, DEC-051, Reschedule Branch A)
- **Hoàn thành toàn bộ 8 giai đoạn kỹ thuật (Phát hiện A -> H)**:
  1. **Phase 1 — Finding A (BE-810)**: Triển khai Durable Idempotency Store hoàn toàn atomic bằng `INSERT ... ON CONFLICT DO UPDATE` với lease token (`owner_token`), stale lease recovery sau 60 giây, canonical JSON hashing phân loại payload không phụ thuộc thứ tự key/khoảng trắng (`CanonicalJsonHasher`), phân định retention tài chính (365 ngày) và phi tài chính (24 giờ), kiểm thử đồng thời 10 luồng song song trên PostgreSQL thật (`IdempotencyConcurrencyTests`) xác nhận duy nhất 1 luồng acquire thành công.
  2. **Phase 2 — Finding B (BE-810)**: Bổ sung toàn diện database invariants và domain validation cho `financial_ledger_entries`: CHECK constraint allowlist 8 loại entry hợp lệ, CHECK `amount_vnd != 0`, CHECK loại trừ lẫn nhau giữa reversal và correction, unique index lọc partial `(appointment_id, entry_type) WHERE reverses_entry_id IS NULL AND corrects_entry_id IS NULL`, trigger PostgreSQL ngăn chặn tuyệt đối UPDATE/DELETE, và 9 integration tests trên PostgreSQL thật (`FinancialLedgerConstraintTests`) xác minh toàn bộ invariant.
  3. **Phase 3 — Finding B/DEC-029 (BE-810)**: Tách biệt hoàn toàn các chiều sổ cái (Gross, Platform Fee, Trainer Payable, Payout, Refund) qua DTO `LedgerDimensionBalances` và phương thức `GetBalancesAsync`, triệt tiêu rủi ro cộng chồng sai lệch số học; tạo migration khắc phục `20261001160000_BackfillRefundLedgerDimensions` bổ sung 2 chiều hoàn tiền `RefundPlatformFeeReturn` và `RefundTrainerDeduction` cho dữ liệu lịch sử theo thuật toán làm tròn lũy kế half-up (`MidpointRounding.AwayFromZero`).
  4. **Phase 4 — Finding C (BE-807)**: Bãi bỏ triệt để route bypass cũ `POST /api/v1/me/bookings/{id}/reschedule`, trả về `410 Gone` với mã lỗi `BOOKING_RESCHEDULE_ENDPOINT_RETIRED` và trích dẫn quy trình Reschedule Nhánh A (`/me/bookings/{id}/reschedule-requests`); loại bỏ hoàn toàn logic hủy và tạo mới appointment trái phép trong `BookingService`.
  5. **Phase 5 — Finding D/E (BE-807)**: Đồng bộ triệt để giao dịch ACID và Outbox Dispatcher: di chuyển `_outbox.Enqueue` trước `SaveChangesAsync` trong toàn bộ quy trình Reschedule (`CreateRequestAsync`, `RejectRequestAsync`, `CancelRequestAsync`) và Admin Finance (`ProcessRefundAsync`, `ReconcileLegacyAppointmentAsync`); xử lý atomic state persistence trong `AcceptRequestAsync` khi phát hiện quá hạn hoặc PT từ chối, ngăn chặn hiện tượng rollback trạng thái về Pending.
  6. **Phase 6 — Finding F (BE-810)**: Bắt buộc xác thực đa yếu tố (`X-Finance-Totp` header) cho quản trị viên qua `IFinanceStepUp.EnsureAsync` trên endpoint đối soát lịch hẹn lịch sử; kiểm tra chặt chẽ `EvidenceReference` (chuỗi 3-128 ký tự, cấm ký tự điều khiển, cấm URL/scheme); ghi nhận nhật ký kiểm toán quản trị (`_audit.Stage`) trước khi ghi nhận sổ cái và phát tán outbox event.
  7. **Phase 7 — Finding G (BE-818)**: Đồng bộ tài liệu hợp đồng OpenAPI (`fitmate-v1.yaml`) phản ánh chính xác trạng thái `410 Gone` (deprecated) của route reschedule cũ, bổ sung đặc tả 2 endpoint `/admin/finance/legacy-appointments/unverified` (GET) và `verify` (POST) kèm schema `LegacyAppointmentReconciliationView` và `X-Finance-Totp` header; bổ sung bộ kiểm thử serialization hợp đồng (`AdminFinanceContractTests`).
  8. **Phase 8 — Finding H (BE-821)**: Chạy kiểm thử tự động toàn diện và kiểm tra chu trình migration Up -> Down -> Up trên cơ sở dữ liệu PostgreSQL thực tế; đính chính tên file migration lịch sử trong tài liệu handoff.
- **Kết quả Kiểm thử & Biên dịch Xác minh trên Final HEAD**:
  - `dotnet build Fitmate.slnx -c Release`: **0 Warning(s), 0 Error(s)** (clean Release build).
  - `dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj`: **789/789 tests PASS** (0 failed, 0 skipped, 100%).
  - `dotnet test tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj`: **59/59 tests PASS** (0 failed, 0 skipped, kết nối PostgreSQL thật qua Docker).
  - **Tổng số tests PASS**: **848/848 tests**.
  - **Chu trình Migration**: Đã thực thi chu trình `dotnet ef database update` Up -> Down (revert về `20260930150000_AddFinancialLedgerEntries`) -> Up lại phiên bản mới nhất thành công 100% không phát sinh lỗi.
- **Trình tự 8 Commits Đã Thực Hiện Chuẩn Quy Ước (Imperative, <= 72 ký tự, VoTanThinh <votanthinhcri28@gmail.com>)**:
  1. `57ca832`: `feat(common): implement atomic idempotency acquisition protocol [BE-810]`
  2. `87952b6`: `fix(finance): enforce database ledger invariants and checks [BE-810]`
  3. `b483b06`: `fix(finance): separate ledger dimensions and backfill refunds [BE-810]`
  4. `b44d6b7`: `refactor(booking): retire legacy reschedule bypass [BE-807]`
  5. `81bad38`: `fix(booking): ensure atomic reschedule states and outbox events [BE-807]`
  6. `6fa1d9e`: `fix(finance): require MFA for legacy reconciliation [BE-810]`
  7. `120912d`: `fix(admin): synchronize changed API contracts [BE-818]`
  8. (Pending commit Phase 8): `docs(release): publish final verified production handoff report [BE-821]`

## 2026-09-30 — Triển khai Backend Production Baseline (PROD-2026-09-22-v1, DEC-051, Plan V8, Reschedule Branch A)
- **Kế hoạch triển khai Backend production**:
  1. **Tách trạng thái Appointment, Payment, Refund (BE-807)**: Migration `20260930120000_SeparateAppointmentAndPaymentStatus.cs`, bỏ phụ thuộc trạng thái chéo; Appointment giữ nguyên khi Payment fail/pending/refund.
  2. **Khóa lịch PT & chống Double Booking (BE-807)**: Bảng `pt_schedule_locks` với PostgreSQL advisory lock / row lock; `ExecuteWithScheduleLockAsync` áp dụng cho Hold, Confirm, Reschedule, Expire.
  3. **Booking Lifecycle Guards & Expiry Worker (BE-807)**: Chống double hold, tự động hoàn trả slot khi hold hết hạn qua `BookingHoldExpiryWorker` chạy định kỳ; xử lý thanh toán đến muộn an toàn (`LATE_PAYMENT_SLOT_TAKEN`).
  4. **Nghiệp vụ Đổi lịch Nhánh A - BR-05 (BE-807)**: `BookingRescheduleRequest`, `POST /api/v1/bookings/{id}/reschedule`, accept/reject, deadline tính theo `min(old_slot - 2h, created + 24h)`. Bổ sung Outbox events (`booking.rescheduled`, `booking.reschedule_requested`).
  5. **Sổ cái tài chính Bất biến Append-Only - BR-07 (BE-810)**: Bảng `financial_ledger_entries` kèm PostgreSQL trigger `fn_prevent_financial_ledger_mutation()`; ghi recognition entries khi session hoàn thành, adjustment entries khi refund; đối soát số dư tổng.
  6. **Thuật toán Hoàn tiền Lũy kế & Cancellation Hỗ trợ PartialRefunded (BE-810)**: Tính delta hoàn tiền bằng công thức lũy kế với `MidpointRounding.AwayFromZero` loại bỏ hoàn toàn lệch xu; tự động hoàn số tiền còn lại khi hủy booking `Paid` hoặc `PartialRefunded`.
  7. **Đối soát Lịch hẹn Lịch sử & Chặn Settlement (BE-810)**: Đánh dấu `LegacyCompletionTimeUnknown`; chặn chi trả quyết toán (`SETTLEMENT_BLOCKED_LEGACY_UNKNOWN`) cho tới khi Quản trị viên đối soát; cung cấp API unverified list và verify endpoint.
  8. **Bảng Idempotency Bền vững & In-Progress Protection (BE-810)**: Bảng `idempotency_records` với trạng thái `processing`, `completed`, `failed`; trả về HTTP 409 `IDEMPOTENCY_IN_PROGRESS` khi có request trùng đang xử lý; trả header `X-Idempotent-Replay: true` khi replay.
  9. **Bắt buộc TOTP Step-up và Idempotency cho Toàn bộ Manual Finance Mutations (BE-810)**: Enforce `X-Finance-Totp` qua `IFinanceStepUp` và `Idempotency-Key` trên toàn bộ các endpoint phê duyệt/tạm giữ quyết toán, ghi nhận chuyển khoản, và yêu cầu hoàn tiền; kiểm tra duy nhất mã giao dịch ngân hàng.
  10. **Kiểm toán Tầng Application, Wire Contracts & Outbox Replay (BE-818)**: Tách `IAdminBookingService`, trả về `AdminBookingSummaryResponse` loại bỏ hoàn toàn trường `Note` theo BR-14; chuẩn hóa `PlatformPolicyResponse` dạng `snake_case`; kiểm soát dead-letter queue và bảo vệ replay tài chính.
- **Kiểm thử & Biên dịch**:
  - `dotnet build --configuration Release`: 0 Warning(s), 0 Error(s).
  - `dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --configuration Release --no-build`: 766/766 tests PASS (100%).
- **Trình tự Commits đã thực hiện**:
  - `ee7a8d0`: `refactor(booking): separate appointment and payment status [BE-807]`
  - `ccaa08f`: `feat(booking): add pt schedule locks against double-booking [BE-807]`
  - `f2d7799`: `feat(booking): enforce lifecycle guards and hold expiry worker [BE-807]`
  - `c99b716`: `feat(booking): implement reschedule branch a per user approval [BE-807]`
  - `7e43c05`: `feat(finance): implement immutable append-only financial ledger [BE-810]`
  - `16f2ee2`: `fix(finance): cumulative refund allocation and cancellation [BE-810]`
  - `c763a96`: `fix(finance): legacy reconciliation and block unverified settlement [BE-810]`
  - `b60c7c2`: `feat(common): implement durable idempotency store [BE-810]`
  - `bd6b515`: `fix(finance): enforce manual settlement totp and idempotency [BE-810]`
  - `fe9a8c5`: `fix(admin): enforce application authorization and contracts [BE-818]`

## 2026-09-27 — Admin Web draft implementation and documentation handoff
- User redirected the work to detailed documentation only. Preserved the preceding unfinished code changes; no additional runtime code changes in this documentation pass.
- Prior draft touched Admin MFA/login/refresh/JWT validation, certificate preview/revoke, AdminReview APIs and Web auth/finance/support/certificate flows. These changes are NOT verified; tests and contracts still need coordinated updates.
- Start at [Admin Web finalization handoff](admin-web-finalization-handoff.md), then the linked Web finalization documents 01–07. They specify actual gaps, target security/finance workflows, implementation order, source-test requirements, operator gates and a copy-ready Antigravity prompt.
- Known critical gaps include client-controlled MFA marker, refresh/session races, financial uncertain-result retries, non-atomic certificate audit and missing audit/booking-review UI. New target session/challenge/history/case contracts are proposals to implement, not existing functionality.
- No build/test/typecheck/install/Docker/migration/server/inference or commit performed. User must run verification; do not mark VERIFIED or production-ready from this handoff.

## 2026-09-27 — Admin Web static completion review
- Reviewed active Web routes/API callers against BE controllers and application contracts. Seven modules exist but production completion is not established.
- Findings and ordered closure checklist: `../FitmateWebApp/docs/production/admin-completion/review-2026-09-27.md` (sibling repository). Main gaps: refresh identity mismatch, missing admin login MFA, session cache cleanup, first settlement review entry, hold TOTP, absent manual-refund UI, private certificate preview, support detail/paging and DTO drift.
- No runtime code changed; no install/build/tests/Docker/inference executed, no commit. User's existing Web changes preserved. Earlier Web test evidence was not rerun or treated as real-backend UAT.

## 2026-09-26 — Verify stack API port collision
- Docker inspection: fitmate-api-check-api-1 remained Created because Compose merged base port 8080 with verify port 18080; existing local API already owns 8080.
- Changed verify API ports to !override (Compose >= 2.24.4; installed version 5.5.1), exposing only loopback 18080. No container restarted by agent; user must rerun compose up. Development remains the correct Swagger environment.

## 2026-09-26 — Development Swagger UI
- Enabled existing Swashbuckle package in Development only: /swagger and /swagger/v1/swagger.json. No new dependencies. Production and Testing do not expose the explorer.
- Runtime controller discovery, unique schema names, Bearer authorization on protected endpoints, optional Idempotency-Key and finance TOTP header inputs. No persisted browser authorization configured.
- Swagger requests execute real local operations. Generated runtime document is for exploration; canonical cross-repo OpenAPI remains separately maintained.
- No build/test/restore/commit performed. User must rebuild/restart the API and verify JSON/UI and authenticated requests.

## 2026-09-26 — Fix stale PostgreSQL integration schema
- User log: payOS suite 0/8 passed; PostgreSQL 42P01 for payos_orders/manual_pt_settlements. Shared fixture reused a database initialized before these entities existed; EnsureCreated does not update existing tables.
- Fixture now defaults to a unique test database per process, validates explicit test database names, parameterizes database lookup and fails immediately on provisioning/schema initialization errors or an already populated database. No database/volume deletion; databases retained for diagnosis.
- This fixture verifies current EF model behavior, not migration upgrades. Migration-chain verification remains a separate gate. Updated payOS runbook accordingly.
- No build/test/restore/migration/commit executed by agent. User must rebuild tests and rerun the payOS filter. Untracked PlatformPolicy.cs was preserved.

## 2026-09-26 — User-run verification and revenue regression fixture correction
- User-provided terminal evidence: solution build succeeded (72 warnings); email integration 4/4 and email unit tests 24/24 passed; full unit suite 626/627 passed.
- The failing backlog guard expected 700,000 gross / 70,000 fee including a fully refunded booking. Updated expectations to 500,000 / 50,000 for the completed session, consistent with recognition rules. Added a paid-but-unfinished booking and assertions for pending payout, zero paid-out money, chart and no outbox event.
- No production service changes in this correction. Agent did not run build/tests/restore or commit; corrected test awaits user rerun without --no-build. Dependency vulnerability warnings remain unresolved; no production approval.

## 2026-09-26 — payOS + xác minh buổi tập và chi PT thủ công
- User chọn agent tự tích hợp provider; chọn payOS theo API booking + KYC cá nhân. Chưa được suy ra mô hình thu hộ nhiều PT đã được provider duyệt. Mặc định tắt live; OPS-03 vẫn chặn.
- Thêm adapter HTTP/HMAC, durable order code, webhook idempotent/late-payment pending refund; giữ VNPay history. Không payout API.
- Thêm approve/hold/record-transfer per appointment và manual refund intent/receipt; chặn legacy settlement event-only và legacy fake refund với payOS; private evidence references, audit, amount computed server, shared payment lock + bank reference lock.
- Revenue/history/ledger đọc receipt; capability phản ánh provider. Payment DTO snake_case và method lowercase; manual finance camelCase. Scoped idempotency key theo actor/method/path, webhook bỏ qua cache.
- Migration 20260926120000_PayosAndManualSettlements + snapshot/designer soạn thủ công; chưa áp DB. Có unit/integration test sources mới; không tự chạy build/test/restore/migrate/commit theo yêu cầu user.
- Cập nhật contract/rules/tasks/runbook và snapshot hai FE; không sửa code UI đang dở trong FE.
- Tiếp theo: user chạy lệnh trong runbooks/payos-manual-settlement.md, phản hồi lỗi; verify schema diff/contract/concurrency, Admin MFA và evidence storage, ledger adjustments; chỉ bật tiền thật sau provider approval và operator UAT.
# Handoff — Backend Release

## BE-410 terminal email failure fix — 2026-09-26
- User test output: EmailOutboxIntegrationTests had 3 passed / 1 failed; expired-token terminal exception incorrectly left the message pending.
- Added explicit TerminalOutboxException handling before the generic retry catch in OutboxWorker; terminal failures move directly to dead_letter without logging the raw exception. Transient retry and shutdown cancellation behavior are preserved.
- Strengthened existing email tests to assert no send/no automatic retry after terminal failure, and pending/attempt/backoff behavior for transient errors.
- Read-only review performed; build/tests not run per user manual-execution workflow. No commit. Existing email implementation changes preserved.
- User should rerun the filtered EmailOutboxIntegrationTests. These tests use an in-memory store/fake sender, not PostgreSQL or real SMTP delivery.
- Separate unresolved warnings in supplied output: vulnerable MailKit/MimeKit/DataProtection/System.Security.Cryptography.Xml dependencies and deprecated forwarded-header APIs. This focused worker fix does not resolve those warnings or establish release readiness.

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

**BE-822: Security và quản lý secrets — VERIFIED**
- Thu hẹp triệt để `.gitleaks.toml`: loại bỏ toàn bộ allowlist diện rộng (`docs/`, `README.md`, `AGENTS.md`, `.env.example`), mở rộng regex phát hiện secret cả có nháy lẫn không có nháy, mở rộng mảng `keywords` bao gồm các biến thể dấu gạch ngang/dưới.
- Xử lý Client IP an toàn qua Reverse Proxy: Cấu hình `ForwardedHeadersOptions` với `XForwardedFor | XForwardedProto`. Giữ nguyên loopback trong `KnownNetworks`/`KnownProxies` mặc định, cho phép bổ sung IP/subnet proxy tin cậy qua env `FORWARDED_KNOWN_PROXIES` và `FORWARDED_KNOWN_NETWORKS`. Không tin `X-Forwarded-For` tùy tiện để chống IP spoofing vượt rate limiting.
- CORS Fail-closed trong Production: Khi chạy ở môi trường Production (`IsProduction()`), bắt buộc `CORS_ALLOWED_ORIGINS` phải được cấu hình rõ ràng, cấm tuyệt đối wildcard `*` và `localhost`/`127.0.0.1`. Throw `InvalidOperationException` ngay khi khởi động nếu vi phạm.
- Triển khai Rate Limiting phân tầng (.NET 8):
  - `GlobalLimiter`: áp dụng tự động cho toàn bộ API (mặc định 60 req/min/IP, cấu hình được qua `RateLimit:PerMinutePerIp`).
  - `AuthLimiter`: gắn qua `[EnableRateLimiting("AuthLimiter")]` vào `/register`, `/login`, `/password/forgot`, `/password/reset` (mặc định 5 req/min/IP).
  - `AuthRefreshLimiter`: gắn qua `[EnableRateLimiting("AuthRefreshLimiter")]` vào `/refresh` (mặc định 30 req/min/IP).
  - Phản hồi 429 ProblemDetails chuẩn RFC 7807 với code `FITMATE.GENERAL.RATE_LIMITED`.
- Bảo vệ tài nguyên & Tiêu đề HTTP: Kestrel `MaxRequestBodySize = 10MB` và Security Headers (`X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `X-XSS-Protection: 0`).
- Fail-Closed startup check: kiểm tra cấu hình payment (`VNPAY_HASH_SECRET`) trong Production khi `FEATURE_PAYMENT_ENABLED == true`.
- Tích hợp Trivy Container Scanner trong `.github/workflows/ci.yml` quét lỗ hổng ảnh `fitmate-ci-api`.
- Bổ sung bộ unit tests trong `SecurityConfigurationTests.cs`: kiểm tra allowlist gitleaks, regex secret unquoted/quoted, fail-closed payment, production CORS validation, rate limit options defaults.
- Kết quả kiểm chứng thực tế từ User:
  - Build sạch: 0 warning, 0 error.
  - Unit tests: 553/553 PASS.
  - Integration tests trên PostgreSQL thật: 12/12 PASS.
  - Container health (`/health/ready`, `/health/live`, `/`): 200 OK kèm đủ 4 Security Headers.
  - CORS preflight (`OPTIONS /api/v1/auth/login`): 204 No Content, credentials allowed, allowed origin `http://localhost:5173`.
  - Rate limiting 429 verified: ProblemDetails RFC 7807 với mã `FITMATE.GENERAL.RATE_LIMITED` khi vượt quá 5 req/min.
- File evidence: `docs/backend-release/production-plans/evidence/BE-822.md`.

**BE-823: Performance và capacity đo thật — VERIFIED**
- Thiết lập hạ tầng kiểm thử độc lập: `compose.perf.yml` với project name `fitmate-perf`, DB `fitmate_perf`, ports `8088:8080` (API) và `5433:5432` (Postgres) chống xung đột socket dev, volume `fitmate-postgres-perf-data`, ghim phiên bản `grafana/k6:0.54.0`.
- Triển khai CLI Data Seeder an toàn: `src/Fitmate.Api/Cli/SeedPerfDataCommand.cs` và đăng ký trong `Program.cs`. Có 3 lớp chốt chặn an toàn (từ chối Production, bắt buộc `--allow-perf-seed` hoặc `PERF_SEED_ENABLED=true`, bắt buộc DB đích là `fitmate_perf`).
- Chuẩn bị Representative Dataset: 600 members, 60 PTs (có Chứng chỉ NASM Approved để hiển thị marketplace), 1.000 bookings (phân bổ 60+ bookings cho các users chỉ định để kiểm thử phân trang sâu 5x10 có dữ liệu thật), 2.000 meal logs, 1.000 workouts.
- Sửa lỗi k6 scripts:
  - `normal.js`: Sửa lỗi duplicate key `http_req_duration` gộp thành mảng `['p(95)<800', 'p(99)<1500']`, sửa lỗi thiếu import `group`, sửa stages về chuẩn 2 phút, thêm tagged read/write thresholds và `checks: ['rate>0.99']`.
  - `common.js` & `constants.js`: Cập nhật VU account pool (100 members, 20 PTs) phân bổ 1-1 theo `__VU`, sửa hàm `resetCaches()` chỉ xóa token khi thời gian hiệu lực còn lại < 60 giây, bổ sung tags `read`/`write`.
  - Admin MFA: Ghi nhận trung thực Admin scenario bị **BLOCKED** do hợp đồng `AuthController.Login` chưa hỗ trợ 2FA challenge response; tuyệt đối không bypass hoặc tạo token giả.
- Viết kịch bản mới:
  - `contention.js`: 20 VU riêng biệt cùng tranh chấp 1 slot PT, assertion tuyệt đối `holds_succeeded == 1`, `conflicts_received == 19`, `unexpected_failures == 0`.
  - `deep-paging.js`: Kiểm thử phân trang sâu `/me/bookings?page=5&pageSize=10` (10 items thật), `/trainers?limit=10&cursor=40`, kiểm tra N+1 qua phân tích tăng trưởng query theo page size.
- Khắc phục các vấn đề kỹ thuật runtime:
  - ValueConverter cho `SlotStartLocal` (`DateTimeKind.Utc` ở DB, `Unspecified` ở Domain) giải quyết triệt để lỗi Npgsql `timestamptz`.
  - Slot Concurrency Lock (`SemaphoreSlim`) trong `BookingService.CreateHoldAsync` giải quyết triệt để race condition TOCTOU khi 20 VUs cùng đặt 1 slot.
  - Tách biến môi trường rate limit `PERF_RATE_LIMIT_*` trong `compose.perf.yml` tránh bị ghi đè bởi file `.env` local.
- Kết quả kiểm chứng thực nghiệm trên môi trường thật:
  - **Smoke Test**: 192/192 checks PASS (100%), error rate 0.00%, p95 238ms.
  - **Contention Test (Hold Race)**: 20/20 checks PASS (100%), `holds_succeeded: 1` (201 Created), `conflicts_received: 19` (409 Conflict với code `BOOKING_SLOT_TAKEN`), `unexpected_failures: 0`.
  - **Deep Paging Test**: 5.112/5.112 checks PASS (100%), error rate 0.00%, throughput 21.26 req/s, p95 read 10.98ms, trang 5x10 trả về đủ 10 items thật.
  - **Normal Load Test**: 5.602/5.602 checks PASS (100%), error rate 0.00%, throughput 46.26 req/s, p95 overall 14.56ms, p95 read 15.24ms (vượt xa SLA < 500ms).
- File evidence: `docs/backend-release/production-plans/evidence/BE-823.md`.

**BE-824: Monitoring và xử lý sự cố — VERIFIED**
- Phân biệt rõ ràng giữa W3C Distributed `TraceId` (`Activity.Current.TraceId`) và business `CorrelationId` (`X-Correlation-ID`) trong `CorrelationIdMiddleware`; inject đầy đủ `TraceId`, `CorrelationId`, `SpanId` vào logger scope và RFC 7807 ProblemDetails.
- Triệt tiêu triệt để nguy cơ bùng nổ metric cardinality: `ResolveRouteTemplate` trích xuất `RouteEndpoint.RoutePattern.RawText` tĩnh (ví dụ `/api/v1/users/{id}`), tuyệt đối không để lọt User ID/Guid động vào metric tags.
- Bổ sung trọn bộ SLIs cốt lõi (traffic, latency, auth failures, queue depth, oldest outbox age, pending refunds count/age, db pool, backup freshness) và xây dựng endpoint xuất chuẩn Prometheus exposition text (`GET /api/v1/admin/metrics/prometheus` và `GET /api/v1/admin/metrics/summary`) bảo vệ bởi quyền `AdminOnly`.
- Xây dựng quy trình Replay Dead-Letter bền vững qua `AdminOutboxService`:
  - Kiểm tra quyền Admin và lý do replay (min 5 chars).
  - Rào chắn bảo vệ giao dịch tiền bạc (Financial Replay Guard): Cấm replay tự do sự kiện thanh toán/hoàn tiền có nguy cơ duplicate nếu chưa qua đối soát.
  - Chống race condition replay đồng thời bằng atomic conditional update: `WHERE id = @id AND status = 'dead_letter'`.
  - Bảo toàn `LastError` chẩn đoán nguyên bản (không bị xóa đè) và ghi nhận bản ghi `AuditLog` bền vững vào database PostgreSQL.
- Làm sạch (redact) dữ liệu nhạy cảm tại nguồn ghi log (`SanitizeError` trong `OutboxWorker` và `SanitizeLastError` trong `AdminOutboxService`).
- Soạn thảo quy định cảnh báo và kịch bản diễn tập sự cố `docs/backend-release/runbooks/alert-rules.md` (ngưỡng SLIs, cửa sổ đánh giá, ức chế bão cảnh báo Inhibition/Grouping, thông báo phục hồi [RESOLVED], external uptime monitor).
- Ghi nhận trạng thái phụ thuộc bên ngoài: Kênh Webhook Slack/PagerDuty và External Uptime probe ghi nhận **BLOCKED (Chờ Operator cấp theo OPS-04/OPS-05)**.
- Kết quả kiểm chứng thực nghiệm trên môi trường thật:
  - Solution build sạch: 0 warning, 0 error.
  - Unit tests: 570/570 PASS.
  - PostgreSQL Integration tests: 4/4 PASS (`ObservabilityAndOutboxReplayTests`).
- File evidence: `docs/backend-release/production-plans/evidence/BE-824.md`.

**BE-825: Backup, retention và disaster recovery — VERIFIED**
- Triển khai gói sao lưu 3 thành phần (PostgreSQL binary dump `pg_dump -Fc`, DataProtection Key Ring từ volume `fitmate-dpkeys`, Media assets) kèm manifest xác thực toàn vẹn SHA-256 (`BackupManifest.cs`).
- Hỗ trợ biến môi trường `DATA_PROTECTION_KEYS_PATH` trong `src/Fitmate.Api/Program.cs` cho phép nạp key ring độc lập khi diễn tập.
- Xây dựng bộ 5 bất biến tài chính nghiêm ngặt trong `PostRestoreInvariantChecker.cs` (Non-negative amounts, Refund cap, Cumulative refunds <= payment, Unique provider txn ref, Appointment-Payment referential integrity) sử dụng đúng schema PostgreSQL quoted.
- Hiện thực hóa bộ kiểm chứng Canary Token (`DataProtectionCanaryVerifier.cs`): mã hóa token trước backup, giải mã sau restore để xác minh tính hợp lệ của key ring được khôi phục.
- Viết kịch bản tự động hóa diễn tập:
  - `scripts/backup.ps1`: Tạo canary token, snapshot DB, trích xuất key ring từ volume `fitmate-dpkeys`, nén media, tính SHA-256, sinh `manifest.json`.
  - `scripts/restore-drill.ps1`: Kiểm tra SHA-256 fail-fast, phục hồi vào database cô lập `fitmate_restore_drill`, giải nén keys vào `temp_restore_keys/`, thực thi 5 câu lệnh SQL kiểm chứng bất biến, replay Deletion Journal, tính toán RTO (thời gian chạy) và RPO (khoảng cách snapshot).
- Soạn thảo sổ tay vận hành phục hồi thảm họa `docs/backend-release/runbooks/disaster-recovery.md` (khóa traffic và worker trong khi restore, checklist đối soát Reconciliation Window với cổng thanh toán, chính sách retention 30d/12m/7y, và cảnh báo quá hạn P1 `BackupFreshnessCritical`).
- Ghi nhận trạng thái phụ thuộc bên ngoài: Offsite Storage (S3/GCS), KMS Key Custody, và Independent Deletion Journal Stream ghi nhận **BLOCKED (Chờ Operator cấp theo OPS-04/OPS-05/OPS-06)**.
- Kết quả kiểm chứng thực nghiệm:
  - Unit tests: 5/5 PASS (`BackupAndRetentionTests`).
  - Integration tests trên PostgreSQL thật: 3/3 PASS (`DisasterRecoveryAndRetentionTests`).
- File evidence: `docs/backend-release/production-plans/evidence/BE-825.md`.

**BE-828: Runtime còn được hỗ trợ — PREPARATION COMPLETED (Upgrade Pending Verification)**
- Phân tích chính xác vòng đời Microsoft: .NET 8 LTS và .NET 9 STS cùng chạm mốc EOL vào ngày 10/11/2026 (~48 ngày còn lại). Xác định mục tiêu của dự án là nâng cấp trực tiếp lên .NET 10 LTS (phát hành 11/11/2025, hỗ trợ dài hạn đến 14/11/2028).
- Lập ma trận dải phiên bản ứng viên cần kiểm chứng (Candidate ranges) cho SDK, Runtime, EF Core, Npgsql, OpenTelemetry, Docker base images. Giữ nguyên động cơ cơ sở dữ liệu PostgreSQL 16.
- Đề xuất nội dung cho `global.json` và `.config/dotnet-tools.json` (chưa nạp file trực tiếp vào working tree trong giai đoạn docs-only để tránh ảnh hưởng build của giải pháp `Fitmate.slnx`).
- Soạn thảo chính sách vận hành và quản trị runtime `docs/backend-release/runtime-support-policy.md`: phân công Owner đề xuất (Backend Lead) và Deadline đề xuất (30/10/2026, trước ngày EOL), kèm tiêu chí go/no-go và kế hoạch rollback.
- Xây dựng bộ test kiểm chứng tương thích baseline `tests/Fitmate.UnitTests/RuntimeCompatibilityTests.cs`:
  - DataProtection: giải mã token hợp lệ và từ chối an toàn fail-closed khi sai key/purpose.
  - Password Hasher: xác thực đúng mật khẩu băm cũ qua BCrypt và ASP.NET Identity PBKDF2, từ chối mật khẩu sai.
  - Token & Contract: JWT claims parsing, expired token 401, và bảo toàn quy ước JSON serialization từ response fixtures thực tế hiện hành.
- Giữ nguyên `TargetFramework` `net8.0` trong các file `.csproj`. Kế hoạp build container image và chạy migration drill trên database cô lập `fitmate_upgrade_drill` được theo dõi ở trạng thái Pending Verification.
- File evidence: `docs/backend-release/production-plans/evidence/BE-828.md`.

## Phiên 37 — 2026-09-29 — Rà lại regression patch và xác nhận baseline (PARTIAL VERIFIED)

**Bối cảnh**
- Làm việc CHỈ trong `D:\Ky8\exe201\FitmateAI_BE`. Không đụng vào `FitmateWebApp` và `FitmateAI_FE`.
- User yêu cầu rà lại các thay đổi dở đã nằm sẵn trong working tree từ phiên trước:
  - `docs/backend-release/openapi/fitmate-v1.yaml`
  - `src/Fitmate.Api/Trainers/AvailabilityController.cs`
  - `src/Fitmate.Api/Trainers/CertificatesController.cs`
  - `src/Fitmate.Api/Trainers/ServicesController.cs`
  - `src/Fitmate.Api/Trainers/TrainersPublicController.cs`
  - `tests/Fitmate.UnitTests/PtSerializationContractTests.cs`
- Quy định Git bắt buộc: tuyệt đối KHÔNG `git add`, `git commit`, `push`, `amend`, `reset`, `restore`, `checkout`, `clean`, `stash`. Chỉ dùng read-only (`status`, `diff`, `log`, `show`, `ls-files`, `hash-object`). Giữ toàn bộ thay đổi ở working tree để user review.

**Kết quả xác minh working tree (zero-content-diff vs HEAD)**
- `git -c core.autocrlf=false diff --stat` trả về 0 dòng — toàn bộ 6 file khớp byte-for-byte với HEAD blob.
- Cảnh báo "LF will be replaced by CRLF" trong `git status` là do `core.autocrlf=true` của repo Windows chứ không phải thay đổi nội dung; blob hash-Object khớp HEAD 100%:
  - `docs/backend-release/openapi/fitmate-v1.yaml` -> `8054413c4e0ce322356846ea4177f22423ee0b4b` (HEAD)
  - `src/Fitmate.Api/Trainers/AvailabilityController.cs` -> `8dd06b0f32f9aec856a55bd0603ef974ee6719c4` (HEAD)
  - `src/Fitmate.Api/Trainers/CertificatesController.cs` -> `e8267389ccbd3fcd834b9faeb862decb84694e10` (HEAD)
  - `src/Fitmate.Api/Trainers/ServicesController.cs` -> `c498418225f23e26a36aeb1af454e5f2c1aeb56c` (HEAD)
  - `src/Fitmate.Api/Trainers/TrainersPublicController.cs` -> `d09d8f62aa3851368556715d441d26c59ece1955` (HEAD)
  - `tests/Fitmate.UnitTests/PtSerializationContractTests.cs` -> `ce76f6fe7103293eb9480ba2402ac70a0cc9b280` (HEAD)
- Kết luận: working tree đã ở đúng baseline authoritative — không có patch regression nào đang dở. Status "M" trong `git status` chỉ là line-ending warning, không phải nội dung.

**Phần A — Regression patch (6 file) đã rà lại**
- `AvailabilityController.cs`: còn `GET /me/availability` (line 27), `PUT /me/availability` (line 48) với query/body `from`/`to`, scoped `ReplaceScopedAsync`, wire DTO `AvailabilitySlotResponse` (snake_case `slot_start_utc`, `slot_start_local`, `duration_minutes`, `status`) và `SetAvailabilityRequest` (`from`/`to`/`slots`). ✓
- `CertificatesController.cs`: còn `ISignedUrlService` injection cho `ListMine` (line 35) và `Submit` (line 59), wire DTO `CertificateResponse` (snake_case `pt_id`, `issued_by`, `issue_date`, `image_url`, `reject_reason`, `verified_at`) + `CertificatePageResponse` (`items`/`total`/`page`/`pageSize`), và `ImageKeyBelongsTo` ownership check (line 74, 86). ✓
- `ServicesController.cs`: còn wire DTO `ServiceResponse` (`id`/`name`/`description`/`price_vnd`/`duration_minutes`/`is_active`), `CreateServiceRequest`, `UpdateServiceRequest`, internal `ToResponse`. Domain entity không bao giờ thoát ra ngoài. ✓
- `TrainersPublicController.cs`: còn mapping `detail.Services.Select(ServicesController.ToResponse).ToList()` ở `Get(Guid ptUserId)` (line 44). Public DTO trùng PT owner contract. ✓
- `PtSerializationContractTests.cs`: còn đủ 6 test case (`ServiceResponse_SerializesToSnakeCase`, `CreateServiceRequest_DeserializesFromSnakeCase`, `AvailabilitySlotResponse_SerializesToSnakeCaseAndLowercaseEnum`, `SetAvailabilityRequest_DeserializesFromSnakeCase`, `CertificateResponse_SerializesToSnakeCaseAndLowercaseStringStatus`, `CertificatePageResponse_SerializesToItemsStructure`). File KHÔNG rỗng. ✓
- `docs/backend-release/openapi/fitmate-v1.yaml`: còn `GET /me/availability`, scoped replace parameters, `AppointmentStatus` enum (`[Held, Pending, Confirmed, InProgress, Completed, Cancelled, Expired]`) tách khỏi `PaymentStatus`/`RefundStatus`, `RecentBookingSummary`/`ActionRequired`/`AdminUserSummary` camelCase đúng schema Web. ✓

**Phần B — Admin API contract camelCase**
- Runtime DTO `AdminUserSummary` (`Fitmate.Application.Identity`) dùng C# record PascalCase (`Id`, `Email`, `FullName`, `Role`, `IsLocked`, `EmailConfirmed`, `CreatedAtUtc`) → ASP.NET Core default serializer (`AddControllers()` + `System.Text.Json` camelCase policy) phát ra camelCase JSON, khớp OpenAPI.
- Runtime DTO `ActionRequired` + `RecentBookingSummary` (`Fitmate.Application.Admin`) tương tự — record PascalCase `PendingCertificates`, `PendingReports`, `PendingPayments`, `RecentBookings` + `AppointmentId`, `MemberId`, `PtUserId`, `ActualPaidVnd`, `PlatformFeeVnd`, `Status`, `CreatedAt` → camelCase runtime.
- OpenAPI khai báo đúng `camelCase` cho 3 schema này, không đứt gãy contract v1. Không tạo breaking change.

**Phần C — Rà 7 module Admin**
- **Users (`AdminUserService` + `AdminUsersController`)**: có `ListAsync` (search/role/status/sort/page/pageSize theo `AdminUserListQuery`), `GetAsync`, `SetLockAsync` với last-admin guard (`CountActiveAdminsAsync` > 1) + bump `SecurityStamp` để thu hồi session/refresh-token. ✓
- **Certificates (`/api/v1/admin/certificates`)**: còn `ISignedUrlService` cấp preview URL cho Admin (image_url signed, không public), approve/reject/revoke với audit log; ownership bảo vệ qua `IAdminCertificateStore.GetByIdForReviewAsync` (chỉ lấy của PT đích). ✓
- **Bookings (`AdminBookingsController` + `BookingService.ListAdminAsync`)**: danh sách phân trang có filter theo `status`, `ptUserId`, `memberId`. Không lộ health/chat private (chỉ trả appointment metadata + slot + giá). ✓
- **Finance (`AdminFinanceController` + `AdminFinanceService`)**:
  - Manual refund: enforce role Admin ở Application, kiểm tra số tiền + state transition (`ProcessRefundAsync`), idempotency key, ghi `AuditLog`, attach vào payment. ✓
  - Manual settlement: tương tự qua `ManualSettlementService` + controller `POST /api/v1/admin/finance/settlements`, idempotency-key bắt buộc. ✓
  - Hiện `TOTP` step-up cho finance mutation chưa enforce ở Application (xem blocker demo).
- **Reports (`/api/v1/admin/reports`)**: list/resolve/dismiss đầy đủ. ✓
- **Support (`/api/v1/admin/support/tickets`)**: list/detail/reply/status đầy đủ, IDOR protection. ✓
- **Audit/Metrics/Action-Required/Dead-letter**: `/api/v1/admin/audit`, `/api/v1/admin/metrics/prometheus`, `/api/v1/admin/metrics/summary`, `/api/v1/admin/action-required`, `AdminOutboxService.ReplayAsync` (financial replay guard, atomic conditional update, audit). ✓
- **MFA**: `/api/v1/admin/auth/mfa` setup/verify/disable, recovery codes (10), bump `SecurityStamp` để revoke session. ✓

**Phần D — Debt trạng thái booking (BookingStatus gộp)**
- `Fitmate.Domain.Common.Enums.BookingStatus` vẫn giữ 9 giá trị `Held, AwaitingPayment, Paid, Completed, Cancelled, Refunded, Expired, Confirmed, InProgress`. OpenAPI target tách thành 3 enum `AppointmentStatus` ([Held, Pending, Confirmed, InProgress, Completed, Cancelled, Expired]), `PaymentStatus` ([Pending, Paid, Failed, Refunded, PartialRefunded]), `RefundStatus` ([Pending, Succeeded, Failed]) theo DEC-026 / DEC-051.
- Runtime vẫn dùng `BookingStatus` ở 9 file:
  - `Fitmate.Domain.Booking.Appointment` (transition methods `AttachPayment`, `MarkRefunded`, `ConfirmByPt`, `StartByPt`, `CompleteByPt`).
  - `Fitmate.Infrastructure.Booking.EfBookingStores`, `Fitmate.Infrastructure.Payments.EfPaymentStore`, `Fitmate.Infrastructure.Identity.EfAccountDeletionStore`, `Fitmate.Infrastructure.Dashboard.EfDashboardStore`, `Fitmate.Infrastructure.Finance.EfAdminFinanceStore`, `Fitmate.Infrastructure.Finance.EfTrainerRevenueStore`, `Fitmate.Infrastructure.Admin.EfAdminMetricsStore`, `Fitmate.Application.Booking.BookingService`.
- File bị ảnh hưởng (blocker): bất kỳ nơi `Appointment.Status` được đọc/ghi trong query sẽ phải ánh xạ `BookingStatus` → `AppointmentStatus`/`PaymentStatus`/`RefundStatus`.
- **Không tự ý thực hiện migration phá vỡ** trong phiên này. Mapping 9 trạng thái → 3 enum + phân tầng cột DB (thêm `payment_status_id`/`refund_id`) cần:
  - Kế hoạch migration additive (giữ cột `Status` cũ + thêm cột mới, backfill song song, dọn cột sau khi admin UAT đối chiếu xong).
  - OpenAPI: đã có target schema, cần đồng bộ DTO trả về runtime (`AppointmentResponse.Status` ↔ `AppointmentStatus`; thêm `PaymentStatus`; thêm `RefundStatus`).
  - Bộ test đối chiếu cũ/mới (`BookingStatusAdapterTests`, fixture admin book detail, admin finance reconciliation).
  - Task mới đề xuất: **BE-829 (tách trạng thái booking theo DEC-051/026)** — chưa khởi tạo, chờ user duyệt scope.
- Tạm thời đã ghi nhận debt trong mục "Blocker còn lại" bên dưới; không đụng code runtime.

**Phần E — Bảo mật**
- Không thêm secret mới, không chạm `.env`, không log JWT/password/TOTP/recovery code. ✓
- Không sửa `.gitleaks.toml` allowlist (Supabase legacy credentials đã được thu hẹp trước đó ở BE-822). ✓
- Không rewrite Git history. Không deploy. Không gọi payment provider. Không tạo dữ liệu smoke/load. ✓

**Kết quả build & test**
- `dotnet build Fitmate.slnx` -> **0 Warning(s), 0 Error(s)**.
- `dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --no-build` -> **714 Passed, 0 Failed, 0 Skipped, Total 714** trong ~1 giây.
- 6/6 `PtSerializationContractTests` PASS — bảo toàn wire contract snake_case cho PT endpoints.

**Trạng thái Git sau phiên**
- Không stage file nào. Không `git add`/`git commit`. Tất cả thay đổi của phiên trước đã có sẵn trong working tree (content = HEAD) và KHÔNG đổi trong phiên này.
- Helper script `verify-equal.ps1` (do agent tạo để verify blob hash) đã được xóa sau khi xác minh xong. Diff sạch ở mức nội dung.
- Trạng thái lịch sử: `AI/` là bundle inference local chưa được review; patch rỗng cũ đã được xóa trong đợt cleanup 2026-10-02.

**Blocker còn lại**

Demo blocker
- TOTP step-up cho finance mutation chưa enforce ở Application layer. Hiện chỉ yêu cầu Admin role + Idempotency-Key + audit log + state transition. Bổ sung `IAdminTotpGuard.RequireFreshAsync(adminId, action, ct)` + header `X-Admin-TOTP` trước khi demo live.
- Debt trạng thái booking: runtime còn dùng `BookingStatus` gộp `Paid`/`Refunded`/`AwaitingPayment` (xem Phần D). API và FE Web/Mobile đang tạm chấp nhận các giá trị legacy; cần migration target theo DEC-026 trước khi freeze contract cho Web.
- Dead-letter replay (`AdminOutboxService.ReplayAsync`) chỉ verify đơn vị. Cần integration test trên PostgreSQL thật (CI integration-test job) để xác nhận atomic conditional update + audit log persists.

Production blocker
- `BookingStatus` debt (Phần D) — cần migration additive + OpenAPI/DTO đồng bộ trước release.
- Thiếu `count_active_admins_async` semantics cho boundary `web/Mobile` chưa verify end-to-end (chỉ có unit test).
- CORS production env chỉ fail-closed nếu `CORS_ALLOWED_ORIGINS` được set; hiện managed deploy chưa có env này.
- Backup/DR drill (BE-825) chỉ chạy local; chưa có operator approval cho offsite storage + KMS custody (OPS-04/05).
- Runtime hiện `net8.0` (EOL 10/11/2026); BE-828 chỉ ở giai đoạn PREPARATION. Cần verify lên `net10.0` trước ngày EOL.

**Lệnh verification cho user (theo đúng thứ tự)**

```powershell
# 1. Xác minh working tree không có diff (nội dung)
git -C "D:/Ky8/exe201/FitmateAI_BE" -c core.autocrlf=false diff --stat

# 2. Xác minh 6 file PT khớp HEAD blob
foreach ($f in @(
  "src/Fitmate.Api/Trainers/AvailabilityController.cs",
  "src/Fitmate.Api/Trainers/CertificatesController.cs",
  "src/Fitmate.Api/Trainers/ServicesController.cs",
  "src/Fitmate.Api/Trainers/TrainersPublicController.cs",
  "tests/Fitmate.UnitTests/PtSerializationContractTests.cs",
  "docs/backend-release/openapi/fitmate-v1.yaml")) {
  git -C "D:/Ky8/exe201/FitmateAI_BE" hash-object $f
}

# 3. Build sạch
dotnet build "D:/Ky8/exe201/FitmateAI_BE/Fitmate.slnx" -c Debug --nologo

# 4. Unit tests
dotnet test "D:/Ky8/exe201/FitmateAI_BE/tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj" --no-build --nologo

# 5. Format guard
dotnet format "D:/Ky8/exe201/FitmateAI_BE/Fitmate.slnx" --verify-no-changes --no-restore
```

**Next (historical, đã được thay thế):** Nội dung này không còn là chỉ dẫn hiện tại; xem mục đầu file và `tasks.md`.

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
  - Allowlist regexes: `__SET_*__`, demo emails `*@fitmate.vn`, CI ephemeral strings. Historical Supabase credential is never allowlisted and is tracked under BE-099.
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

- Bổ sung trong cùng phiên: IFinanceStepUp/FinanceStepUp kiểm Admin MFA bằng X-Finance-Totp; tests nguồn cho role/MFA, PostgreSQL concurrent receipts/refund hold; timestamp receipt chuẩn hóa microseconds. Chưa chạy bất kỳ build/test/migration nào.
## 2026-09-27 — Admin completion execution docs
- Khảo sát Admin Web manifest/App/Login và BE controller routes/auth/manual finance. Tailwind4 đã có; web còn auto-admin/mock/localStorage. Phát hiện login-MFA/enrollment và business KPI contract cần đối chiếu bổ sung.
- Thêm bộ docs tại FitmateWebApp/docs/production/admin-completion: 7 files về scope/API gaps/architecture/security/task execution/verification/prompt, liên kết từ README/tracker/plans index.
- BE coordination: [admin-web-integration-handoff.md](admin-web-integration-handoff.md). Chỉ docs; chưa sửa runtime, chưa build/test/install/commit; không thay trạng thái release.
## 2026-09-27 — Restore dropped work and normalize commit subjects
- User explicitly authorized recovery and history normalization on backend-release/contract-and-foundation. Preserved other contributors' author/committer identities and all historical commit trees; normalized 35 subjects without bodies or attribution trailers.
- Restored the seven files from e72601f after verifying the staged tree exactly matched that recovery commit; recovery recorded separately as ab0b6a1. This restores Swagger, verify port isolation, test database isolation and their documentation.
- Local backup branches preserve pre-normalization HEAD and e72601f; a Git bundle and commit map were saved outside the repository. Historical SHA references in old evidence remain historical and must not be treated as the current release revision.
- Updated AGENTS and Cursor policy: one English subject, maximum 72 characters, no body, owner email votanthinhcri28@gmail.com; retain original contributor identities. No build, test, migration, service restart or push performed.

## 2026-09-28 — Admin Web finalization: cert pagination, cert ownership, idempotency scope, BE-152 MFA verify

### Cert list pagination (CERT-01)
- `Fitmate.Application/Trainers/ICertificateService.ListTrainerForAdminAsync` giờ trả `(IReadOnlyList<Certificate> Items, int Total)` với `page` (≥1) + `pageSize` (1..100, mặc định 25). Cùng signature được thêm vào `CertificatePage` DTO.
- Store: `Fitmate.Infrastructure/Trainers/EfTrainerStores.ListByPtPagedAsync` dùng `Skip((page-1)*pageSize).Take(pageSize)`.
- Controller: `Fitmate.Api/Trainers/AdminCertificatesController.GET /admin/certificates/trainer/{ptUserId}` chấp nhận `?page&pageSize`, response shape = `{ items, total, page, pageSize }`.
- Tests: `tests/Fitmate.UnitTests/CertificateServiceTests.cs` — `ListTrainer_Pagination_ReturnsCorrectSlice` (page 2 size 2 của 5 certs), `Certificate_Submit_PersistsImageUploadedByUserId`.

### Cert ownership (CERT-02)
- Domain: `Certificate.ImageUploadedByUserId` (nullable Guid) lưu user thực sự upload cert lên storage.
- Migration: `Fitmate.Infrastructure/Persistence/Migrations/20260928130000_CertificateOwnership.cs` — thêm nullable column + index `IX_certificates_ImageUploadedByUserId`; KHÔNG backfill chủ sở hữu cho dữ liệu cũ (legacy rows = null).
- Upload path: `CertificatesController.Submit` (PT) ghi `uploadedByUserId = currentUser.Id`. Cập nhật `ICertificateStore.SubmitAsync`/`CertificateService.SubmitAsync` để nhận tham số này.
- Ownership gate: `AdminCertificatesController.Preview(id)`:
  - 404 NotFound nếu `cert.ImageStorageKey` null / `ImageUploadedByUserId` null (legacy + cert chưa có key).
  - 403 Forbid nếu `ImageUploadedByUserId != cert.PtUserId` (admin muốn lấy link chứng chỉ của PT khác).
  - 200 OK + ghi audit `certificate.preview.viewed` nếu `ImageUploadedByUserId == cert.PtUserId`.
- Tests: `tests/Fitmate.UnitTests/AdminCertificatesControllerOwnershipTests.cs` — 3 case: missing uploader (404), mismatch (403), match (200 + audit).

### Password policy — thống nhất 12 ký tự (user chốt 2026-09-28)

Có **5 nơi lệch nhau**, gây vòng bất khả thi: đăng ký qua API được mật khẩu 8 ký tự
(`RegisterRequestValidator`), nhưng Admin Web bắt `minLength={12}` khi đổi mật khẩu — chính
admin đó không thể đổi mật khẩu mà không tăng độ dài.

Đã tạo `src/Fitmate.Domain/Common/PasswordPolicy.cs` (`MinimumLength = 12`, `MaximumLength = 128`,
`MinimumLengthMessage`, `IsAcceptable()`) và thay ở:

- `AuthValidators`: `RegisterRequestValidator`, `ResetPasswordRequestValidator`, `ChangePasswordRequestValidator`
  (từ `MinimumLength(8)` → `PasswordPolicy.MinimumLength`).
- `Program.cs`: `IdentityOptions.Password.RequiredLength`.
- `SeedAdminCommand` (check STDIN + Identity), `SeedPerfDataCommand`, `SeedUatDataCommand`.
- `openapi/fitmate-v1.yaml`: `minLength: 8 → 12` ở register / reset / change.
- Web `Settings.tsx` **không đổi** (đã là 12).

**Phạm vi có chủ đích**: chỉ mật khẩu MỚI. Không tác động hash hiện có, không buộc tài khoản
đang tồn tại đổi mật khẩu, không vô hiệu hóa phiên đăng nhập. Tài khoản cũ có mật khẩu 8–11 ký tự
vẫn đăng nhập bình thường; chỉ khi họ chủ động đổi/đặt lại mật khẩu mới chịu ràng buộc 12.

**Ngoại lệ có chủ đích**: `SeedPerfDataCommand` vẫn bật `RequireUppercase/RequireLowercase/RequireNonAlphanumeric = true`
(mật khẩu persona test tự sinh, nghiêm hơn sản phẩm). Không nới về policy chung vì dữ liệu test không nên trùng mẫu yếu của production. Đã ghi chú trong code.

Test: `tests/Fitmate.UnitTests/PasswordPolicyTests.cs` (NEW, 10 case).

### Idempotency scope fix
- Bug: `IdempotencyMiddleware.ShouldStore` cache cả 4xx (kể cả 400 validation), làm retry với payload đã sửa vẫn bị replay 400.
- Fix: chỉ store 2xx và 409 Conflict (`statusCode is >= 200 and < 300 || statusCode == 409`). 4xx khác (400, 422) cho phép retry với payload mới.
- Tests: `tests/Fitmate.UnitTests/IdempotencyMiddlewareTests.cs` — `Validation_400_Is_NOT_Stored_ForReplay`, `Retry_After_400_Reaches_Controller_With_Fixed_Body`, `Conflict_409_IS_Stored_ForReplay`.

### Status changes
- BE-152 `POST /admin/auth/login/verify` + challenge expiry/reuse: source đã có trong `AuthController.Verify` (gọi `IAdminLoginChallengeService`) + `AdminLoginChallengeService` (single-use, 5 phút TTL). Không thay đổi code; đã rà soát static.
- BE-150 `POST /admin/auth/login` MFA enforcement: xác nhận `AuthController.Login` reject nếu `Status == PendingDeletion` (403), dùng `Identity.ExternalLoginInfo` mapping, return 202 challenge khi `RequiresMfa` true. Không thay đổi code.
- Outbox: chưa chạy; cần smoke manual settlement để xác nhận `OutboxBackgroundService` enqueue + process.

### Open (chưa giải)
- Migration `20260928130000_CertificateOwnership`: chưa apply vào DB nào. Cần user chạy `dotnet ef database update` trên local Postgres trước, kiểm tra `psql \d certificates` thấy column `ImageUploadedByUserId uuid NULL` + index `IX_certificates_ImageUploadedByUserId` đã tạo.
- Finance step-up: **ĐÃ enforce đúng**. `ManualSettlementService` gọi `stepUp.EnsureAsync(adminId, ct)` trên `CreateRefundAsync`, `ConfirmRefundAsync`, `ApproveAsync`, `HoldAsync`, `RecordTransferAsync` — tức ở Application layer, không phải chỉ controller. `FinanceStepUp` đọc header `X-Finance-Totp`, yêu cầu 6 chữ số ASCII, `VerifyTwoFactorTokenAsync`, `AccessFailedAsync` khi sai. Test source mới: `tests/Fitmate.UnitTests/FinanceStepUpTests.cs` (11 case, mock `UserManager` + `IUserLockoutStore`). Token TOTP thật vẫn cần integration test.
- `OpenAPI/fitmate-v1.yaml`: **đã cập nhật 2026-09-28** — thêm `/admin/certificates/trainer/{ptUserId}` (paged), `/admin/certificates/{id}/preview` (403/404 ownership), `/admin/certificates/{id}/revoke`, `/admin/bookings/{id}/review`, `/admin/trainers/{id}/review`, `/admin/auth/login/verify`, response `202` challenge của `/auth/login`. CHƯA validate YAML parse — user chạy lệnh validate.
- `authorization-matrix.md`: đã thêm mục "Cert image preview ownership" mô tả bảng điều kiện 404/403/200.
- `feature-api-matrix.md` / `api-contract.md`: CHƯA cập nhật (bảng cấp feature, không liệt kê từng endpoint; `api-contract.md` chưa đọc lại để khẳng định là cần).

### Sửa lỗ hổng storage key (2026-09-28) — phát hiện khi rà soát CERT-02 sâu hơn

- `UploadsController` tạo key dạng `{category}/{safeName}_{guid}{ext}` — **không chứa userId**. Hệ quả: một PT có thể lấy storage key của tài khoản khác (từ signed URL lúc upload) rồi submit key đó cho cert của mình. Khi đó `ImageUploadedByUserId` = PT đó = `PtUserId`, nên check "uploader == owner" ở preview **vô hiệu hoàn toàn** — đúng cái lỗ hổng mà CERT-02 cố đóng.
- Đã sửa `UploadsController`: key nay là `{category}/{userIdN}_{safeName}_{random}{ext}`; thiếu user id claim → 401 thay vì ghi `"anonymous"` vào log + key.
- Đã sửa `CertificatesController.Submit`: kiểm `ImageKeyBelongsTo(storageKey, currentUserId)` trước khi ghi `ImageUploadedByUserId`; sai scope → `DomainException("CERT_IMAGE_NOT_OWNED", ..., 403)`. Có `internal static` để test được mà không cần HTTP.
- Test: `tests/Fitmate.UnitTests/CertificateImageKeyOwnershipTests.cs` (NEW, 9 case: key của chính mình 2 shape, thiếu `_` separator, key của user khác, key legacy không có scope, 7 dạng malformed gồm traversal, trim, case-insensitive).
- **Tác động dữ liệu**: không có. Chỉ đổi format key cho **file upload mới**. Storage key cũ vẫn đọc được qua signed URL; nhưng nếu muốn submit lại cert cũ bằng key cũ thì sẽ bị 403 (đúng — không chứng minh được chủ).

### Sửa lỗi phát hiện khi đối chiếu docs ↔ code (2026-09-28)
- `AdminCertificatesController.Preview` trả `Forbid()` (403) cho cả trường hợp `ImageUploadedByUserId == null`. Điều này **khớp ngược** với quyết định "fail-safe 404" đã ghi ở `requirement-matrix.md`/`risks-and-verification.md`/`authorization-matrix.md`/OpenAPI. Đã sửa code: `null` → `NotFound()` (không tiết lộ sự tồn tại của tài nguyên không xác minh được chủ); mismatch uploader ≠ owner → `Forbid()` (403).
- `IdempotencyMiddleware` comment nội bộ ghi "chỉ store JSON 2xx/4xx" và remark nói "5xx stored for read-only inspection" — mâu thuẫn với `ShouldStore` (chỉ 2xx + 409). Đã sửa cả hai comment cho khớp code.
- Handoff cũ ghi "Finance controller chưa wire `X-Finance-Totp`" — SAI. Đã đối chiếu source: `ManualSettlementService` gọi `EnsureAsync` trên cả 5 mutation. Đã sửa handoff.

### Tests
- Chưa chạy `dotnet test` (theo lệnh user). Đã viết test source cho CERT-01, CERT-02, idempotency scope, finance step-up.
  - Chi tiết file + số case: `docs/archive/review-handoff-2026-09-29/requirement-matrix.md` (FIN-07, CERT-01, CERT-02, FIN-09).

### PowerShell commands (chạy local)

```powershell
# 1. Build
Set-Location D:\Ky8\exe201\FitmateAI_BE
dotnet build src/Fitmate.Api/Fitmate.Api.csproj --nologo

# 2. Unit tests — chạy riêng từng nhóm viết ở đợt này
dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --nologo --verbosity minimal `
  --filter "FullyQualifiedName~Certificate"
dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --nologo --verbosity minimal `
  --filter "FullyQualifiedName~IdempotencyMiddleware"
dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --nologo --verbosity minimal `
  --filter "FullyQualifiedName~FinanceStepUp"
dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --nologo --verbosity minimal `
  --filter "FullyQualifiedName~AuthSession"
dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --nologo --verbosity minimal `
  --filter "FullyQualifiedName~PasswordPolicy"

# 3. Toàn bộ unit tests (khuyến nghị trước bàn giao)
dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --nologo --verbosity minimal

# 4. Validate OpenAPI sau khi thêm 6 path mới
Set-Location D:\Ky8\exe201\FitmateAI_BE
npx @redocly/cli lint docs/backend-release/openapi/fitmate-v1.yaml
```

### Migration `20260928130000_CertificateOwnership` — pre-flight

1. **DB đích**: chỉ áp dụng lên môi trường local Postgres (Docker compose service `postgres`) hoặc staging clone.
   **KHÔNG** chạy trên production cho tới khi operator duyệt plan rollback. Production là managed Postgres DigitalOcean (DEC-030) — cần backup của nhà cung cấp trước, xem `runbooks/`.
2. **Tác động dữ liệu**:
   - Thêm **một cột nullable** `image_uploaded_by_user_id uuid NULL` + index trên bảng `certificates`.
   - **Không backfill.** Mọi cert có sẵn sẽ có `NULL`.
   - Hệ quả nghiệp vụ: `GET /admin/certificates/{id}/preview` trả **404** cho toàn bộ cert cũ tới khi có bằng chứng ai upload. Admin vẫn thấy metadata (list/approve/reject/revoke) — chỉ mất khả năng xem ảnh. Đây là hành vi fail-safe có chủ đích.
   - Không `NOT NULL`, không default, không rewrite bảng lớn → lock nhẹ, thời gian thực thi ngắn.
3. **Thứ tự thao tác**:
   1. Backup local: `docker exec fitmate-postgres-1 pg_dump -U fitmate -d fitmate > cert-backup.sql`
      (production: dùng managed backup của nhà cung cấp, không `pg_dump` ad-hoc qua tunnel).
   2. Xem trạng thái migration hiện tại:
      `dotnet ef migrations list --project src/Fitmate.Infrastructure --startup-project src/Fitmate.Api`
   3. Apply: `dotnet ef database update --project src/Fitmate.Infrastructure --startup-project src/Fitmate.Api`
   4. Kiểm tra schema đích:
      `docker exec -it fitmate-postgres-1 psql -U fitmate -d fitmate -c "\d certificates"`
      Expect: cột `image_uploaded_by_user_id uuid NULL` + `index IX_certificates_ImageUploadedByUserId`.
   5. Restart API để controller mới đọc column.
   6. Smoke test (mục kế tiếp).
4. **Backfill (tùy chọn, chỉ khi operator có bằng chứng)**:
   ```sql
   -- CHỈ chạy khi đã xác minh được mọi cert cũ thuộc đúng PtUserId.
   UPDATE certificates
      SET image_uploaded_by_user_id = pt_user_id
    WHERE image_storage_key IS NOT NULL
      AND image_uploaded_by_user_id IS NULL;
   ```
   Không tự ý chạy câu này. Không có bằng chứng thì để `NULL` — preview trả 404 là hành vi đúng.
5. **Rollback**:
   ```powershell
   dotnet ef database update 20260928120000_AdminSessionSecurity `
     --project src/Fitmate.Infrastructure --startup-project src/Fitmate.Api
   ```
   Migration `Down` drop index rồi drop column. Mất dữ liệu cột đã backfill (nếu có) — lưu ý trước khi rollback.

### Smoke test sau khi apply (user thực hiện)

```powershell
# 1) Khởi động Postgres + API
Set-Location D:\Ky8\exe201\FitmateAI_BE
docker compose up -d postgres
docker compose up -d api

# 2) Đăng nhập admin (MFA nếu đã bật) — lấy access token
$admin = Invoke-RestMethod -Method Post -Uri http://localhost:5116/api/v1/auth/login `
  -Headers @{ 'X-Fitmate-Client' = 'admin-web' } `
  -ContentType 'application/json' `
  -Body (@{ email = '<admin-email>'; password = '<password>' } | ConvertTo-Json)
$h = @{ Authorization = "Bearer $($admin.accessToken)"; 'X-Fitmate-Client' = 'admin-web' }

# 3) Danh sách cert phân trang: page/pageSize phải được echo lại trong response
Invoke-RestMethod -Uri "http://localhost:5116/api/v1/admin/certificates/trainer/<ptUserId>?page=1&pageSize=25" -Headers $h

# 4) Preview:
#    cert MỚI (submit sau migration)  -> 200 + { url }
#    cert CŨ (trước migration)         -> 404 (ImageUploadedByUserId NULL)
Invoke-RestMethod -Uri "http://localhost:5116/api/v1/admin/certificates/<certId>/preview" -Headers $h

# 5) Idempotency sau 400: cùng key + payload đã sửa phải tới controller (không bị replay 400)
#    Xem hành vi mong đợi trong tests/Fitmate.UnitTests/IdempotencyMiddlewareTests.cs
```

### Smoke test password policy (chạy được không cần migration)

```powershell
Set-Location D:\Ky8\exe201\FitmateAI_BE
docker compose up -d postgres api

# A) Đăng ký với mật khẩu 8 ký tự → phải bị từ chối 400 với message "at least 12 characters"
try {
  Invoke-RestMethod -Method Post -Uri http://localhost:5116/api/v1/auth/register `
    -ContentType 'application/json' `
    -Body (@{ email = 'pwshort@test.local'; password = 'Short1!'; full_name = 'Short Pass'; role = 'member' } | ConvertTo-Json)
  Write-Host "SAI: đăng ký 8 ký tự phải bị từ chối" -ForegroundColor Red
} catch {
  Write-Host "OK: $($_.Exception.Response.StatusCode.value__) $($_.ErrorDetails.Message)" -ForegroundColor Green
}

# B) Đăng ký với mật khẩu 12 ký tự → 200/201
Invoke-RestMethod -Method Post -Uri http://localhost:5116/api/v1/auth/register `
  -ContentType 'application/json' `
  -Body (@{ email = 'pwlong@test.local'; password = 'TwelveChars1'; full_name = 'Long Pass'; role = 'member' } | ConvertTo-Json)

# C) Tài khoản cũ (đăng ký trước khi deploy bản này, mật khẩu 8 ký tự) vẫn đăng nhập được
#    → xác nhận không có tác động tới hash/phiên hiện hữu.
```

## 2026-09-29 — PT Wire Contracts (BE-801) & Scoped Availability Updates (BE-807)

- **BE-801 PT Wire Contracts**:
  - `ServicesController`: Created explicit `ServiceResponse`, `CreateServiceRequest`, and `UpdateServiceRequest` with `snake_case` JSON properties (`id`, `name`, `description`, `price_vnd`, `duration_minutes`, `is_active`). Replaced direct domain entity exposure.
  - `CertificatesController`: Created `CertificateResponse` and `CertificatePageResponse` with `snake_case` JSON properties (`id`, `pt_id`, `title`, `issued_by`, `code`, `issue_date`, `status`, `image_url`, `reject_reason`, `verified_at`). Status serialized as lowercase string enum (`pending`, `approved`, `rejected`, `revoked`). Image URL generated via presigned read URL from `ISignedUrlService`.
  - `TrainersPublicController`: Mapped `Services` in `GetDetailAsync` to `ServiceResponse` so `PTPublicDetail.services` conforms to OpenAPI `Service` schema.
  - Added unit test suite `tests/Fitmate.UnitTests/PtSerializationContractTests.cs` (6 tests).

- **BE-807 Scoped Availability Updates**:
  - `IAvailabilityService` / `AvailabilityService`: Added `ListAuthoritativeForPtAsync` (returns all `Open`, `Held`, `Booked` slots for PT in range `[fromUtc, toUtc)`) and `ReplaceScopedAsync` (transactionally replaces only `Open` slots within `[fromUtc, toUtc)`, protecting `Open` slots outside range and never touching `Held`/`Booked` slots).
  - Validation: rejects slots outside range, rejects batch interval overlaps (`ConflictException` 409), rejects overlap with existing `Held` or `Booked` slots (`ConflictException` 409), validates duration (15..240), computes UTC based on PT profile timezone (`user.Timezone`, fallback `Asia/Ho_Chi_Minh`).
  - `IAvailabilitySlotStore` / `EfAvailabilitySlotStore`: Added `ReplaceScopedAsync` and `GetPtTimezoneAsync`.
  - `AvailabilityController`: Added owner `GET /api/v1/me/availability?from=&to=` returning `AvailabilitySlotResponse[]` (`id`, `slot_start_utc`, `slot_start_local`, `duration_minutes`, `status`). Updated `PUT /api/v1/me/availability` to support scoped replacement when `from` and `to` are passed (query or body), preserving legacy behavior when omitted.
  - Added unit tests in `AvailabilityServiceTests.cs` (5 new tests covering scoped replacement, boundary validation, batch overlap rejection, held/booked conflict rejection, and authoritative listing).
  - Test evidence: 711 unit tests passing in `Fitmate.UnitTests` (100% pass, 0 fail).

## 2026-09-29 — Admin demo static repair (unverified)

- Hardened Admin user list/detail authorization in `AdminUserService`; invalid pagination is rejected before the store call.
- Hardened pending certificate list authorization in `CertificateService` and normalized its page/page-size response.
- Hardened Outbox stats/list/replay with Application-layer Admin authorization. Admin metrics now reads Outbox state through that guarded service.
- Invalid Admin booking status filters now return a validation error instead of silently returning all bookings.
- Added `http://localhost:5174` to the example/local CORS origins for the active Admin Vite port. The local `.env` edit remains ignored and must never be staged.
- No build, tests, Docker, migration, or runtime smoke was executed in this pass. Status is **IMPLEMENTED_UNVERIFIED** pending the operator commands in the Admin Web demo-readiness handoff.
- Two Admin schema migrations remain pending for the target database: `20260928120000_AdminSessionSecurity` and `20260928130000_CertificateOwnership`.
- The untracked `AI/` bundle is outside this Admin demo patch. Do not use broad `git add .` in the BE repository; stage reviewed paths explicitly so model weights and local artifacts are not pushed accidentally.

## 2026-09-29 — Admin verification build repair (awaiting rerun)

- Operator build reached all production projects but failed while compiling `AdminMetricsServiceTests`: its `RecentAppointmentRow` fixture still used the pre-`platformFeeVnd` constructor. The fixture now supplies a platform fee.
- Updated the direct vulnerable dependencies reported by NuGet audit: `Microsoft.AspNetCore.DataProtection` `10.0.0` -> `10.0.12` and `MailKit` `4.10.0` -> `4.18.1`. Data Protection `10.0.12` also requires the patched `System.Security.Cryptography.Xml` `10.0.12`; MailKit `4.18.1` requires the matching patched MimeKit dependency. All direct Microsoft runtime/EF package references and `dotnet-ef` pins were aligned to `10.0.12` to avoid a mixed servicing graph; Npgsql remains on its compatible `10.0.0` provider release.
- Removed current compiler warnings in the two seed commands by preserving their validated non-null password invariant, and migrated forwarded-network configuration to the .NET 10 `KnownIPNetworks` API.
- No command was rerun by the agent. The operator must rerun restore/build/unit/integration tests and confirm the NuGet audit warnings are gone.
- A production deployment of the Data Protection fix still requires the documented Data Protection key-ring rotation procedure; do not delete or rotate local/shared keys as part of a build check.
- First operator rerun confirmed restore succeeds and all NuGet vulnerability warnings are gone. The next compile exposed an expression-tree-incompatible assertion and nullable SMTP password warning; the assertion now uses separate strongly typed checks, and SMTP configuration now fails closed when only one credential half is supplied. Awaiting the next operator rerun.
- The subsequent integration run reached 45 tests: 26 passed and 19 failed. Failures reduced to three shared fixture defects rather than 19 independent business failures: legacy synthetic JWTs lacked persisted auth sessions (401), parallel WebApplicationFactory hosts raced while seeding Identity roles (`RoleNameIndex`), and UAT journey 04 persisted an Appointment with an orphan `PaymentId`.
- Integration authentication now creates/updates the Identity user, assigns its role, establishes the security stamp/MFA state, persists an `AuthSession`, and issues a JWT containing matching `sid`, `security_stamp`, and `amr` claims. Shared PostgreSQL integration tests are serialized, the revenue journey now persists its Payment ledger row, and production role bootstrap now uses a PostgreSQL transaction advisory lock to be safe across concurrent API instances.
- The remaining UAT Journey 09 test no longer invokes the retired aggregate PT payout method. It now exercises the production manual settlement workflow for one completed and paid appointment: admin MFA step-up, evidence approval, transfer recording, settlement persistence, and both audit actions. The integration fixture computes a current RFC 6238 authenticator code from its isolated test user's key; production MFA behavior is not bypassed.
- Verification completed on 2026-09-29:
  - `dotnet build tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj --no-restore --verbosity minimal /m:1` -> **PASS**, 0 warnings, 0 errors.
  - `dotnet test tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj --no-build --no-restore --logger "console;verbosity=minimal"` run 1 -> **45/45 PASS**, 0 skipped (12 s).
  - Same integration command run 2 -> **45/45 PASS**, 0 skipped (9 s).
  - Same integration command run 3 -> **45/45 PASS**, 0 skipped (9 s).
- Integration status is **VERIFIED** for this working tree. Full solution/unit verification and runtime/Admin Web smoke remain separate release gates.

## 2026-09-29 — CI secret scan and container scan repair

- Diagnosed the failed Gitleaks job: all 11 reported findings were exact public test vectors, local performance-only credentials, immutable evidence hashes, or false matches against the `dbPassword` C# variable name. No production credential was present in the report.
- Tightened the custom database-password rule so it only recognizes separated environment/configuration names such as `DB_PASSWORD` and `db-password`. Added value-scoped allowlists for the reviewed test vectors and local benchmark values; no source or documentation directory was excluded from scanning.
- Replaced the nonexistent `aquasecurity/trivy-action@0.28.0` ref with the official `v0.36.0` release. The failing Docker job had stopped while resolving the action, before either Dockerfile was built.
- Repository diff checks passed before commit. The definitive Gitleaks and action-resolution verification remains a new GitHub Actions run after the commit is pushed.

## 2026-09-29 — Local Compose hot-reload repair (verified)

- `docker compose up api` reused the production-style `fitmate-api:local` image, whose `ENTRYPOINT` caused the override's `dotnet watch` command to be interpreted as the application's `dotnet` CLI argument.
- The local override now uses the distinct `fitmate-api-dev:local` image, exposes only port `5116`, and aligns `ASPNETCORE_URLS` with that port.
- `Dockerfile.dev` now uses the .NET 10 SDK required by the current `net10.0` projects.
- Operator rerun required: `docker compose up --build api`, then verify `/health/ready` and `/swagger` on `http://localhost:5116`.
- First rerun built all four .NET 10 projects with 0 warnings and 0 errors, then `dotnet watch` encountered a Windows `obj\\Debug` path inside the Linux bind mount. The override now masks every project `bin`/`obj` directory with container-only anonymous volumes. Rerun with renewed anonymous volumes before runtime verification.
- Verification after `docker compose up --build --force-recreate --renew-anon-volumes api`: all four projects built with 0 warnings and 0 errors; role bootstrap and outbox queries reached PostgreSQL; Kestrel listened on `http://[::]:5116`; `GET http://127.0.0.1:5116/health/live` returned HTTP 200. The `dotnet watch` static-web-assets read message and failed in-container browser launch are non-fatal tooling diagnostics; the application remains running.

## 2026-09-29 — Raw JWT claim mapping repair (awaiting MFA rerun)

- Admin MFA enrollment repeatedly failed immediately after a fresh login even though the local Admin was Active, not locked, and had MFA disabled.
- Root cause: JWT bearer validation disabled inbound claim mapping to preserve wire claims such as `sid`, but `OnTokenValidated` still resolved only the legacy `ClaimTypes.NameIdentifier`, and authorization retained the legacy role claim type. Raw setup tokens therefore carried valid `sub`/`role` claims but were rejected before `AdminMfaController`.
- JWT validation now resolves the raw `sub` claim with a legacy fallback and explicitly sets `NameClaimType = "sub"` and `RoleClaimType = "role"`. Operator must log in again to obtain a fresh setup token and rerun MFA setup/verification.

## 2026-09-29 — Admin MFA challenge verification repair (awaiting runtime rerun)

- Applied the four pending migrations through the migration bundle, including `20260928120000_AdminSessionSecurity` and `20260928130000_CertificateOwnership`. The local database now contains the Admin login challenge/session tables and certificate ownership columns.
- Diagnosed `POST /api/v1/admin/auth/login/verify` HTTP 500: `EfAdminLoginChallengeService.VerifyAsync` opened a user transaction directly while PostgreSQL used `NpgsqlRetryingExecutionStrategy`.
- The complete row-lock, factor verification, failed-attempt update, and commit operation now runs through `Database.CreateExecutionStrategy().ExecuteAsync<Guid?>`, preserving the existing one-use challenge and recovery-code guarantees.
- The SDK container compiled all four production projects with **0 warnings and 0 errors**. The API still requires `docker compose up -d --build --force-recreate --renew-anon-volumes api` because a container restart retained stale anonymous `obj` volumes; the operator declined that recreate during this pass, so the fixed `/verify` request has not yet received a runtime response.
- Admin Web now renders password login and MFA challenge as separate stages. During the MFA stage the password field is absent, only the Authenticator/recovery-code input is submitted, and returning to password login clears the challenge state. Web `typecheck` and `lint` both pass.
- The first successful MFA login then exposed a separate wire-claim mismatch: `JwtTokenService` emitted the legacy .NET role URI while JWT bearer validation intentionally expected the raw `role` claim. This authenticated the access token but made every `AdminOnly` policy return 403. Token issuance now emits `role` directly and its wire-format unit assertion was updated. A fresh login is required after the API rebuild because already-issued access tokens retain their original claims.

## 2026-09-29 — Legacy Spring Boot and Supabase current-tree removal

- Operator confirmed Fitmate no longer uses Supabase and requested removal. Deleted the unused Maven project (`pom.xml`) and complete legacy `src/main/**` Spring Boot implementation; the active .NET solution, Docker files, migrations, and tests do not reference that module.
- Removed the historical Supabase password value from current documentation and from the Gitleaks allowlist. Current docs use `[REDACTED]`; Gitleaks must fail on the historical occurrence until BE-099 is completed instead of silently accepting it.
- Updated UA-001 to require deletion/decommission of the old Supabase project, or credential revocation if the project is retained. Provider-side revocation has not been independently verified.
- BE-099 remains **BLOCKED** because the credential still exists in Git history. Rewriting refs and force-pushing require explicit operator authorization for the exact scope after provider revocation is confirmed.

## 2026-09-29 — Local-only k6 execution guard

- The k6 service remains isolated behind its Compose `k6` profile, so normal team startup cannot launch it.
- Every load script now fails before its first HTTP request unless `LOAD_TEST_CONFIRM=FITMATE_LOCAL_ONLY` is supplied explicitly. Targets are restricted to `localhost`, `127.0.0.1`, `api`, or `api-perf`; staging and production hosts are rejected even with confirmation.
- `smoke.js` now creates one disposable account in `setup()` per complete run instead of registering a new account in every VU iteration. Existing `smoke-*` rows are not deleted automatically.
- Documentation and `compose.loadtest.yml` now show the explicit opt-in. Runtime k6 execution was left to the operator.


# Email verification OTP completion — 2026-10-02

- Reopened the previously incomplete BE-802 email-verification slice: registration now returns `verification_required` instead of issuing a session, unconfirmed accounts cannot log in, and `POST /api/v1/auth/email/verify` plus `/email/resend` implement a rate-limited six-digit email code flow.
- Verification codes are generated by the ASP.NET Identity email token provider and encrypted with Data Protection before entering the durable outbox. Successful verification sets `EmailConfirmed`; replay is rejected because confirmed accounts no longer enter verification.
- Registered `EmailOptions`, MailKit SMTP transport, the email template/token services, and all five email outbox handlers in the API composition root. Fixed forgot-password enqueue so its token is protected and the outbox row is actually saved.
- Replaced obsolete `SMTP_*` example variables with canonical `Email__*` keys. Local MailHog uses `None`; production validation only accepts `StartTls` or `SslOnConnect` and requires complete credentials when either credential is present.
- Contract SHA-256 after the coordinated OTP additions: `83CF757A4286258E1A026387B0A9FCD2A45513F17261DD3D190BC10D52EC79C8`.
- Verification executed: API build PASS with 0 warnings/errors; all unit tests PASS 879/879; contract tests PASS 99/99; filtered email integration tests PASS 4/4.
- External gate remains: operator must verify a sender/domain and provide a Brevo SMTP key, then run a real-inbox register/resend/verify/forgot-password smoke test. No secret was created or committed.
- Recovery fix: login for an unconfirmed account now returns stable code `FITMATE.AUTH.EMAIL_NOT_VERIFIED`, allowing clients to resume verification instead of leaving the email stranded. API build rechecked PASS after this change.
- Security follow-up: password verification now occurs before exposing the unconfirmed-email state. Unknown emails and wrong passwords receive the same generic 401; OTP recovery is available only after the correct password proves account ownership, preventing enumeration and resend spam.
# Nutrition date filter runtime fix — 2026-10-02

- `GET /api/v1/me/nutrition/meals` now binds OpenAPI `date` query parameters as `DateOnly` and converts the selected local calendar-day bounds to explicit UTC `DateTime` values before querying PostgreSQL.
- The upper database bound is exclusive at the start of the following day, so `from=YYYY-MM-DD&to=YYYY-MM-DD` returns the entire selected day instead of passing an unspecified-kind `DateTime` to Npgsql.
- The nutrition integration roundtrip now exercises the same-date filter used by the Android diary.
# Booking lifecycle update — 2026-10-03

- User approved practical booking flow: published PT slots are pre-accepted; verified payOS settlement automatically confirms the booking.
- Hold TTL is 10 minutes. A member may self-cancel only when the slot starts at least 24 hours later; PT cancellation remains possible and creates the existing full-refund request.
- PT completion now enters `AwaitingMemberConfirmation`. Member can confirm or dispute; no dispute for 24 hours is auto-confirmed by the booking sweep. Financial recognition is created only after final `Completed`.
- Added migration `20261003010000_PracticalBookingLifecycle`, OpenAPI endpoints `confirm-completion` and `dispute`, plus matching Mobile/Admin states.
- Verification in this session: backend build passed; unit tests 884/884 passed after contract updates. Mobile booking/type contract changes type-check; full Mobile suite still contains unrelated pre-existing failures recorded in the session output. Admin Web typecheck and 122/122 tests pass.
# Session update — 2026-10-05 (community post search)

- `GET /api/v1/social/posts` accepts optional `query` up to 100 characters without changing the existing default feed behavior.
- Plain queries search visible post content, author display name and tags. Queries beginning with `#` search tags only after removing the leading hash.
- Search remains behind the existing visibility, friendship and block filters and keeps cursor ordering/pagination.
- OpenAPI documents the additive query parameter. Social unit tests pass 38/38 and the targeted PostgreSQL integration test verifies content, author and hashtag translation.

# Session update — 2026-10-05 (user discovery and search)

- Fixed `GET /api/v1/users/search` PostgreSQL execution by removing the query against the unmapped `ApplicationUser.IsDeleted` property; the global soft-delete filter remains authoritative.
- Search excludes the caller and accounts blocked in either direction. Added authenticated `GET /api/v1/users/suggestions`, excluding the caller, Admin accounts, existing friends, pending friend requests and blocked accounts.
- Verification passed: `UserProfileServiceTests` 8/8, `RouteOpenApiParityTests` 11/11, and the targeted PostgreSQL user-search/suggestion integration test 1/1.

# Session update — 2026-10-05 (friend notification copy)

- Friend-request and friend-request-accepted in-app notification titles/summaries are now written in Vietnamese for the Android user experience.
- Existing dispatcher behavior remains unchanged: the in-app notification is persisted even when no native push provider/device token is configured.
- Verification passed: `FriendServiceTests` 18/18.

# Session update — 2026-10-05 (authenticated rate-limit partition)

- General API requests now authenticate before rate limiting and use the authenticated user ID as the partition key; anonymous/auth traffic remains partitioned by IP.
- This prevents two signed-in phones on the same Wi-Fi from consuming one shared 60-request-per-minute budget while preserving an IP limit for unauthenticated traffic.
- API project build passed with 0 warnings and 0 errors.

# Session update — 2026-10-05 (friend chat media and reactions)

- Friend messages now accept text, up to five private uploaded images, or both; storage ownership is enforced from the authenticated sender and responses contain temporary signed URLs.
- Participants can toggle one of five supported reactions per message. Repeating the same reaction removes it; choosing another replaces the previous reaction.
- Added separate image and reaction migrations plus OpenAPI/runtime contract coverage. Backend build passed, chat/contract tests passed 44/44, and route/OpenAPI parity passed 11/11.

# Session update — 2026-10-05 (authenticated realtime delivery)

- Added the authenticated `/hubs/realtime` SignalR hub with explicit JWT query-token handling and a `sub`-aware user ID provider.
- Chat message/reaction changes and friend request lifecycle changes now notify only the affected authenticated users; notification badges receive their own realtime invalidation event.
- Realtime delivery is non-authoritative and cannot roll back an already committed business mutation. REST remains the source of truth and clients retain a fifteen-second foreground-only reconciliation poll.
- General rate-limit rejections now include `Retry-After` when the limiter exposes it.
# Weekly nutrition plan completion — 2026-10-06

- Replaced the legacy four-string schedule as the mobile weekly-plan source with additive owner-only APIs under `/api/v1/me/nutrition/weekly-plan`; the legacy `/schedule` contract remains for compatibility.
- A plan item is scoped to a concrete Monday `week_start`, day `0..6`, meal slot, serving, calories/macros and notes. Editing the current week therefore cannot rewrite prior weeks.
- `POST /weekly-plan/{id}/consume` creates a confirmed real `MealLog`; repeated or concurrent requests for the same plan item/date resolve to the same durable consumption through a unique database constraint.
- Deleting a planned item is soft-delete only and never removes historical meal logs.
- Added migration `CompleteWeeklyMealPlan`, applied it to the local Docker PostgreSQL database, rebuilt/recreated the local API container, and updated OpenAPI plus unit/integration test sources.
- Verification after restoring the damaged local test package cache: `dotnet build Fitmate.slnx --no-restore` PASS (0 warnings/errors); focused nutrition/contract/route-parity unit tests PASS (30/30); `NutritionAndAiContractTests` integration tests PASS (3/3) against PostgreSQL. Mobile runtime verification remains operator-owned per the mobile repository rule.
# Full AI ingredient contract — 2026-10-06

- Nutrition scan no longer concatenates the first three model ingredients into `name` or emits `+ N nguyên liệu`; the public response now exposes every unique model ingredient in `ingredients` (max 20) and keeps one whole-meal nutrition summary because the current model only provides whole-meal macros.
- Updated OpenAPI and adapter/contract tests. Focused tests PASS 11/11. Local Docker API was rebuilt/recreated so the new response contract is active.
# Nutrition targets — 2026-10-06

- Added nullable per-user daily targets for calories, protein, carbohydrates and fat to `UserPreference`, including EF migration `AddNutritionTargets`.
- `GET/PUT /api/v1/users/me/preferences` now reads and updates the four targets with server validation.
- Dashboard nutrition projection now returns the stored targets instead of always returning a null calorie target; macro targets were added additively.
- OpenAPI and DTO serialization tests were updated with the additive fields.
- Targets remain unset by default; the clients must show an explicit setup state rather than fabricated recommendations.

# Dashboard actionable projections — 2026-10-06

- Dashboard now returns `today_meals` from the same meal logs used for nutrition totals.
- Dashboard now returns the user's matching weekday workout plan as `today_workout` with an explicit `resource_type` for safe client routing.
- Existing upcoming appointment projection remains the source for the home appointment card and contextual quick action.
- No hydration values were invented; clients should omit hydration UI until a real persistence contract exists.
- OpenAPI and focused dashboard/serialization tests were updated.

# Workout execution and goals completion — 2026-10-06

- Added an authenticated, paginated `/api/v1/exercises` catalog with 18 Vietnamese-curated exercises sourced from the Unlicensed `yuhonas/free-exercise-db`; source and license metadata remain in every projection.
- Workout-session logging now hides foreign plans, rejects exercises outside the selected owned plan, persists every set result, and updates the exercise aggregate's completed-set count from those results.
- Added owner-only soft deletion for `/api/v1/me/weight/{id}`.
- Added nullable `target_weight_kg` and `monthly_workout_target` to user preferences with server validation and migration `AddWorkoutGoals`; unset values remain an explicit setup state.
- Updated OpenAPI for the catalog, weight deletion, workout goals, and image exercise media.
- Verification in this session: API build PASS (0 warnings/errors); full unit/contract suite PASS 924/924. Database migrations were scaffolded but not applied to an operator database in this session.
- Mobile implementation is `IMPLEMENTED_UNVERIFIED` under its manual execution policy: the operator still needs to run migration, API/device tests, and verify create-plan → active session recovery → summary/history → weight/goals on Android.

# Exercise catalog expansion and R2 media — 2026-10-06

- Expanded the curated exercise catalog from 18 to 30 active exercises while preserving the upstream `yuhonas/free-exercise-db` source and Unlicense attribution in API projections.
- Copied both reference images for every exercise to the configured Cloudflare R2 bucket (`60/60` objects). Database seed data now stores stable `storage://exercise-catalog/...` keys rather than depending on GitHub raw URLs.
- Catalog responses resolve private object keys to temporary signed R2 URLs; bucket public access is not required.
- Added the repeatable `import-exercise-media` CLI for controlled media import. It validates downloads, limits individual image size, uploads only below the exercise-catalog prefix, and does not print credentials.
- Applied migrations `InsertExpandedExerciseCatalog` and `NormalizeExerciseCatalogMediaKeys` to local Docker PostgreSQL. Local verification found 30 active catalog entries and 30 entries with both media keys normalized.
- Verification: migration bundle build PASS (0 warnings/errors); full unit/contract suite PASS (924/924); local API image rebuilt and container recreated successfully. Runtime reached `Application started` on port 5116 (this API does not expose `/health`, so that probe correctly returns 404).

# Workout plan persistence ordering fix — 2026-10-07

- Fixed workout-plan creation failing with PostgreSQL foreign-key error because EF Core did not know the relationship between `Exercise.WorkoutPlanId` and `WorkoutPlan` and could insert exercises before their parent plan.
- Added the missing EF relationship with restricted delete behavior. This lets EF order both inserts correctly inside the existing single `SaveChangesAsync` transaction; plan deletion remains the existing soft-delete flow.
- Added and applied migration `FixWorkoutPlanExerciseRelationship` to align the EF model snapshot and database constraint. API build passed with 0 warnings/errors, unit/contract tests passed 924/924, and the local Docker API reached `Application started` on port 5116.

# Workout session persistence follow-up — 2026-10-07

- Mobile log-session request was missing the mandatory `Idempotency-Key` header; the mobile adapter now sends a stable per-session UUID and preserves the exact payload for retry.
- Explicitly configured the existing workout-session → session-exercise relationship in EF so the parent insert is ordered before child inserts. The FK already exists in `WorkoutSnapshot`; updated the model snapshot without introducing a duplicate database migration.
- This follow-up has not been rebuilt or device-tested in this session. Confirm API save and summary/history after rebuilding the API image and reloading the app.

# PT online/package payment design note — 2026-10-08

- Recorded the owner's successful Gmail Meet API Explorer `spaces.create` (HTTP 200) and second-account join test, plus the agreed one-time PT Google connection and automatic per-session link design in `pt-online-package-payment-note.md`.
- Documented the proposed one-payment 4/8-session package design and the gap from existing single-booking payOS and 24-hour cancellation policy. This is documentation only; no PT/Meet/package runtime implementation or payment verification was performed here.
- Owner subsequently chose manual bank transfers to PT for the planned small real-user launch (50–100 users); payOS Payout API must stay disabled. The design note records the review/receipt/reconciliation and eventual shutdown obligations. No runtime payment action was taken.

# PT policy implementation slice — 2026-10-08

- Owner clarified that approved manual member refunds must be transferred within 48 consecutive hours, including weekends/holidays. The 48-hour transfer deadline is documented but NOT enforced by the current refund state machine; there is no distinct refund approval timestamp yet.
- Owner chose automatic SMS OTP for PT phone verification but has not chosen a vendor. Added `ISmsOtpSender` and a fail-closed unconfigured adapter; no send/verify endpoints or provider credentials exist yet, and `PhoneNumberConfirmed` is not set by this slice.
- Changed the existing single-booking member self-cancellation policy from 24 to 48 hours and member-initiated reschedule old-slot cutoff to 48 hours. PT-initiated reschedule retains its existing 2-hour minimum. Synchronized system capabilities, app copy/guards, pre-checkout short-notice warning, domain/OpenAPI text and tests. This does not implement package credits, partial refunds, PT penalties, Google OAuth/Meet links or package checkout.
- Verification: full backend unit suite PASS 926/926; mobile type-check PASS; targeted booking mobile tests PASS 13/13. No DB migration, device run, payOS transaction, or real SMS was performed. Existing unrelated dirty workout changes and migration files were preserved. No commit or push.
- Next: design and implement a durable SMS challenge/outbox with attempt limits and real provider; distinct approved-refund/transfer states and 48-hour due queue; atomic package holds and one-payment allocation; verified webhook handling; PT Google OAuth/Meet; admin/member/PT flows and end-to-end tests. Do not consider PT package launch-ready yet.

# PT onboarding and manual refund follow-up — 2026-10-08

- Added PT-only phone-verification start/confirm endpoints, an Identity-backed durable one-active OTP challenge, five-minute expiry, 60-second resend cooldown, five failed attempts, protected SMS outbox payload, and a unique index for confirmed phone numbers. Mobile has a verification screen linked from the PT certificate screen. Admin cannot approve a PT certificate without a verified Identity phone. The SMS adapter remains fail-closed because no provider has been selected; no real SMS was sent.
- Added a separate Admin manual-refund approval operation with MFA and idempotency, stored approval timestamp/admin ID and a computed 48-consecutive-hour transfer deadline. Actual bank transfer recording remains the only event that marks the refund succeeded. Admin Web now separates approve and record-transfer buttons and highlights overdue approved refunds. No bank transfer was initiated.
- Updated OpenAPI and the PT design note. Backend unit/contract tests passed 934/934; Admin typecheck passed and focused manual-finance tests passed 6/6 after finding and fixing an approval-form bug; mobile typecheck passed. PostgreSQL integration tests cannot provision an isolated test database because Docker Desktop's engine is not running; no user data/volume was touched.
- These are partial vertical slices. Outstanding: selected SMS provider/credentials and delivery verification; Google OAuth/Meet automation; service purpose/mode/location contract; atomic 4/8-session checkout and per-session allocations; package-credit/cancel/stop workflow with 24-hour PT response; attendance dispute/verified PT penalty escalations; live provider approval/keys and isolated end-to-end UAT. Do not enable package sales or claim launch readiness.

# PT booking eligibility correction — 2026-10-08 follow-up

- Marketplace detail/list and the booking service lookup now require an approved PT profile, an approved certificate, an active email- and phone-verified account. Previously an approved certificate alone could expose a PT; any active service could be booked even when PT verification/account status had changed. API build passed with zero warnings/errors. Existing PT accounts without verified Identity phones will not be bookable until SMS verification succeeds; this is deliberate fail-closed behavior, not a backfill.
- The 4/8-session package cannot reuse the current one-payment/one-appointment finance code safely: payment amount, refund, and payout calculations assume the whole payment belongs to one appointment. An allocation table and callback/ledger/revenue changes must land together before package checkout can be enabled. No package payment endpoint was exposed in this follow-up.

# PT booking and package allocation continuation — 2026-10-08

- Booking hold lookup now also requires an exact PT-published open slot of sufficient duration. The application rejects self-booking, non-UTC start times and starts within 30 minutes. OpenAPI hold summary/status and TTL were corrected. Existing 48-hour cancellation rule remains.
- Added a tested pure-domain four/eight-session VND allocator: chronological slots receive deterministic remainder; cumulative half-up fee deltas total exactly 10% of actual payment. It is not persisted or connected to payOS/ledger; package checkout stays unavailable until all financial consumers use the allocation.
- Backend unit/contract suite passed 939/939. PostgreSQL isolation remains unavailable while Docker Desktop is stopped. Marketplace/booking eligibility queries need database integration coverage before release.

# PT service contract and isolated PostgreSQL verification — 2026-10-08

- Added service purpose, delivery mode, target audience, required equipment, deliverable and one fixed location for direct sessions. Create requires complete fields and a meaningful description; online services are saved inactive until Google Meet OAuth/link creation exists. Marketplace and booking hide/reject legacy incomplete services, unapproved PT profiles, unverified phone/email accounts and slots not published by the PT.
- Added `AddPtServiceDetails` migration (Up/Down add/drop only service columns). It was **not applied to the app database**. The mobile PT editor now captures these fields; trainer detail and booking cards show them, and the fabricated `Fitmate Partner Studio` venue was removed. Booking selection filters slots too short for the service and clears stale selection.
- The isolated PostgreSQL integration database ran in a disposable container, separate from the Compose app database/volume; the container was stopped after tests. Backend unit tests PASS 941/941, integration PASS 65/65 (including new PT bookability transition coverage), API build PASS. Four legacy integration assertions were updated for the existing paid→Confirmed transition, manual-refund approve-before-transfer step, and payout waiting period. Mobile typecheck PASS and focused PT/booking tests PASS 17/17.
- `dotnet ef migrations has-pending-model-changes` still reports drift, apparently from the pre-existing list-valued exercise seed diff emitted on every scaffold; inspect and resolve separately before release. The generated service migration's unrelated 30 exercise `UpdateData` blocks were removed, and the owner's workout files were not edited in this follow-up.
- Public trainer list/detail now uses the OpenAPI/mobile flat snake_case profile shape, the PT **user** ID for navigation/booking, real Identity full name/avatar, and the cheapest active complete direct service price instead of stale profile pricing. An isolated HTTP integration test verifies the actual list/detail payload and booking eligibility; full integration suite still passes 65/65.
- Remaining launch blockers: real SMS provider credentials/delivery; Google OAuth/Meet and compensation; atomic package holds, persistence and payOS/ledger/refund/payout allocations; package-stop/PT response workflow; attendance dispute and penalty escalation; payOS production credentials/operator approval and device UAT. No package checkout is exposed and no payment/transfer was initiated. No commit or push.

# PT SMS and online single-session continuation — 2026-10-09

- Chose SpeedSMS for the owner's small Vietnam pilot. Added an HTTPS sender adapter with server-only Basic credentials, normalized E.164 recipient, provider response handling, `.env.example` fields and 4 focused tests. This is not live delivery evidence; missing token/provider leaves PT phone verification fail-closed.
- Added PT Google OAuth start/status/disconnect and backend callback, protected refresh-token storage in the existing persistent DataProtection key ring, automatic appointment-specific Google Meet space creation, and a paid-participant-only link endpoint. Online services can be published/discovered/booked only with complete details, verified PT and a current connection. Online hold creation releases the slot/appointment on Meet failure before checkout; success notification waits until a link exists. No Google credentials were supplied and no live OAuth/Meet/payOS transaction was run.
- Mobile PT services screen connects/disconnects Google and refreshes status; booking selection shows online services, and paid booking detail offers the private Meet link. Updated OpenAPI and FE contract pin. Added unit tests for online hold success/failure cleanup and PostgreSQL integration for online discovery requiring a connection.
- Verification: API build PASS, backend unit 951/951, isolated PostgreSQL integration 66/66, mobile type-check PASS, mobile 203/203 tests PASS. The new integration test initially collided with a pre-existing test phone number in the shared test DB; its test fixture now uses a distinct generated number and the complete suite passes. No operator app DB migration, device/provider verification, commit or push in this continuation.
- Still gated: package 4/8 atomic holds + one payOS order + persisted allocation across webhook/refunds/ledger/payout; package-stop and 24h PT response; verified attendance disputes and penalty escalation; live SpeedSMS and Google OAuth/Meet testing; payOS/operator approval, bank reconciliation, device UAT and release review. Do not describe the entire PT flow as complete or enable package sales.

# PT package persistence foundation — 2026-10-10

- Added a private 4/8-session package aggregate and per-session immutable VND allocation records. A hold validates UTC slot bounds, unique appointments, positive price, exact 10% cumulative half-up fee allocation, and the agreed four-week initial scheduling/two-week make-up deadlines. Payment attachment and activation require matching identity and full quoted amount.
- Added the package/session EF mapping and `AddSessionPackageFoundation` migration with unique appointment and package-sequence constraints. Removed unrelated exercise-catalog `UpdateData` noise from the scaffolded migration. Verified migration Up and Down on the separate `fitmate-api-check-postgres-1` test PostgreSQL database `fitmate_integration_test_pkg_20261010`; the Compose app database and volumes were untouched.
- Verification: backend unit tests PASS 955/955, PostgreSQL integration tests PASS 66/66 against a newly provisioned isolated test database, idempotent migration script generation PASS. These integration tests use `EnsureCreated` for the current model; the migration chain was tested separately.
- No package purchase endpoint is exposed. The existing `Payment`, payOS webhook, refund, ledger and settlement logic still assume one payment belongs to one appointment; package checkout MUST remain disabled until every financial consumer is changed and tested together. Next: atomic multi-slot reservation, package-aware single payOS order/callback and allocations, package cancellation/stop and PT response, refund/settlement and attendance/penalty flows, then mobile/admin end-to-end UAT. This is not launch-ready.

# PT package atomic reservation continuation — 2026-10-10

- Linked each package-session allocation to its exact booking hold as well as appointment, with unique FK constraints. The additive `LinkSessionPackageHolds` migration fails closed if unexpected package rows already exist rather than assigning a fake hold ID; Up and Down were verified on the isolated `fitmate_integration_test_pkg_20261010` database, not the app database.
- Added an internal in-person package reservation service. It validates all 4/8 published slots, a stable service quote, four-week bound and within-request non-overlap, then uses the same PostgreSQL PT schedule lock as single-session booking. Holds, appointments, package allocations and outbox event commit in one transaction; a taken slot rolls back the whole request. It is registered for internal use but has **no public endpoint or checkout** yet.
- PostgreSQL integration verifies four-row persistence, no partial rows after a conflicting request, and exactly one success when two package requests race for the same four slots. Backend unit tests PASS 955/955 and full PostgreSQL integration PASS 67/67. No provider transaction or mobile/admin path was tested.
- Remaining before exposing package purchase: online package Meet preparation/compensation, one package-aware payOS order and verified callback, package expiry recovery, per-session refund/ledger/payout consumption, package-stop/PT response and dispute/penalty UI. Single-booking behavior remains unchanged.

# PT package checkout safety and unpaid expiry — 2026-10-10

- The existing single-appointment payment endpoint now rejects a hold linked to a package with `PACKAGE_CHECKOUT_REQUIRED` before creating any payment. Single-appointment cancel and reschedule requests similarly reject package children with `PACKAGE_ACTION_REQUIRED`; package-wide operations must be implemented separately rather than silently applying a full-booking refund to one allocation.
- An unpaid package expiration sweep releases all child holds and expires all held appointments under one PT schedule transaction, and emits one package expiry outbox event. The legacy single-appointment sweep excludes package children so it cannot leave a package half-expired. `PaymentPending` packages cannot be expired by this unpaid path; a verified late payment needs a separate reconciliation/refund path.
- Verification: unit tests PASS 956/956; PostgreSQL integration PASS 67/67, including package payment/cancel guards, concurrent hold conflict and whole-package expiration. No payOS package order, provider callback, package stop/refund, online package Meet, mobile/admin path or operator DB migration was performed. Checkout remains disabled.

# PT package payment subject and order preparation — 2026-10-10

- `Payment` now has exactly one subject: legacy `AppointmentId` or new `PackageId`. The `AddPackagePaymentSubject` migration adds an exclusive-subject CHECK, unique package-payment index and package FK; rollback fails closed if any package payment exists. Unrelated exercise seed updates were removed from the scaffolded migration. Up and Down passed on the isolated package-migration PostgreSQL database; app DB was not changed.
- Added an **internal-only** package checkout preparation service. Under the PT schedule lock it verifies owner, package status, all 4/8 appointment holds and the immutable total; creates one pending payOS payment and one durable payOS order; repeated calls return the same payment. Provider I/O occurs after the transaction. PostgreSQL integration uses a test-only provider to verify the one-payment/one-order invariant and retry behavior, not a live payOS call.
- Legacy single-appointment finance paths require an appointment subject and fail closed for package payments. **No package checkout route is exposed** because the payOS webhook, late-payment liability, per-session refund/ledger/settlement and mobile/admin flows are not package-aware yet. A package payment must not be created for a real customer until those paths are completed and verified.

# PT package verified callback and late-payment liability — 2026-10-10

- A signed payOS callback for an internal package order now obtains the same PT schedule lock as hold creation/expiry. It validates exact payment identity, VND amount, package allocations and all 4/8 child reservations before atomically marking the one payment paid, the package active, and every child booking confirmed with its persisted paid/fee/PT-share allocation. Duplicate callbacks make no second state change.
- If any hold has expired or been released while all child appointments are still unpaid holds, the callback marks the received payment paid, releases/expires every child reservation, leaves the package in `LatePaymentRefundPending`, and creates **one** pending manual refund for the entire collected amount. The refund uses the first package appointment only as an administrative anchor; no booking is reinstated. Admin approval starts the existing 48-hour transfer deadline. Manual transfer confirmation applies the full payment refund without negative booking-revenue ledger entries, since no session was completed/recognized. Unexpected child state fails closed for reconciliation instead of creating a partial package.
- Verification on isolated PostgreSQL: package callback duplicate/complete and late/refund tests PASS; full backend unit 957/957 and integration 69/69. No real payOS payment, transfer, operator migration or app database change was made. **Package checkout remains private/disabled**: active-package cancellation/refund allocation, package-stop/PT response, payout and attendance/dispute/penalty workflows, online-package Meet handling, and mobile/admin paths still need implementation and end-to-end UAT before any real sale.

# PT package finance read-side correction — 2026-10-10

- Admin booking totals and PT revenue history/overview now attribute only **succeeded refund records matching that exact paid appointment and payment**. The previous `Payment.AppointmentId` lookup always returned zero for package payments; using the payment-wide refunded amount would incorrectly charge every child session. A late full-package refund anchored to an expired unpaid appointment is excluded from per-session revenue.
- Finance reconciliation discrepancy DTO now permits a nullable appointment ID and exposes the package ID for a package payment. This avoids a 500 from `RequireAppointmentId()` on a genuine package discrepancy; existing appointment-payment discrepancies keep their appointment ID. Admin Web's current table displays payment/issue/amount and does not use the appointment ID, but its TypeScript interface must be synchronized when package finance UI is implemented.
- Isolated PostgreSQL tests verify exact one-session attribution, late-package exclusion, and package discrepancy identity. Backend unit PASS 957/957; full integration PASS 71/71; API build 0 errors/warnings. No operator database, real money, deploy or push. Active-package refund creation/confirmation and full package-stop rules still block public checkout.

# PT package per-session payout eligibility — 2026-10-10

- The previous manual PT settlement predicate required `Appointment.ActualPaidVnd == Payment.AmountVnd`, which made every 4/8-session package child ineligible because one payment covers all sessions. Under the payment row lock, the settlement store now proves that the requested child belongs to the package and that its paid/fee/PT-share snapshot exactly equals the immutable session allocation. Only then can the application calculate the PT balance from that session's own succeeded refunds, using cumulative half-up fee rounding on that session's allocation. A refund on another session never reduces this session's payout; an unverified package child fails closed.
- The existing completion, 24-hour waiting period, two-party confirmation, evidence, dispute, pending-refund and manual bank-transfer requirements remain intact. Global pending refunds on the same package currently hold all its PT payouts conservatively until resolved. No automatic payout or bank transfer was initiated.
- Full backend unit suite PASS 958/958 and isolated PostgreSQL integration PASS 72/72, including a package with a refund on only one of two completed children. Package checkout is still private/disabled. Next finance prerequisite: create/cap/confirm active-package refunds by exact session, and design package-stop snapshot so a 24-hour PT response does not accidentally move an otherwise refundable session inside the 48-hour cutoff. Sessions already inside 48 hours need an explicit non-refund/compensation representation; do not silently mark them Completed or credit PT without attendance evidence.

# PT package per-session manual refunds — 2026-10-10

- Admin's existing MFA-protected manual-refund request accepts an optional `appointmentId`; package payments require it. The store validates package Active, child membership and exact persisted allocation, caps succeeded plus pending refunds to both the payment and that child, and rejects partial refunds for an uncompleted session. An uncompleted confirmed child must start at least 48 hours later, is cancelled with a booking outbox event, and gets a pending refund intent; no money is marked returned before the bank transfer is recorded. A reviewed Completed child may be partially refunded without cancelling its completion.
- On manual transfer confirmation, a completed package child reverses the gross/fee/PT-share ledger by that child's cumulative half-up allocation, only if original revenue recognition exists. A cancelled child has no recognized ledger to reverse; the late-payment whole-package liability retains its separate no-recognition handling. Retry with the same bank reference is idempotent; excess per-session requests fail. The OpenAPI manual-refund body documents the optional `appointmentId` field.
- Focused isolated PostgreSQL tests cover the future-child, under-48h rejection, completed-child partial refund, cumulative cap and idempotent transfer paths. Public package checkout remains disabled. Full stop-request/PT-response flow, PT no-show incident refund path, concurrent package start-versus-admin cancellation serialization, and Mobile/Admin package UI are still pending before real sales.

# PT package stop finalization — 2026-10-10

- After PT acceptance or the 24-hour no-response approval, an internal one-minute sweep finalizes approved stop requests under the same payment row lock as refund and payout. It validates the immutable per-session quote, payment/global cap and exact child allocations, then cancels only the children that were Confirmed and at least 48 hours away **when the member requested the stop**. Each eligible child gets one pending system-requested manual refund; noneligible near-term or completed children are left untouched for normal completion or incident review. No automatic bank transfer or PT payment occurs.
- Finalization stores `FinalizedAtUtc`, marks the package Stopped, and emits audit/outbox events atomically. A repeat run does not create duplicate refunds. Admin's existing manual-refund confirmation now accepts Stopped packages so the approved 48-hour transfer SLA can be processed after the package closes. The additive `FinalizeSessionPackageStops` migration only adds the nullable timestamp; generated exercise-catalog seed updates were removed. Down fails closed when finalized history exists. The migration was **not applied to the app database**.
- Verification: backend unit PASS 962/962; isolated PostgreSQL integration PASS 81/81, including snapshot-only refunds, idempotent finalization and Admin refund transfer on a Stopped package. Remaining before public package sales: online package Meet setup/compensation, package booking API and Mobile/Admin flows, near-term attendance/dispute/no-show/PT penalty decisions, and live provider/UAT evidence. Do not expose package checkout yet.

# PT online package Meet preparation — 2026-10-10

- Added an **internal-only** online 4/8-session hold path. Under the PT schedule lock it validates one online service and all slots, persists the complete reservation as `OnlinePreparing`, then creates an appointment-specific Google Meet space for every child **outside** the DB transaction. Checkout still accepts only `Held`; a package becomes `Held` and emits `booking.package_held` only after all spaces return valid Meet URLs, the hold is unexpired, and every reservation remains active.
- A failed/invalid Meet response or expired hold expires the unpaid package and releases all child appointments/holds atomically; locally persisted Meet link records are removed. The expiration worker also handles abandoned `OnlinePreparing` packages after TTL, so process death cannot leave package slots held indefinitely. Already-created remote Google spaces can remain orphaned on Google after a failed attempt; they are not customer-visible and do not create a payment, but remote cleanup/monitoring should be reviewed before launch.
- Verification: backend unit PASS 963/963; isolated PostgreSQL integration PASS 83/83. Tests include four successful links, third-link failure and all-slot compensation, and a checkout attempt while Meet preparation is still in progress. No schema migration, app DB change, live Google API call, payOS order or public package route. Remaining: package API and Mobile/Admin journeys, provider UAT, near-term incident/penalty workflow and release approvals. Do not enable real package sales yet.

# PT package participant read API — 2026-10-10

- Added owner-scoped `GET /api/v1/me/session-packages`, PT-role-scoped `GET /api/v1/me/session-packages/assigned`, and participant-scoped `GET /api/v1/me/session-packages/{id}`. Read models expose the package total/payment state, each session's current slot/status and immutable paid allocation, and the current stop request without exposing private Meet links or provider credentials. Server filtering happens before pagination, with page size 1–100 and reconciliation conflicts for broken package/child/payment relationships. Other users receive 404 for detail; a Member cannot call the PT list.
- OpenAPI now describes these three read endpoints and their DTOs. HTTP integration covers 200, 400, 401, 403, 404 and participant isolation; full backend unit PASS 963/963 and isolated PostgreSQL integration PASS 83/83. No booking/payment mutation route was exposed, no app DB migration applied, and no live provider action taken. Remaining: safe public package hold/checkout decision after dispute/penalty business flow, Mobile/Admin UX, live provider UAT and operator release gates.

# PT package stop commands API — 2026-10-10

- Added authenticated member stop request and withdrawal, and assigned-PT acceptance endpoints under `/api/v1/me/session-packages/{id}/stop-requests`. Every financial mutation requires an `Idempotency-Key`; the middleware stores/replays results as financial operations. Owner/PT assignment is checked in Application before state changes, and the PT endpoint also requires the PT role. Return values contain the immutable refund quote and 24-hour response deadline. Finalization still runs asynchronously and only creates pending manual refunds; Admin records the actual bank transfer.
- Fixed idempotency audit operation names for long nested UUID routes: the storage field is limited to 128 characters, so the displayed name is shortened with a stable hash suffix while the full path remains in the storage-key identity. HTTP integration covers ownership, role, missing/invalid input, replay, payload conflict, accept, withdraw, and a closed withdrawal. No package checkout route was enabled; there is still no customer-facing pre-submission refund preview, Mobile/Admin package journey, live provider UAT, or near-term incident/penalty workflow. Do not sell packages yet.

# PT package stop refund preview — 2026-10-10

- Added owner-only `GET /api/v1/me/session-packages/{id}/stop-preview` with each child session's current status, paid allocation and 48-hour eligibility plus the indicative refund total. It rejects inactive/already-stopping packages, broken child/payment allocations, and eligible sessions already reserved for refund or paid to PT. The preview is intentionally read-only and can drift; POST submission rechecks everything under the payment lock and commits the authoritative snapshot. Mobile must show the indicative amount and possible change before confirmation, then display the POST response as final. No package checkout or real transfer was enabled. Remaining: Mobile/Admin UX, incident/penalty handling, live provider UAT and release approval.

# PT package stop quote confirmation — 2026-10-10

- The public stop-request POST now requires `expected_refund_quote_vnd` from the preview. Under the existing payment lock it recomputes the eligible allocation and returns `PACKAGE_STOP_QUOTE_CHANGED` (409) without creating a stop request if the amount differs. This prevents a member from confirming one VND amount and silently receiving a lower authoritative snapshot when the 48-hour cutoff moves or session status changes. The internal service preserves optional expected amount for existing system-only callers. Mobile must refresh the preview after 409 and require a new confirmation with a new idempotency key. No live sale or money movement was enabled.

# Admin PT package read-only operations — 2026-10-10

- Added Admin-only paged `GET /api/v1/admin/session-packages` and detail `/{id}`. Filters are limited to named package statuses, Member/PT UUID, page and pageSize (max 100), applied in PostgreSQL before pagination. Application layer verifies Admin role; output reuses the reconciled package/session/payment/stop projection without private Meet URLs or provider credentials. These endpoints make stop requests visible to an operator but do not authorize a refund, payout or booking mutation.
- Updated OpenAPI, authorization/feature matrices and HTTP integration for 200/400/401/403/404, filtering and detail. API build: 0 errors/warnings; unit tests: 963/963; isolated PostgreSQL integration: 85/85. The new Admin Web read-only panel still requires a real two-account browser/device UAT. No app DB migration, live provider call, payment, transfer or deploy was performed. Package checkout remains disabled until near-term incident/penalty flow, full Admin finance workflow and provider/operator gates are complete.

# PT package checkout and verified no-show penalties — 2026-10-11

- Public authenticated package hold and PayOS payment-request routes are now exposed under `/api/v1/me/session-packages`. A package reserves exactly 4 or 8 nonoverlapping PT slots atomically; online packages only become payable after all Google Meet spaces are ready. Payment requires explicit acknowledgement when any selected session is within 48 hours, and an existing pending checkout can be resumed without a second acknowledgement.
- Admin's verified `PtNoShow` incident decision now records strikes on the locked PT row in the same transaction as the pending manual refund. Two strikes cause a 7-day suspension; four cause a 30-day suspension and begin a one-year monitoring window; six within that window permanently ban and lock the account. A year without the third flag resets the counters on the next verified incident. New bookings, marketplace visibility, new services and availability are blocked during suspension/ban. Approval checks current phone numbers of permanently banned PTs.
- New additive `RecordPtNoShowPenalties` migration was discovered by EF and applied by isolated integration tests, **not to the application database**. No live PayOS payment or Google Meet call was performed. Backend unit tests passed 967/967 and isolated integration tests passed 91/91. Mobile typecheck passed; device checkout UAT, provider credentials/callback, current app DB backup+migration, phone ban identity hardening, and operator release approval still remain. Do not describe this as verified live payments.

# Local application DB migration — 2026-10-11

- At the owner's explicit request, migrated the running Compose PostgreSQL database `fitmate` from 37 to 49 recorded migrations, ending at `20261011030000_RecordPtNoShowPenalties`. A custom-format dump was saved outside the repository at `D:\Ky8\exe201\fitmate-local-backups\fitmate-before-pt-migration-20261011.dump` and `pg_restore -l` read its catalog successfully. No volume or container data was reset.
- Rebuilt the migration bundle from current source, ran the one-shot migration job successfully, verified the five new PT penalty columns, restarted the dev API, and received HTTP 200 from `/health/ready`. Public capabilities currently report `payments_enabled=false` because payOS credentials/configuration are absent; no real payment or provider webhook was exercised. The local data is migrated, but live payment/UAT gates remain open.

# AI Plus groundwork — 2026-10-11

- Product design is recorded in `ai-plus-subscription.md`: a separate monthly digital subscription through Google Play Billing, not PT/payOS or legacy VIP. Product owner has not created the Play Console app/product yet. Price and native billing cannot be live-tested; no purchase route or entitlement grant has been exposed.
- Added the additive `AddAiUsageReservations` migration and authenticated `GET /api/v1/me/ai-usage`. Chat and meal scan reserve daily usage atomically under a PostgreSQL advisory transaction lock, commit on successful AI result, release on failure, and report next midnight in Vietnam time. Free-tier defaults are proposed at 10 chat/2 scans per day; Plus defaults are 100/20 but **not active** without verified entitlement. `AiUsage__Enabled=false` keeps enforcement off while purchase verification/UI are unavailable. Do not enable this flag yet.
- API build and unit tests passed 967/967. Focused PostgreSQL integration tests passed 3/3, including concurrent reservations and authenticated status. The new migration ran in the isolated test database only; it has **not** been applied to the app database. Full integration suite initially exposed an unrelated package test that assumed no other test's Active package existed in the shared database; the assertion was scoped to its own Member/PT pair, then the full suite passed 94/94.
- Remaining for real upgrade: Play Console `vn.fitmate.app`, monthly subscription/base plan and price; authenticated Google Play Developer API verification, entitlement storage and token binding, acknowledge/reconcile/RTDN/refund handling; native Android Billing library/build and two-account purchase/restore/cancel/refund UAT. Never use PayOS or a client-reported purchase flag to grant AI Plus.

# AI Plus Google Play implementation — 2026-10-11

- Added authenticated `GET /api/v1/me/ai-subscription` and idempotent `POST /api/v1/me/ai-subscription/verify`. Backend exchanges a service-account JWT for Android Publisher access, calls `subscriptionsv2.get`, validates package/product/base plan, acknowledges an active purchase, hashes and DataProtection-encrypts the purchase token, and rejects binding one token or linked replacement token to another Fitmate account.
- Added `ai_subscriptions` migration `20261011050000_AddGooglePlayAiSubscriptions`. Plus quota is granted only for Google states Active, InGracePeriod, or Canceled before verified expiry; pending/on-hold/paused/expired/revoked do not grant it. A bounded worker and account reads requery Google; an unrecognized stale token is revoked. Provider/config errors fail closed instead of trusting client purchase state.
- Mobile added `expo-iap` 6.0.0 and its Expo config plugin. The account AI-plan screen fetches localized Play price/offers, buys only the configured monthly base plan, sends purchase tokens to BE, restores owned purchases, and opens Play subscription management. Expo Go/direct sideload shows an honest unavailable state; native internal-testing build is required. Backend credentials never enter public mobile env.
- Verification evidence: API build passed with 0 warnings/errors; unit suite 967/967; full isolated PostgreSQL integration 96/96 including entitlement binding/state tests; Mobile typecheck passed, targeted lint passed, full tests 208/208, Expo config introspection succeeded. Full lint has 0 errors and 146 pre-existing warnings. `npm audit` reports transitive Expo/Metro advisories including one critical `shell-quote`; automatic force remediation proposes breaking Expo downgrades/upgrades and was not applied. This is a release gate, not evidence of an `expo-iap` vulnerability.
- Local app DB was backed up to `D:\Ky8\exe201\fitmate-local-backups\fitmate-before-ai-plus-20261011.dump` (custom archive catalog verified), then migrations `20261011040000_AddAiUsageReservations` and `20261011050000_AddGooglePlayAiSubscriptions` were applied. Both tables/history entries were verified, API image was rebuilt, and `/health/ready` returned HTTP 200. No volume was deleted.
- Still external/unverified: Play Console app `vn.fitmate.app`, product `fitmate_ai_plus`, `monthly` price/base plan, Android Publisher service-account permission, signed AAB installed from internal testing, license tester purchase/restore/cancel/renew/refund UAT, and optional authenticated RTDN/Pub/Sub for near-real-time updates. Do not enable `GooglePlayBilling__Enabled` or `AiUsage__Enabled` until those exact values and UAT are complete.
- Final verifier hardening maps Google OAuth/Publisher connection failures and provider timeouts to typed HTTP 503 without granting Plus, while preserving caller cancellation. Focused billing integration tests passed 2/2 after the change.
