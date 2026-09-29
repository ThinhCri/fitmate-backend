# Risks and verification — handoff

## Trạng thái xác minh

- **Mọi thay đổi dưới đây ban đầu đều chưa được chạy build/test/typecheck/Docker/migration/runtime.** Cập nhật 2026-09-29:
  - **BE (FitmateAI_BE) — ĐÃ xác minh**: `dotnet build tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj` 0 error; `dotnet test` **700/700 pass**.
  - **Web (FitmateWebApp) — CHƯA chạy**: `npm run typecheck` / `test:run` / `build` vẫn cần user chạy.
  - **Integration test BE — CHƯA chạy** (cần Postgres theo `docs/backend-release/runbooks/`). Các claim về transaction/atomicity vẫn chỉ chứng minh tĩnh.
  - Migration `20260928130000_CertificateOwnership` vẫn CHƯA apply; OpenAPI vẫn CHƯA lint.
- Verifier cho phần còn lại (Web build, integration test, migration apply, OpenAPI lint) là user/operator.
- Mọi patch có hậu quả schema phải được user review Up/Down + apply trên DB test theo
  runbook hiện hành trước khi chạy production.

## Rủi ro còn mở (ranking)

### R01 — Setup MFA không bước step-up cho admin đã active (post-fix)
- Hiện `AdminMfaController.SetupMfa` đã trả 409 nếu `user.TwoFactorEnabled`; secret key
  cho active admin không bị leak. Risk nếu sau này mở rộng đường re-enroll/recover phải
  kèm recent TOTP/recovery proof. Theo dõi trong audit gating; thêm test source nếu code
  thay đổi.

### R02 — Refresh rotation concurrency guard (RESOLVED in working tree)
- `RefreshToken.RevokedAt` đã là `IsConcurrencyToken` ở `IdentityConfiguration.RefreshTokenConfiguration`.
- `IRefreshTokenStore.TrySaveRotationAsync` đã catch `DbUpdateConcurrencyException` và rollback; winner giữ token cũ (revoked) + tạo successor; loser trả null.
- Đã rà soát tĩnh; integration test chưa chạy. Đã ghi nhận trong `requirement-matrix.md::AUTH-03` là ĐÃ GIẢI.

### R03 — `X-Fitmate-Client` nhầm giữa native và web (chưa sửa)
- `CookieRequestAllowed` hiện chấp nhận native khi không có Origin và không có cookie.
- Rủi ro: native clients gửi cùng header có thể bị nhầm là web; chưa ghi nhận bug cụ thể.
- Theo dõi; chưa sửa trong phạm vi đợt này. Nếu BE có auth flow khác cho cùng IP, có thể nhầm.

### R03 — SetRefreshTokenCookie dựa `X-Fitmate-Client=admin-web` header. Native clients gửi cùng
header có thể bị nhầm.
- CookieRequestAllowed hiện chấp nhận native khi không có Origin và không có cookie.
- CầN phân biệt native vs web rõ ràng hơn; thêm test cho 2 client kind cùng gọi login.

### R04 — Web client.ts Authorization header (RESOLVED in working tree)
- Hiện `apiFetch` chỉ set `Authorization` mặc định khi caller KHÔNG truyền header. Caller set
  custom header sẽ giữ nguyên (ví dụ setup token / `_skipRefresh`).
- Đã thêm comment giải thích trong `api/client.ts`. Chưa có test cho default-vs-custom; đã
  ghi nhận trong `requirement-matrix.md::AUTH-08` và `changes.md` (đợt F1).

### R05 — `Logout` ở web: hiện revoke mọi session bằng cách gọi `Sessions.RevokeAsync(sessionId, userId, ct)` chỉ theo `sid` claim (đúng). Tuy nhiên `user.FindFirstValue("sid")` fallback "sub" sub — không gây sai nhưng user.Roles revoke có thể chạm nếu claim không đúng.
- Theo dõi khi JWT claim mapping thay đổi; thêm test cho logout khi security stamp đổi.

### R06 — Manual finance form state machine (RESOLVED in working tree)
- Hiện `ManualFinancePanel.FinanceOperation` đã tách thành 5 phase: `editing`,
  `submitting`, `validation`, `uncertain`, `conflict`. Idempotency-Key ổn định cho mọi
  retry trong cùng intent; 400 re-enable fields để user sửa nhưng giữ key; 409 đóng &
  tải lại; network/timeout giữ body + key cho retry.
- Đã thêm test source tại `src/features/finance/__tests__/ManualFinancePanel.test.tsx`
  (4 case); test CHƯA chạy.

### R07 — `EfAdminReviewStore.BookingAsync` không authorize per-actor; controller authorize bằng policy.
- Ổn vì policy `AdminOnly`. Tuy nhiên re-check ownership của payment/refund metadata
  tránh lộ. Đã có allowlist fields.

### R08 — Support: input sai từ HTTP trả 500 thay vì 400/409 (RESOLVED 2026-09-28)
- `UseExceptionHandler` trong `Program.cs` chỉ map `DomainException` sang ProblemDetails; `ArgumentException`/`InvalidOperationException` rơi vào nhánh 500. Đọc kỹ mới thấy **4 chỗ nhận input HTTP** bị dính, không chỉ 1 như handoff cũ ghi:
  1. `CreateTicketAsync` — subject/category/message rỗng.
  2. `AdminChangeStatusAsync` — `status` không parse được.
  3. `UserReplyTicketAsync` / `AdminReplyTicketAsync` — `message` rỗng, và `AddUserReply`/`AddAdminReply` ném `InvalidOperationException` khi ticket đã `Closed` (⇒ 500 cho hành động hoàn toàn hợp lệ).
  4. `ListMyTicketsAsync` / `ListAdminTicketsAsync` — `status` là số không thuộc enum (vd `99`): `Enum.TryParse` vẫn thành công nên store lọc theo giá trị bịa ⇒ trả 0 dòng, admin tưởng không có ticket nào.
- Đã sửa: cả 4 → `ValidationException(ErrorCodes.Validation.FieldInvalid, …, errors dictionary)` với key `subject` / `category` / `message` / `status`; reply ticket `Closed` → `ConflictException(ErrorCodes.Support.TicketClosed)` (409) thay vì 500; thêm `Enum.IsDefined` vào cả 2 list filter. Thêm `ErrorCodes.Support` mới trong `Fitmate.Domain/Common/ErrorCodes.cs`.
- Rà toàn `Fitmate.Api`: không còn `throw new ArgumentException` nào. Các `ArgumentException` còn lại trong `Fitmate.Application` (`Money`, `PasswordHasher`, `OutboxDispatcher`, `IdempotencyServiceImplementation`, `PushPayload`) là **internal invariant**, không phải input HTTP → giữ nguyên có chủ đích. `IdempotencyMiddleware` đã chặn key rỗng/sai dạng trước khi gọi service.
- **Sửa thêm 1 chỗ ngoài Support** (cùng lớp lỗi, phát hiện khi rà): `AdminFinanceService.ParseType` từng `throw new InvalidOperationException` khi gặp `ledger_entries.type` lạ (dữ liệu legacy) ⇒ **một dòng lỗi làm hỏng cả trang ledger** (500). Đã đổi `ParseType` trả `LedgerEntryType?` + lọc dòng không nhận diện được trước khi map. `total` vẫn giữ số đếm thật từ store nên admin thấy tổng lệch với items — chấp nhận được, thà hiển thị sai số tiền.
- `AdminFinanceController.ParseType`/`ParseRange` (bản ở controller) **đã** trả `ValidationException` 400 từ trước — không phải nguồn lỗi.
- Test: `SupportServiceTests` (NEW, 20 case) — validation theo field, enum không định nghĩa bị bỏ qua, pagination clamp, reply ticket đóng trả 409, IDOR trả `null` (404 chứ không 403). CHƯA chạy `dotnet test`.
- **Chưa rà cùng kiểu cho các service khác**: `BookingService`/`PaymentService`/`SocialService` có thể còn chỗ tương tự. Đây là hướng rà tiếp theo nếu operator muốn siết.

### R09 — `CertificateService.RevokeAsync` atomic với audit (RESOLVED in working tree)
- Đã đổi sang pattern `IAuditLogger.Stage(...)` (chỉ Add, không SaveChanges) + `SaveChangesAsync`
  một lần cho cả approve/reject/revoke → audit row sinh ra cùng transaction với state change.
- Test atomic chưa viết; integration test DB sẽ chứng minh end-to-end.

### R10 — BookingReview.tsx rỗng (RESOLVED in working tree)
- `src/features/bookings/BookingReview.tsx` đã có nội dung: 3 nhóm status/payments/refunds.
  `bookingsApi.fetchAdminBookingReview` gọi `/admin/bookings/{id}/review`; wired vào
  `BookingManagement.tsx` qua nút Receipt.
- Loading/error/empty phân biệt rõ; không hiển thị metadata payment thô.

### R11 — `getMe` wire DTO BE trả snake_case → FE mapping OK (RESOLVED)
- `authApi.getMe` đã map wire `full_name`/`avatar_url` → view `fullName`/`avatarUrl`.
- Test source: `src/features/auth/__tests__/authApi.test.ts` (NEW: 2 case — full mapping + null avatar), `src/features/auth/__tests__/authRace.test.ts` (NEW: snake_case fixture assert). CHƯA chạy vitest.

### R12 — Settings MFA copy + password min 12 vs UI min 8 (RESOLVED — user chốt 12)
- `PasswordPolicy.MinimumLength = 12` (`src/Fitmate.Domain/Common/PasswordPolicy.cs`) là nguồn duy nhất.
- BE: `RegisterRequestValidator`, `ResetPasswordRequestValidator`, `ChangePasswordRequestValidator` (thay `MinimumLength(8)`), `Program.cs` `IdentityOptions.Password.RequiredLength`, `SeedAdminCommand`/`SeedUatDataCommand`; OpenAPI 3 chỗ `minLength: 8 → 12`.
- Web `Settings.tsx` đã là `minLength={12}`; Mobile `register.tsx` `MIN_PASSWORD_LEN = 12`.
- Cả 3 validator dùng chung `MinimumLengthMessage` để client hiển thị đúng câu BE trả.
- Test: `PasswordPolicyTests` (10 case, khoá constant + 3 validator từ chối 8 ký tự).
- Ngoại lệ có chủ đích: `SeedPerfDataCommand` giữ `RequireUppercase/NonAlphanumeric = true` (mật khẩu persona test nghiêm hơn sản phẩm).

### R13 — DLQ overview map `createdAt→failedAt` sai (RESOLVED in working tree)
- BE trả `createdAt`, không có `failedAt`. Đã sửa `overviewApi.ts` để mapping sang
  `createdAt` thật + thêm `status` từ response; cập nhật `Overview.tsx` hiển thị
  "Bản ghi tạo lúc …"; fixture MSW đồng bộ.

### R14 — Support/Report attachments trong ticket chỉ là URL text.
- UI hiện chưa rõ evidence download/signed-URL. Ghi nhận để hoàn thiện; không mở attachment
  server-side từ URL thô.

### R15 — Public deletion page hứa "login để khôi phục" sai với PendingDeletion khóa login (RESOLVED)
- `AuthController.Login` thực tế reject `Status == PendingDeletion` bằng 403. Copy cũ "đăng nhập lại trong 30 ngày để khôi phục" sai.
- 2026-09-28 sửa: `AccountDeletionPolicy.tsx` thay bằng "gửi yêu cầu hủy xóa qua email support@fitmate.vn hoặc mở ticket hỗ trợ trong ứng dụng Fitmate" — khớp `AccountDeletionService.CancelDeletionAsync` (operator/support action).

### R16 — Cert list pagination server (RESOLVED đầu-cuối)
- `ICertificateService.ListTrainerForAdminAsync` clamp page≥1, pageSize 1..100, mặc định 25; `EfTrainerStores.ListByPtPagedAsync` dùng `Skip().Take()`; controller `AdminCertificatesController.GET trainer/{ptUserId}` chấp nhận `?page&pageSize` trả `{ items, total, page, pageSize }`.
- Web caller `trainersApi.ts::fetchTrainerCertificates(id, page, pageSize, signal)` trả `AdminCertificatePage`; `CertificateReview.tsx::TrainerCertificates` có `useState page` + `Pager` + `queryKey` gồm `page` (wire 2026-09-28).
- Test: `CertificateServiceTests` (4 case: slice + 3 biên) + Web `trainersApi.test.ts` (3 case) + `CertificateReview.test.tsx` (4 case). CHƯA chạy `dotnet test` / `vitest`.

### R17 — Cert preview ownership (RESOLVED với migration CHƯA apply)
- Domain `Certificate.ImageUploadedByUserId` (nullable Guid) lưu user upload. Migration `20260928130000_CertificateOwnership` thêm column + index. Controller `AdminCertificatesController.Preview`:
  - 404 NotFound nếu `ImageStorageKey` null hoặc `ImageUploadedByUserId` null (legacy).
  - 403 Forbid nếu `ImageUploadedByUserId != cert.PtUserId` (admin xem cert PT khác).
  - 200 OK + `IAuditLogger.Stage("certificate.preview.viewed")` nếu match.
- KHÔNG backfill legacy rows. Plan rollback + pre-flight xem `docs/backend-release/handoff.md` §2026-09-28.
- Test: `AdminCertificatesControllerOwnershipTests` (NEW: 3 case). CHƯA chạy `dotnet test`.

### R18 — Idempotency cache 4xx (RESOLVED)
- `IdempotencyMiddleware.ShouldStore` thu hẹp về "2xx hoặc 409 Conflict" (từ "≥400"). 4xx khác (400 validation, 422) cho phép retry với payload đã sửa. 409 vẫn cache để retry idempotent.
- Test: `IdempotencyMiddlewareTests` (NEW: 3 case). CHƯA chạy `dotnet test`.

### R19 — Storage key không chứng minh ownership (RESOLVED — lỗ hổng thật)
- Đây là lỗ hổng làm vô hiệu check của CERT-02, không chỉ là thiếu sót hình thức.
- `UploadsController` tạo key `{category}/{safeName}_{guid}{ext}` — **không có userId**. PT A lấy được key của PT B (từ signed URL lúc upload) rồi submit key đó vào cert của mình ⇒ `ImageUploadedByUserId` được ghi là A, `PtUserId` cũng là A ⇒ preview trả 200 cho ảnh của B. Kiểm `uploader == owner` không bắt được gì.
- Đã sửa 2 điểm:
  1. `UploadsController` nhúng `userId` vào key: `{category}/{userIdN}_{safeName}_{random}{ext}`; không có user id claim → 401 (trước đó ghi `"anonymous"` vào key).
  2. `CertificatesController.Submit` kiểm `ImageKeyBelongsTo(key, currentUserId)` → sai scope trả 403 `CERT_IMAGE_NOT_OWNED`. Cần cả hai: key scope chứng minh ai upload, còn cột `ImageUploadedByUserId` chứng minh cert nào dùng ảnh đó.
- Test: `CertificateImageKeyOwnershipTests` (NEW, 9 case). CHƯA chạy `dotnet test`.
- **Tác động vận hành**: file upload mới dùng format key mới. Storage key cũ không thể submit lại vào cert (403) — đúng, vì không chứng minh được chủ. Không có thay đổi schema, không cần migration cho phần này.
- **Còn lại chưa làm** (ngoài phạm vi đợt này, cần operator quyết định): key cũ đã lưu trong `certificates.image_storage_key` của các dòng hiện hữu vẫn hợp lệ để **xem** (signed URL) nhưng không submit lại được. Nếu cần backfill key cũ sang format mới thì phải có bằng chứng ai upload từng file — cùng điều kiện với backfill `ImageUploadedByUserId` (mục 4 trong pre-flight).

### R20 — Finance step-up: đã enforce, đã có test source (RESOLVED, trước đây báo thiếu)
- Handoff cũ ghi "controller chưa wire `X-Finance-Totp`" — **SAI**. Đối chiếu source: `ManualSettlementService` gọi `stepUp.EnsureAsync(adminId, ct)` trên cả 5 mutation (`CreateRefundAsync`, `ConfirmRefundAsync`, `ApproveAsync`, `HoldAsync`, `RecordTransferAsync`), tức enforce ở Application layer đúng nguyên tắc AGENTS.md.
- `FinanceStepUp.EnsureAsync` kiểm: caller == adminId, user tồn tại, không soft-delete, `Status == Active`, không Identity lockout, `TwoFactorEnabled == true`, header 6 chữ số ASCII, `VerifyTwoFactorTokenAsync`; sai code → `AccessFailedAsync` + `FINANCE_MFA_INVALID`.
- Test source mới: `FinanceStepUpTests` (11 case). Test này mock `UserManager` nên **chỉ chứng minh logic**, không chứng minh token provider thật — cần integration test với `AuthenticatorTokenProvider` thật.
- CHƯA chạy `dotnet test`.

## Kế hoạch kiểm chứng (chờ user chạy)

### Web (chạy trên `D:\Ky8\exe201\FitmateWebApp`)

```powershell
# Trong D:\Ky8\exe201\FitmateWebApp
npm ci
npm run typecheck
npm run test:run
npm run build
```

Nếu dev cần: `npm run dev -- --host 127.0.0.1`. Playwright: `npm run test:e2e`.

### BE (chạy trên `D:\Ky8\exe201\FitmateAI_BE`)

```powershell
# Trong D:\Ky8\exe201\FitmateAI_BE
dotnet build Fitmate.slnx
dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj
dotnet test tests/Fitmate.IntegrationTests/Fitmate.IntegrationTests.csproj
```

Nếu SDK không đọc slnx, dùng `dotnet build src/Fitmate.Api/Fitmate.Api.csproj` để verify
project-level.

### Migration mới (khi có)

Các thay đổi schema phải có migration Up/Down rõ ràng; user apply sau khi review nội dung.
Lệnh apply tham khảo (user chạy):

```powershell
docker compose -f compose.yml -f compose.verify300.yml up -d postgres
docker compose -f compose.yml -f compose.verify300.yml --profile migrate run --rm migrate
```

Không xóa volume/data có sẵn.

### UAT admin auth/session/finance (tóm tắt từ `06-verification-operator.md`)

| Tình huống | Expected | Test source |
|---|---|---|
| Admin password đúng chưa MFA → 202 mfaSetupRequired | Setup token gọi `/users/me` 403 | `authApi.test.ts` (existing) |
| Admin MFA, password → 202 mfaRequired → verify challenge | Verify cùng challenge lần 2 → null | `tests/Fitmate.UnitTests/AuthSessionServiceTests.cs` (NEW — invariant) |
| Refresh khi session revoked → 401 không cấp token | Web logout lần 2 không thấy token | `client.test.ts` (existing) |
| Manual approve thiếu noOpenDispute → 400 SETTLEMENT_NOT_VERIFIED | UI giữ Idempotency-Key, body editable | `ManualFinancePanel.test.tsx` (NEW) |
| Manual record-transfer hai lần → idempotent (200, cùng key) | UI retry gửi cùng key + body | `ManualFinancePanel.test.tsx` (NEW "uncertain" case) |
| BookingReview render 3 nhóm — payment null | Không crash, hiển thị empty row | `features/bookings/__tests__/BookingReview.test.tsx` (NEW: 6 case) |
| Logout → delayed `/users/me` response → không promote auth store | Hook `clearSession` bump generation; response resolve null | `authRace.test.ts` (NEW) |
| Cert list page=2 size=2 của 5 certs | Response 2 certs (cert-3, cert-4), total=5 | `CertificateServiceTests.ListTrainer_Pagination_ReturnsCorrectSlice` (NEW) |
| Cert preview của PT khác (uploader không trùng owner) | 403 Forbid | `AdminCertificatesControllerOwnershipTests` (NEW) |
| Cert preview legacy (uploader null) | 404 NotFound (fail-safe) | `AdminCertificatesControllerOwnershipTests` (NEW) |
| Upload file → storage key phải chứa userIdN | Key dạng `certificates/{userIdN}_{file}_{rand}.ext` | `CertificateImageKeyOwnershipTests` (NEW) |
| PT submit cert với key của tài khoản khác | 403 `CERT_IMAGE_NOT_OWNED`, cert không được tạo | `CertificateImageKeyOwnershipTests` (NEW) |
| PT submit cert với key không có user scope (legacy) | 403 `CERT_IMAGE_NOT_OWNED` | `CertificateImageKeyOwnershipTests` (NEW) |
| Finance mutation không có `X-Finance-Totp` | 403 `FINANCE_MFA_REQUIRED`, không đổi trạng thái | `FinanceStepUpTests` (NEW) |
| Finance mutation với TOTP sai | 403 `FINANCE_MFA_INVALID`, tăng access-failed count | `FinanceStepUpTests` (NEW) |
| Finance mutation với TOTP 6 chữ số non-ASCII | 403 `FINANCE_MFA_REQUIRED` | `FinanceStepUpTests` (NEW) |
| Finance mutation khi admin `Status=PendingDeletion` | 403 `FINANCE_MFA_REQUIRED` | `FinanceStepUpTests` (NEW) |
| Idempotency replay sau 400 validation với payload đã sửa | Controller chạy lại, không trả 400 cũ | `IdempotencyMiddlewareTests` (NEW) |
| Idempotency replay 409 conflict | Trả 409 cũ, không gọi controller lần 2 | `IdempotencyMiddlewareTests` (NEW) |

### Operator gates cần user/operator cấp

1. Secret manager / DataProtection key ring.
2. Allowed CORS origins production.
3. HTTPS / reverse proxy `allowlists`.
4. Bank reference whitelist (nếu policy yêu cầu tài nguyên kiểm soát).
5. Email support thật.
6. Merchant provider payOS live capability và OPS-03.

## Lệnh PowerShell khi có sửa đổi

Đợt hiện tại: F1 + F2 (đã chạm code + test), F3 + F4 + F5 (đã sửa + test cho thêm).

### Web (FE)

```powershell
Set-Location D:\Ky8\exe201\FitmateWebApp
npm ci
npm run typecheck
npm run test:run
npm run build
```

Mong đợi:
- typecheck: 0 error.
- test:run: 4 case `ManualFinancePanel.test.tsx` PASS; các test hiện có vẫn pass.
- build: success; bundle dưới ngưỡng warning.

### Backend (BE)

```powershell
Set-Location D:\Ky8\exe201\FitmateAI_BE
dotnet build src/Fitmate.Api/Fitmate.Api.csproj
dotnet test tests/Fitmate.UnitTests/Fitmate.UnitTests.csproj --logger "console;verbosity=normal"
```

Mong đợi:
- build: 0 error.
- test: tất cả pass bao gồm `AuthSessionServiceTests` (NEW).
- Integration tests cần Postgres theo `docs/backend-release/runbooks/`.

### Migration

Nếu schema đã có migration mới (`AdminSessionSecurity` Up/Down đầy đủ), user chạy theo runbook:

```powershell
Set-Location D:\Ky8\exe201\FitmateAI_BE
docker compose -f compose.yml up -d postgres
dotnet ef database update --project src/Fitmate.Infrastructure --startup-project src/Fitmate.Api
```

Không xóa volume/data có sẵn.

Trong working tree có nhiều thay đổi chưa commit — đừng
chạy `git clean`/`reset --hard` để "dọn" vì có thể mất việc chưa xác minh của user.
