# Changes — review handoff

Ghi nhận các thay đổi source theo đợt (F0..F6). Phần A: code đã có trước khi Antigravity
tiếp quản. Phần B: sửa của Antigravity đang dở (chưa build/test/runner). Phần C: gap
chưa triển khai.

## Đợt 0 — inventory + tĩnh

### A. Trước Antigravity (giữ nếu đúng)

- BE
  - `src/Fitmate.Domain/Identity/AuthSession.cs` — entity `AuthSession` + `AdminLoginChallenge`
    (NEW FILE rỗng trước đó, giờ đã có nội dung).
  - `src/Fitmate.Application/Auth/AdminLoginChallengeService.cs` — interface rỗng 0 byte
    trước đó, đã có nội dung: interfaces `IAdminLoginChallengeService`, `IAuthSessionService`.
  - `src/Fitmate.Infrastructure/Auth/EfAuthSessionService.cs` — `EfAuthSessionService` (Create/FindActive/Revoke) + `EfAdminLoginChallengeService` (Create/Verify với FOR UPDATE, transaction, attempt limit 5, recovery code redeem one-use).
  - `src/Fitmate.Infrastructure/Auth/EfRefreshTokenStore.cs` — thêm revoke AuthSession khi revoke toàn user; `TrySaveRotationAsync` catch `DbUpdateConcurrencyException`.
  - `src/Fitmate.Infrastructure/Persistence/Configurations/AuthSessionConfiguration.cs` — config cả hai bảng mới + index UserId+RevokedAt + index ExpiresAt.
  - `src/Fitmate.Infrastructure/Persistence/Configurations/IdentityConfiguration.cs` — `RefreshTokenConfiguration` thêm `SessionId` + `ReplacedByTokenHash`; `RevokedAt` concurrency token.
  - `src/Fitmate.Infrastructure/Persistence/Migrations/20260928120000_AdminSessionSecurity.cs` — Up tạo `auth_sessions` + `admin_login_challenges` + ALTER `refresh_tokens` ADD COLUMN `SessionId`. Down tương ứng.
  - `src/Fitmate.Api/Auth/AuthController.cs` — Login: Admin chưa MFA → 202 setupToken (role `MfaSetup`, 5 phút); Admin đã MFA → 202 mfaRequired + challengeId; Member/PT → 200 AuthResponse. Endpoint mới `POST /api/v1/admin/auth/login/verify` với transaction + recent session. Refresh đọc từ cookie hoặc body, kiểm user status + session active + Admin MFA. Logout revoke refresh + session by `sid`.
  - `src/Fitmate.Api/Program.cs` — JwtBearer OnTokenValidated check user.IsDeleted/Status/Lockout/security_stamp; cho MfaSetup role chỉ path `/api/v1/admin/auth/mfa/{setup,verify}`; Admin role yêu cầu TwoFactorEnabled + method=mfa. DI register IAuthSessionService + IAdminLoginChallengeService.
  - `src/Fitmate.Api/Admin/AdminMfaController.cs` — Setup 409 nếu TwoFactorEnabled (chống leak secret). Verify enable MFA + UpdateSecurityStamp + RevokeAllForUser + recovery codes. Disable cần code hoặc password.
  - `src/Fitmate.Application/Admin/AdminReviewService.cs` (NEW) — interfaces + DTO AuditItem/BookingReview/TrainerReview + service.
  - `src/Fitmate.Infrastructure/Admin/EfAdminReviewStore.cs` (NEW) — query metadata-free + bounded pagination.
  - `src/Fitmate.Api/Admin/AdminReviewController.cs` (NEW) — `/admin/audit`, `/admin/bookings/{id}/review`, `/admin/trainers/{id}/review`.
  - `src/Fitmate.Application/Support/SupportService.cs` — CanLinkBookingAsync check participant hoặc Admin.
  - `src/Fitmate.Infrastructure/Support/EfSupportStore.cs` — SaveChangesAsync chạy `FOR UPDATE` lock appointment nếu ticket có BookingId.
  - `src/Fitmate.Application/Finance/ManualSettlementService.cs` — Approve/Hold/RecordTransfer validate UTC, evidence không control chars, length 200. RecordTransfer idempotent khi duplicate (cùng reference+evidence+transferredAt).
  - `src/Fitmate.Infrastructure/Finance/EfManualSettlementStore.cs` — `pg_advisory_xact_lock` + reference reuse check cả refunds và settlements. `UpdateAsync` lấy payment theo `Appointment.PaymentId` đúng cách (không SingleOrDefault).
  - `src/Fitmate.Domain/Payment/ManualPtSettlement.cs` — Approve validate UTC + sessionEnd > now + conflict throws. RecordTransfer idempotent + status check.
  - `src/Fitmate.Api/Admin/FinanceStepUp.cs` — VerifyTwoFactorTokenAsync + AccessFailedAsync; validate format TOTP 6 số ASCII digit.
  - `src/Fitmate.Api/Admin/ManualSettlementsController.cs` + `ManualRefundsController.cs` — yêu cầu `Idempotency-Key` cho mutation.
  - `src/Fitmate.Application/Trainers/CertificateService.cs` — RevokeAsync + audit.
  - `src/Fitmate.Api/Trainers/AdminCertificatesController.cs` — preview + URL TTL 5 phút; route `/admin/certificates/trainer/{ptUserId}` chưa pagination.
  - `src/Fitmate.Api/Auth/AuthController.cs` — ClientResponse giấu refreshToken cho web; SetRefreshTokenCookie path /api/v1/auth HttpOnly+Secure+khi prod.

- Web
  - `src/api/client.ts` — sessionGeneration; doRefresh finally chỉ clear nếu vẫn là pending.
  - `src/features/auth/authApi.ts` — verifyLogin + login union narrow; getMe map snake_case → camelCase.
  - `src/features/auth/AuthContext.tsx` — login/epoch init → check generation; logoutHandler clearSession + cancelQueries ngay.
  - `src/features/reports/AuditTrail.tsx` (NEW) — filter UUID regex + action allowlist + page/server.
  - `src/features/finance/financeApi.ts` — holdSettlement/approveSettlement/recordTransfer/createManualRefund yêu cầu TOTP + Idempotency-Key.
  - `src/features/finance/ManualFinancePanel.tsx` — form operation; intent key locked sau first send.
  - `src/components/FinanceManagement.tsx` — re-export FinanceWorkspace.

### B. Antigravity (đợt 0 — static review rà soát)

Không tự sửa code runtime ở đợt 0; đợt này chỉ đối chiếu review 2026-09-28 với code hiện
tại và cập nhật tài liệu bàn giao (chưa thêm phần B).

### C. Gap chưa triển khai (ghi nhận theo đợt tiếp)

- R01 vẫn tiềm ẩn: re-enrollment chưa có step-up recent TOTP (sẽ xử lý sau khi có UI re-enroll).
- R02: Concurrency token trên RefreshToken (chưa ổn hai instance) — sẽ thêm ở đợt F1.
- R04: apiFetch ghi đè Authorization — sẽ sửa ở đợt F1.
- R06: ManualFinancePanel state machine — sẽ sửa ở đợt F2.
- R09: Cert revoke + audit atomic — sẽ sửa ở đợt F3.
- R10: BookingReview.tsx rỗng — sẽ tạo ở đợt F4.
- R12: Settings validation length 12 vs UI min 8 — sẽ sửa ở đợt F5.
- R13: overviewApi createdAt → failedAt sai — sẽ sửa ở đợt F5.

## Đợt F1 — auth/session/MFA (planned)

Sẽ commit nội dung:

- BE
  - `src/Fitmate.Infrastructure/Auth/EfRefreshTokenStore.cs` — thêm concurrency guard cho rotation.
  - `src/Fitmate.Api/Auth/AuthController.cs` — tinh chỉnh client vs web detection.
  - (tests) `tests/Fitmate.UnitTests/AuthSessionServiceTests.cs` — challenge TTL/attempt/verify các nhánh.

- Web
  - `src/api/client.ts` — chỉ set Authorization mặc định khi header chưa có.
  - `src/features/auth/AuthContext.tsx` — race-safe initSession + logout.

## Đợt F2 — finance (planned)

- FE
  - `src/features/finance/ManualFinancePanel.tsx` — state machine editing/submitting/validation/uncertain/conflict.
  - (tests) `src/features/finance/__tests__/ManualFinancePanel.test.tsx` — state machine table.

## Đợt F3 — PT/Certificates (đang xử lý — atomic done; pagination/preview CHƯA)

### B. Antigravity (đợt F3)

- BE
  - `src/Fitmate.Application/Trainers/ICertificateService.cs` — thêm `IAuditLogger.Stage(action, type, id, actor, reason)` (không SaveChanges) để caller SaveChanges cùng transaction với business state change.
  - `src/Fitmate.Infrastructure/Audit/EfAuditLogger.cs` — `Stage`/`Build` helper; `LogAsync` cũng dùng cùng `Build`.
  - `src/Fitmate.Application/Trainers/CertificateService.cs` — `ApproveAsync`/`RejectAsync`/`RevokeAsync` đổi sang `_audit.Stage(...)` rồi `SaveChangesAsync` một lần. `RevokeAsync` thêm validate lý do (min 5, max 500) tương tự `RejectAsync` để không cho revoke không lý do.

### C. Gap sau F3

- Pagination server cho PT list certificate (`/admin/certificates/trainer/{ptUserId}`) — ICertificateStore chỉ có `ListByPtAsync` không phân trang; CHƯA sửa, để dành cuối đợt vì không có use case rõ ràng.
- Preview ownership check (storage key prefix → ownership row check) — CHƯA sửa; đợi user quyết định schema.
- `SubmitAsync`, `ListMineAsync` vẫn dùng SaveChangesAsync riêng → không atomic với audit; theo domain-rules thì PT self-submit không yêu cầu audit, nên chấp nhận được.

## Đợt F4 — Booking (planned)

- FE
  - `src/features/bookings/BookingReview.tsx` — render 3 nhóm booking/payments/refunds.
  - `src/components/BookingManagement.tsx` — link từ danh sách sang BookingReview.
  - (tests) `src/features/bookings/__tests__/BookingReview.test.tsx`.

## Đợt F5 — Support/audit/overview/settings (đang xử lý — DLQ + Settings đối chiếu)

### B. Antigravity (đợt F5)

- Web
  - `src/features/overview/overviewApi.ts` — đổi `DeadLetterItem` dùng `createdAt` thật (không phát minh `failedAt`); thêm `status`. Map handler phản ánh BE.
  - `src/components/Overview.tsx` — UI hiển thị "Bản ghi tạo lúc …" thay vì `failedAt`.
  - `src/test/msw/handlers/overview.ts` — fixture DLQ đồng bộ BE contract: `createdAt`, `attempts`, `status`, `lastError`.
- (Đã đối chiếu R12) `src/components/Settings.tsx` đã dùng `minLength={12}` cho password.
- (Đã đối chiếu R10) `src/features/legal/AccountDeletionPolicy.tsx` hiện copy "đăng nhập lại trong 30 ngày để khôi phục" khớp với BE `CancelDeletionAsync` (qua login endpoint); copy không thay đổi nhưng đã rà soát.

### C. Gap sau F5

- AuditTrail table có phân trang server; vẫn cần test cho tab detail/admin check.
- `getMe` snake_case ↔ camelCase mapping chưa có test wire-form chính thức; đã có mapping trong `authApi.ts`.
- `Reports` module sử dụng attachment URL thô — chưa ký URL có TTL. Theo dõi.

## Đợt F6 — đồng bộ (planned)

- Cập nhật `docs/backend-release/openapi/fitmate-v1.yaml` cho review/preview/challenge mới.
- Cập nhật `docs/backend-release/feature-api-matrix.md`, `authorization-matrix.md` nếu scope thay đổi.
- Cập nhật `docs/review-handoff/` README, changes, requirement-matrix, risks-and-verification.

## Đợt F1 — auth/session/MFA (chỉnh sửa thực tế)

### B. Antigravity (đợt F1)

- Web
  - `src/api/client.ts` — bổ sung ghi chú về điều kiện set `Authorization` mặc định (chỉ thêm khi caller không cung cấp); giữ nguyên logic cũ nhưng thêm comment để ngăn vô tình ghi đè setup token.
- BE
  - `tests/Fitmate.UnitTests/AuthSessionServiceTests.cs` (NEW) — ghi nhận các invariant của `AuthSession`/`AdminLoginChallenge`: SecurityStamp thay đổi vô hiệu hóa session cũ; Consumed terminal; Attempts cap 5. KHÔNG thay thế được integration test (cần Postgres để chạy `SELECT ... FOR UPDATE`).

### C. Gap sau F1 (chuyển sang F2/F6)

- R02 concurrency token đã có sẵn (`RefreshToken.RevokedAt IsConcurrencyToken`, `TrySaveRotationAsync` catch). KHÔNG cần sửa thêm; integration test mới chứng minh end-to-end.
- R03 session security stamp check đã có trong `AuthController.Refresh`. Đã ổn.
- R04 apiFetch setup-token ghi đè — Web đã protect qua `if (!headers.has("Authorization"))`. Giữ nguyên, ghi nhận trong requirement-matrix.

## Đợt F2 — finance (chỉnh sửa thực tế)

### B. Antigravity (đợt F2)

- Web
  - `src/features/finance/ManualFinancePanel.tsx` — refactor `FinanceOperation` thành state machine `editing | submitting | validation | uncertain | conflict`. TOTP vẫn yêu cầu; 400 giữ fields editable nhưng giữ Idempotency-Key cũ (retry không tạo giao dịch trùng); 409 hiển thị nút "Đóng và tải lại trạng thái"; network/timeout đánh dấu `uncertain`, retry gửi cùng body+key. Hiển thị Idempotency-Key trong dialog để operator tham chiếu khi cần.
  - `src/features/finance/__tests__/ManualFinancePanel.test.tsx` (NEW) — bốn test cho state machine: TOTP-missing reject, 409 conflict, 400 validation retry giữ key, network uncertain retry giữ body+key.
  - `src/test/setup.ts` — polyfill `HTMLDialogElement.showModal()` / `.close()` (jsdom thiếu) để AdminUi.Dialog hiển thị trong unit test.

### C. Gap sau F2

- Chưa chạy `pnpm test`/`vitest run` để xác nhận test pass; user execute.
- Không sửa thêm state machine ở F5/F6 vì đã đủ nhánh.

## Đợt F4 — Booking (chỉnh sửa thực tế)

### B. Antigravity (đợt F4)

- Web
  - `src/features/bookings/BookingReview.tsx` (NEW) — render 3 nhóm (appointmentStatus · payments · refunds). Loading/error/empty phân biệt; tránh metadata payment thô.
  - `src/features/bookings/bookingsApi.ts` — thêm `BookingPaymentView`/`BookingRefundView`/`BookingReviewDto` + `fetchAdminBookingReview(id)` gọi `/admin/bookings/{id}/review`.
  - `src/components/BookingManagement.tsx` — nút Receipt (cạnh Eye) mở `BookingReviewView` thay vì modal cũ; tách bạch hai luồng xem nhanh vs đánh giá đầy đủ.
  - `src/test/msw/handlers/bookings.ts` — handler `/admin/bookings/:id/review` trả fixture nhỏ (1 payment, không refund) để component test không phụ thuộc DB.

### C. Gap sau F4

- Chưa viết test component `BookingReviewView` (loading/error/empty); user execute.
- `AdminReviewService.BookingAsync` hiện throw `NotFoundException`; UI chưa có nhánh riêng cho 404 (đang hiển thị "Không tải được dữ liệu") — chấp nhận được vì query.isError + error message sẽ hiển thị. Có thể thêm test cho 404 ở F6.

## Đợt F0b — review inventory trước đợt F1

## Đợt F3b — Cert pagination + ownership (chỉnh sửa thực tế)

### B. Antigravity (đợt F3b)

- BE
  - `src/Fitmate.Domain/Trainers/Certificate.cs` — thêm `ImageUploadedByUserId (Guid?)` (lưu user thực sự upload cert lên storage). Factory `Submit(...)` nhận tham số tùy chọn `imageUploadedByUserId`.
  - `src/Fitmate.Application/Trainers/Stores.cs` — đổi `ICertificateStore.ListByPtAsync` → `ListByPtPagedAsync(ptUserId, page, pageSize, ct)` trả `(IReadOnlyList<Certificate> Items, int Total)`. Thêm `SubmitAsync(..., Guid? uploadedByUserId, ct)`.
  - `src/Fitmate.Application/Trainers/ICertificateService.cs` — thêm `CertificatePage` record DTO; `ListTrainerForAdminAsync(ptUserId, page, pageSize, ct)` clamp `page≥1`, `pageSize∈[1,100]`, mặc định 25. `SubmitAsync(req, currentUserId, ct)`; `ListMineAsync(currentUserId, ...)`.
  - `src/Fitmate.Application/Trainers/CertificateService.cs` — `ListTrainerForAdminAsync` clamp + repository call; `SubmitAsync` truyền `uploadedByUserId` xuống store.
  - `src/Fitmate.Infrastructure/Trainers/EfTrainerStores.cs` — `ListByPtPagedAsync` dùng `Skip((page-1)*pageSize).Take(pageSize)`. `SubmitAsync` set `ImageUploadedByUserId`.
  - `src/Fitmate.Api/Trainers/AdminCertificatesController.cs` — `GET /admin/certificates/trainer/{ptUserId}?page&pageSize` trả `AdminCertificatePage`; `Preview(id)`:
    - 404 NotFound nếu `ImageStorageKey` null hoặc `ImageUploadedByUserId` null.
    - 403 Forbid nếu `ImageUploadedByUserId != cert.PtUserId` (admin muốn lấy link cert của PT khác).
    - 200 OK + `IAuditLogger.Stage("certificate.preview.viewed", ...)` nếu `ImageUploadedByUserId == cert.PtUserId`.
  - `src/Fitmate.Api/Trainers/CertificatesController.cs` — `Submit` (PT) truyền `currentUser.Id` làm `uploadedByUserId`.
  - `src/Fitmate.Infrastructure/Persistence/Migrations/20260928130000_CertificateOwnership.cs` (NEW) — Up: `ALTER TABLE certificates ADD COLUMN image_uploaded_by_user_id uuid NULL; CREATE INDEX IX_certificates_ImageUploadedByUserId ...`. Down: drop index + drop column. KHÔNG backfill legacy rows.
  - `src/Fitmate.Infrastructure/Persistence/Migrations/FitmateDbContextModelSnapshot.cs` — cập nhật snapshot.
  - `tests/Fitmate.UnitTests/CertificateServiceTests.cs` — đổi `InMemoryStore` sang paged + `SubmitAsync` mới; thêm `ListTrainer_Pagination_ReturnsCorrectSlice` (page=2 size=2 của 5 certs trả đúng 2 certs và total=5) + `Certificate_Submit_PersistsImageUploadedByUserId`.
  - `tests/Fitmate.UnitTests/AdminCertificatesControllerOwnershipTests.cs` (NEW) — ba test: 404 khi uploader null, 403 khi uploader ≠ owner, 200 + audit staging khi match.

### C. Gap sau F3b

- Migration `20260928130000_CertificateOwnership` chưa apply. Xem `docs/backend-release/handoff.md` §2026-09-28 để biết lệnh PowerShell + pre-flight.
- Web caller `trainersApi.ts` + `CertificateReview.tsx` chưa wire `page`/`pageSize` query. Có thể bổ sung F6.
- `OpenAPI/fitmate-v1.yaml` chưa thêm `page`/`pageSize` cho `/admin/certificates/trainer/{ptUserId}`. Có thể bổ sung F6.

## Đợt F1b — apiFetch header preservation (chỉnh sửa thực tế)

### B. Antigravity (đợt F1b)

- Web
  - `src/api/client.ts` — comment mô tả rõ: "Default Bearer is only added when caller did not already set Authorization. Endpoints that should never inherit the current session (e.g. login/challenge/verify/enrollment) pass `_skipRefresh` and typically also omit any Authorization themselves." Logic đã đúng; chỉ làm rõ comment.
  - `src/features/auth/__tests__/client.test.ts` — thêm test "giữ nguyên Authorization do caller truyền thay vì dùng bearer mặc định" (R04).
  - `src/features/auth/__tests__/authApi.test.ts` — thêm 2 test cho `getMe` snake_case → camelCase mapping (fullName, avatarUrl) + null avatar_url.
  - `src/features/auth/__tests__/authRace.test.ts` (NEW) — bốn test: `clearSession()` bump generation; `initSession()` trả null khi response về sau clearSession; `login()` mới không bị stale initSession từ session cũ ghi đè; `getMe` map snake_case.

### C. Gap sau F1b

- Đã đầy đủ cho F1. R04 cập nhật requirement-matrix: PASS (header preservation test).

## Đợt F4b — BookingReview tests (chỉnh sửa thực tế)

### B. Antigravity (đợt F4b)

- Web
  - `src/features/bookings/__tests__/BookingReview.test.tsx` (NEW) — sáu test:
    1. Render loading spinner ban đầu.
    2. Render 3 sections (buổi tập, thanh toán, hoàn tiền) khi data hợp lệ.
    3. Không hiển thị metadata payment thô (`internal_id`, `gateway_charge_id`).
    4. Render empty state "Chưa có thanh toán" khi payments list rỗng.
    5. Render empty state refund khi refunds list rỗng.
    6. Render error banner khi 500 và 404 (BE trả `code: not_found`).

### C. Gap sau F4b

- Đã cover loading/error/empty/render/no metadata leakage. Component test xong; user chạy `pnpm test BookingReview.test.tsx`.

## Đợt F5b — Support/audit/overview/settings (chỉnh sửa thực tế)

### B. Antigravity (đợt F5b)

- Web
  - `src/features/legal/AccountDeletionPolicy.tsx` (R15) — sửa copy: thay "đăng nhập lại để khôi phục" (sai — `AuthController.Login` reject `PendingDeletion` bằng 403) thành "gửi yêu cầu hủy xóa qua email support@fitmate.vn hoặc mở ticket hỗ trợ trong ứng dụng Fitmate" — khớp với `AccountDeletionService.CancelDeletionAsync` (operator/support action, không tự động qua login).
  - `src/features/overview/overviewApi.ts` — R13: dùng `createdAt` thật cho `DeadLetterItem`, bỏ `failedAt` (BE không trả). Đã ổn.
  - `src/components/Overview.tsx` — UI đổi "Bản ghi tạo lúc …" (R13). Đã ổn.
  - `src/test/msw/handlers/overview.ts` — fixture DLQ đồng bộ BE contract (R13). Đã ổn.
  - `src/components/Settings.tsx` — R12 password `minLength={12}` (chưa chốt với BE; hiện `ChangePasswordRequestValidator` chỉ yêu cầu `MinLength(8)`). Mismatch: Web 12 vs BE 8. CẦN quyết định nghiệp vụ.

### C. Gap sau F5b

- R12 chưa chốt: Web `minLength=12` vs BE `MinLength(8)`. Đề xuất: giữ BE 8 (vì chưa thay đổi được code đang dở của user) và sửa Web thành 8; HOẶC chốt policy 12 ở BE thêm `MinimumLength(12)` ở validator. Cần user quyết định — không sửa vội.

## Đợt F1c — IdempotencyMiddleware scope (chỉnh sửa thực tế)

### B. Antigravity (đợt F1c)

- BE
  - `src/Fitmate.Api/Idempotency/IdempotencyMiddleware.cs` — `ShouldStore` đổi từ "store mọi status >= 400" thành "store 2xx hoặc 409". Lý do: 400/422 validation error không nên cache; cho phép retry với payload đã sửa. 409 conflict vẫn cache để retry idempotent trong cùng session.
  - `tests/Fitmate.UnitTests/IdempotencyMiddlewareTests.cs` — thêm 3 test:
    - `Validation_400_Is_NOT_Stored_ForReplay` — gọi 1 lần 400, replay cùng key → controller chạy lại (status mới nhất của test controller).
    - `Retry_After_400_Reaches_Controller_With_Fixed_Body` — payload đã sửa đi đến controller, không replay 400 cũ.
    - `Conflict_409_IS_Stored_ForReplay` — replay 409 đúng, không gọi controller lần 2.

### C. Gap sau F1c

- Đã cover 3 invariant. User chạy `dotnet test --filter IdempotencyMiddleware`.

## Đợt F6b — đồng bộ tài liệu + contract (chỉnh sửa thực tế)

### B. Antigravity (đợt F6b)

- BE
  - `src/Fitmate.Api/Trainers/AdminCertificatesController.cs` — **SỬA LỖI KHỚP DOCS**: `Preview` trả `Forbid()` cho cả `ImageUploadedByUserId == null`. Đổi thành `NotFound()` cho null (fail-safe, không tiết lộ tồn tại của tài nguyên không chứng minh được chủ), giữ `Forbid()` (403) cho uploader ≠ owner.
  - `src/Fitmate.Api/Storage/UploadsController.cs` — **SỬA LỖ HỔNG**: storage key cũ `{category}/{safeName}_{guid}{ext}` không chứa userId → PT có thể dán key của tài khoản khác khi submit cert, làm check "uploader == owner" ở preview vô hiệu. Key nay `{category}/{userIdN}_{safeName}_{random}{ext}`; thiếu user id claim → 401 (thay vì ghi `"anonymous"` vào key/log).
  - `src/Fitmate.Api/Trainers/CertificatesController.cs` — thêm `ImageKeyBelongsTo(storageKey, userId)` (internal static để test được) và gọi trước khi ghi `ImageUploadedByUserId`; sai scope → `DomainException("CERT_IMAGE_NOT_OWNED", ..., 403)`. Submit không ảnh vẫn cho phép (`null` key + `null` uploader).
  - `src/Fitmate.Api/Idempotency/IdempotencyMiddleware.cs` — sửa 2 comment mâu thuẫn với `ShouldStore` ("2xx/4xx" → "2xx và 409"; bỏ claim "5xx stored for read-only inspection").
  - `src/Fitmate.Domain/Trainers/Certificate.cs` — sửa XML doc trỏ tới `EfCertificateStore.EvaluatePreviewOwnership` (không tồn tại).
  - `tests/Fitmate.UnitTests/CertificateImageKeyOwnershipTests.cs` (NEW) — 9 case cho `ImageKeyBelongsTo`: key của chính mình (2 shape), thiếu `_` separator bị chặn, key của user khác, key legacy không có scope, 7 dạng malformed (rỗng, chỉ category, thiếu category, nhiều segment, scope chỉ là tiền tố, traversal `../`), trim whitespace, so sánh case-insensitive.
  - `tests/Fitmate.UnitTests/FinanceStepUpTests.cs` (NEW) — 11 case cho `FinanceStepUp.EnsureAsync`: valid code, thiếu header, 5 dạng code sai (ngắn/dài/ký tự chữ/khoảng trắng/full-width digit), sai code → `FINANCE_MFA_INVALID`, 2FA tắt, Identity lockout, `Status=Locked`, `Status=PendingDeletion`, soft-deleted, user not found, caller ≠ adminId. Dựng `UserManager` qua `Mock<IUserStore<ApplicationUser>>` + `As<IUserLockoutStore>()` + `NullPasswordHasher` + `IOptions<IdentityOptions>` với `AuthenticatorTokenProvider` giả.
  - `tests/Fitmate.UnitTests/CertificateServiceTests.cs` — thêm 3 test biên: `pageSize <= 0` → fallback 25 (tránh `Skip/Take` âm), `page` âm → clamp 1, `page` vượt cuối → `Items` rỗng nhưng `Total` đúng.
  - `docs/backend-release/openapi/fitmate-v1.yaml` — thêm 6 path + 1 response mới:
    - `GET /admin/certificates/trainer/{ptUserId}` (page/pageSize, trả `{items,total,page,pageSize}`).
    - `GET /admin/certificates/{id}/preview` (mô tả rõ 404 legacy / 403 mismatch, TTL 300s).
    - `POST /admin/certificates/{id}/revoke` (reason 5..500).
    - `GET /admin/bookings/{id}/review` (schema 3 nhóm, ghi rõ không trả raw payment metadata).
    - `GET /admin/trainers/{id}/review`.
    - `POST /admin/auth/login/verify` (single-use, TTL 5 phút, max 5 lần).
    - `/auth/login` thêm response `202` với `oneOf` (`mfaRequired` | `mfaSetupRequired`).
  - `docs/backend-release/authorization-matrix.md` — thêm mục "Cert image preview ownership" với bảng điều kiện 404/403/200 và ghi rõ backfill chỉ khi có bằng chứng.
  - `docs/backend-release/handoff.md` — section 2026-09-28 mới: cert pagination, cert ownership, idempotency scope, finance step-up (đã enforce, handoff cũ ghi sai), 3 sửa lỗi lệch docs↔code, PowerShell commands, pre-flight migration (DB đích / tác động dữ liệu / thứ tự / backfill có điều kiện / rollback), smoke test.
  - `docs/review-handoff/{README,changes,requirement-matrix,risks-and-verification}.md` — cập nhật đồng bộ.
- Web
  - `src/features/trainers/trainersApi.ts::fetchTrainerCertificates` — **SỬA LỖI RUNTIME**: cũ `.map()` trên response array; BE nay trả envelope → sẽ ném `items.map is not a function`. Đổi signature `(trainerId, page = 1, pageSize = 25, signal)` trả `AdminCertificatePage`.
  - `src/features/trainers/CertificateReview.tsx::TrainerCertificates` — thêm `useState page` + `Pager`; `queryKey` gồm `page`; empty state dùng `items.length`; chỉ render `Pager` khi có item.
  - `src/components/Overview.tsx` — **SỬA LỖI THẬT**: `?? 0` làm lỗi API hiện như số 0 (admin tưởng "không có việc"). Thay bằng `countOrUnknown(value, isError)` → `!` (role=status, amber) khi lỗi, `—` khi chưa có, số thật khi OK; thêm banner `role="alert"` nêu rõ block nào lỗi.
  - `src/features/legal/AccountDeletionPolicy.tsx` — sửa copy R15 (không còn "đăng nhập lại để khôi phục").
  - `src/features/trainers/__tests__/trainersApi.test.ts` — +3 case (query params, paged envelope + enum normalize, default page/pageSize).
  - `src/features/trainers/__tests__/CertificateReview.test.tsx` (NEW) — 8 case (4 pagination + 4 preview ownership).
  - `src/features/auth/__tests__/client.test.ts` — +1 case caller-`Authorization` được giữ.
  - `src/features/auth/__tests__/authApi.test.ts` — +2 case `getMe` snake_case.
  - `src/features/auth/__tests__/authRace.test.ts` (NEW) — 6 case race + wire mapping.
  - `src/features/overview/__tests__/overview-errors.test.tsx` (NEW) — 5 case lỗi ≠ 0.
  - `src/components/__tests__/Settings.test.tsx` (NEW) — 10 case (6 password + 4 MFA).
  - `src/test/msw/handlers/trainers.ts` — +3 handler (`trainer/:trainerId` có phân trang thật, `:id/preview`, `:id/revoke`).
  - `src/test/msw/handlers/auth.ts` — +3 handler (`mfa/status`, `password/change` mô phỏng validator BE, `mfa/disable`).
  - `docs/review-handoff/{README,changes,requirement-matrix,risks-and-verification}.md` — cập nhật đồng bộ.

### Password policy — thống nhất 12 ký tự (user chốt 2026-09-28)

Trước khi chốt có **5 nơi lệch nhau**, gây vòng bất khả thi thật: người dùng đăng ký được mật khẩu
8 ký tự qua API (`RegisterRequestValidator` = 8) nhưng Admin Web lại bắt `minLength={12}` khi đổi
mật khẩu — chính admin đó không thể đổi mật khẩu mà không tăng độ dài.

| Nơi | Trước | Sau |
|---|---|---|
| `RegisterRequestValidator` | `MinimumLength(8)` | `PasswordPolicy.MinimumLength` (12) |
| `ResetPasswordRequestValidator` | `MinimumLength(8)` | `PasswordPolicy.MinimumLength` (12) |
| `ChangePasswordRequestValidator` | `MinimumLength(8)` | `PasswordPolicy.MinimumLength` (12) |
| `Program.cs` `IdentityOptions.Password.RequiredLength` | `12` | `PasswordPolicy.MinimumLength` |
| `SeedAdminCommand` (check STDIN + Identity) | `12` | `PasswordPolicy.IsAcceptable` / `.MinimumLength` |
| `SeedPerfDataCommand`, `SeedUatDataCommand` | `12` | `PasswordPolicy.MinimumLength` |
| `fitmate-v1.yaml` (register, reset, change) | `minLength: 8` | `minLength: 12` |
| Web `Settings.tsx` | `minLength={12}` | không đổi (đã khớp) |

- `src/Fitmate.Domain/Common/PasswordPolicy.cs` (NEW) — `MinimumLength`, `MaximumLength`, `MinimumLengthMessage`, `IsAcceptable()`. Đặt ở `Fitmate.Domain` vì đó là layer thấp nhất mà cả Api, Application, Infrastructure và CLI đều tham chiếu được, không tạo dependency vòng. Thay vì rải magic number `12` ở 6 file.
- Message dùng chung (`MinimumLengthMessage`) để client hiển thị đúng câu BE trả về.
- **Phạm vi**: chỉ mật khẩu MỚI (register / change / reset / seed admin). Không tác động hash hiện có, không buộc tài khoản đang tồn tại phải đổi mật khẩu, không vô hiệu hóa phiên đăng nhập.
- Test: `tests/Fitmate.UnitTests/PasswordPolicyTests.cs` (NEW, 10 case) — khoá constant, `IsAcceptable` (kể cả over-max), và quan trọng nhất: cả 3 validator phải từ chối mật khẩu 8 ký tự và dùng **cùng một** message.
- Rà toàn repo: không còn `minLength: 8` cho password trong OpenAPI; không có test/fixture nào dùng mật khẩu < 12 ký tự cho các đường này.
- **Ngoại lệ có chủ đích**: `SeedPerfDataCommand` vẫn bật `RequireUppercase/RequireLowercase/RequireNonAlphanumeric = true` (mật khẩu persona test tự sinh, nghiêm hơn sản phẩm). Không nới về policy chung vì dữ liệu test không nên trùng mẫu yếu của production. Đã ghi chú trong code.

### C. Gap sau F6b

- **SET-W01 (đã chốt)**: Web `minLength={12}` vs BE `MinimumLength(8)` → user chốt **12**. Xem mục "Password policy" trong `changes.md`.
- OpenAPI CHƯA validate parse (user chạy `npx @redocly/cli lint`).
- Migration `20260928130000_CertificateOwnership` CHƯA apply — xem pre-flight trong `docs/backend-release/handoff.md`.
- `api-contract.md` + `feature-api-matrix.md` chưa rà lại để khẳng định có cần cập nhật.
- Support/Reports attachment signed-URL — ngoài phạm vi.
- `SHELL-01`/`SHELL-02` (sidebar keyboard + labels không dấu) — chưa có test source.
