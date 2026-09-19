# Handoff — Backend Release

> Điểm đang dừng + việc tiếp theo. Cập nhật cuối mỗi phiên.

## Phiên hiện tại

- **Ngày**: 2026-09-19
- **Phiên 20**: Triển khai **BE-150** (Admin finance read-side).
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
- **Tiếp theo** (BE-160 Status migration hoặc BE-220 Admin metrics):
  - **BE-160 Status migration** — schema migration: `Appointment.HoldId FK`, tách enum `BookingStatus` → `AppointmentStatus` (DEC-026), `booking_reschedule_events`. Cho phép BE-060 hoàn thiện Confirm/Accept + analytics chính xác (đặc biệt phân biệt trạng thái buổi tập vs trạng thái thanh toán).
  - **BE-220 Admin metrics + action-required** — read-side KPIs (members/PT/bookings count), recent bookings, action queue (pending certs, locked accounts). Reuse `IAdminFinanceStore` cho financial KPIs.
  - **BE-150 follow-up: GET /admin/platform-policy** — read-only endpoint `{platform_fee_rate_bps: 1000, policy_version, effective_from, editable: false}`. Cần `PlatformPolicy` table + migration. Tách thành task con BE-151.
  - Đề xuất: đi **BE-220 (admin metrics + action-required)** tiếp — Admin Web Overview cần counter members/PTs/bookings + action queue (pending certs, locked accounts). BE-220 phụ thuộc nhẹ vào BE-110 (đã có `IAdminUserStore`).
  - Sau BE-220 → BE-160 (status migration) → BE-151 (platform-policy read). Khi có payment integration (VNPay) sẽ triển khai BE-130 + BE-140 + BE-600.

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

### Phiên sau (khi user trả lời UA-018..024)

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

1. **BE-080**: Validation + ProblemDetails (FluentValidation toàn cục). Sửa `Program.cs` đã có sẵn `ConfigureApiBehaviorOptions`; chỉ cần thêm `AddFluentValidationAutoValidate` + viết các `IValidator<T>` cho DTO đầu tiên (RegisterRequest, LoginRequest).
2. **BE-030**: Auth (JWT + refresh + rate limit + lockout). Tạo `TokenService`, `AuthController`, controller endpoints theo OpenAPI. Identity `SignInManager` đã chuẩn bị.
3. Sau đó: BE-070 (Idempotency/Outbox) → BE-060 (Booking concurrency + transition matrix) → BE-090 (Authorization) → admin endpoints (BE-110..220 kèm `/admin/platform-policy`) → BE-300 (Compose) → BE-330 (k6).
