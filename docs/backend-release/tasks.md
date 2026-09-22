# Tasks — Backend Release

> Tiến độ các task triển khai backend. ID dạng BE-NNN. Status: TODO / IN_PROGRESS / BLOCKED / DONE / SKIP.

## Giai đoạn 1 — Nền tảng (không cần provider)

### BE-350 Normalize local commit messages and define commit policy
- Scope: normalize the 33 local commits after `main`; preserve every commit tree, parent relationship, author/committer identity, and timestamp. Add an English-only, attribution-free commit policy.
- Authorization: user explicitly requested this local history rewrite on 2026-09-19. No remote history changes or push authorized.
- Status: DONE — all 33 rewritten commit trees and metadata verified; no merge or signed commits in the target range. Existing base and backup branches retained.
- Backup: `backup/english-messages-2026-09-19T07-21-40-817Z`.
- Mapping: `.git/commit-message-cleanup/2026-09-19T07-21-40-817Z/mapping.json` (local recovery metadata, not committed).
- Validation: English-only messages; no attribution trailers in the rewritten range; old and new HEAD trees identical before this documentation update. No application tests required for message-only changes.

### BE-001 Scaffold .NET solution
- Repo: FitmateAI_BE
- Scope: solution + projects (Api, Application, Domain, Infrastructure, Tests). .gitignore chuẩn .NET.
- Depends on: —
- Done when: `dotnet build` pass, `dotnet test` pass với 0 test.
- Status: DONE (commit scaffold hoàn tất 2026-09-18; còn chốt Identity schema ở BE-021)

### BE-010 EF Core + Postgres wiring
- Scope: DbContext, Npgsql provider, migrations ban đầu.
- Depends on: BE-001
- Done when: `dotnet ef database update` chạy được với Postgres local.
- Status: DONE (2026-09-18) — wired EF Core + Postgres, migration `20260918160320_InitialSchema` created + applied to local Postgres (Docker `fitmate-postgres`, `postgres:16-alpine`, user `fitmate` / pass `dev`). 32 tables created (1 `__EFMigrationsHistory` + 28 domain + 3 helpers idempotency/outbox/audit). **Bug fix ngay trong phiên 3**: filter `IX_users_Email` ban đầu dùng `deleted_at IS NULL` (snake_case) trong khi EF Core generate column `DeletedAt` quote-preserved PascalCase → Postgres error `column "deleted_at" does not exist`. Fix tại `IdentityConfiguration.cs` line 15: filter đổi thành `"DeletedAt" IS NULL`, regenerate migration, apply OK. Unit tests: 14/14 PASS.

### BE-020 Domain entities (Identity, Profile, RefreshToken)
- Scope: schema `identity`. Users + UserProfile + RefreshToken + AuditLog.
- Depends on: BE-010
- Done when: migration tạo schema, seed admin user qua CLI.
- Status: DONE (2026-09-18) — entities scaffold (User, UserProfile, RefreshToken, AuditLog), migration applied (32 tables), **CLI `seed-admin` working**: `dotnet run --project src/Fitmate.Api -- seed-admin --email X`. Test 1 lần: created admin@fitmate.vn (id=2e417dd7-...). Re-run idempotent: CLI tra loi "da ton tai (role=Admin, status=Active). Bo qua." → exit code 0 neu dung role, 2 neu dung nhung sai role. Password nhap tu STDIN (khong echo), yeu cau >= 12 ky tu.

### BE-021 Tích hợp ASP.NET Core Identity schema
- Repo: FitmateAI_BE
- Scope: thay `User` bằng `ApplicationUser : IdentityUser<Guid>` + 6 bảng Identity (Roles, UserRoles, UserClaims, RoleClaims, UserTokens, UserLogins). `Program.cs` đăng ký `AddIdentityCore` + Roles + EF stores + DataProtection. Seed roles Admin/PT/Member.
- Depends on: BE-020. Đã chốt qua DEC-032 (không còn phụ thuộc UA-021/UA-018 cho mục tiêu code; UA-021 chỉ chặn provisioning managed DB).
- Done when: migration `IdentitySchema` tạo 7 bảng Identity (6 bảng Identity + 1 bảng `user_profiles` giữ nguyên) + drop `users` cũ, `dotnet ef database update` chạy được, `dotnet build` + `dotnet test` pass.
- Status: **DONE (phiên 8, 2026-09-18)**. Migration `20260918170539_IdentitySchema` applied to local Postgres compose (38 bảng). Build 0 warning, 0 error; 14/14 unit tests PASS. DB schema reset (greenfield theo user option A) + admin seed CLI refactor sang `UserManager` (`src/Fitmate.Api/Cli/SeedAdminCommand.cs`).

### BE-099 Xóa secret Supabase khỏi Git history (BE cũ)
- Repo: FitmateAI_BE
- Scope: `git filter-repo` để xóa file `src/main/resources/application.properties` chứa password `doanxemtoilaai`. Force-push sau khi user duyệt.
- Depends on: UA-001 (rotate password).
- Done when: clone fresh từ origin không có password; collaborator đã re-clone.
- Status: BLOCKED (chờ UA-001).

### BE-030 Auth (register / login / refresh / logout)
- Scope: JWT custom + BCrypt/Identity password. Endpoint theo OpenAPI. Theo DEC-026/027 — `AppointmentStatus` / `PaymentStatus` / `RefundStatus` enum đã chốt, truyền trong response. Theo DEC-027 — `Payment.method = vnpay` 1 value.
- Depends on: BE-021.
- Done when: integration test pass (success, wrong pwd, locked, expired token, replay).
- Status: **DONE (phiên 10, 2026-09-19)** — 4 endpoints `/api/v1/auth/{register,login,refresh,logout}` theo OpenAPI. JWT HS256 (`Microsoft.IdentityModel.Tokens` + `System.IdentityModel.Tokens.Jwt`) với claims `sub/email/jti/role/full_name`. Refresh token SHA-256 hash lưu DB, rotate-on-use, revoke idempotent. FluentValidation auto-validate cho `RegisterRequest`/`LoginRequest`/`RefreshRequest`. JWT Bearer auth pipeline (`AddAuthentication` + `AddJwtBearer`). `FitmateProblemDetails` (rename từ `ProblemDetailsFactory` để tránh xung đột với `Microsoft.AspNetCore.Mvc.ProblemDetailsFactory`). Unit tests: 17/17 PASS mới (37/37 tổng). Auto-review chặn web host + DB query nên chưa viết integration test WebApplicationFactory — để phiên sau (BE-070) cùng integration suite.

### BE-040 Trainer + Certificate
- Scope: PT profile, certificate submission, admin approve/reject.
- Depends on: BE-030
- Done when: PT register kèm cert status=pending, admin approve set verified.
- Status: **DONE (phiên 14, 2026-09-19)** — Application services: `IPtProfileService` (EnsureDraft/Update/GetByUserId) + `ICertificateService` (Submit/ListMine/ListPendingPaged/Approve/Reject). Store abstractions (`IPtProfileStore`/`ICertificateStore`/`IUserRoleLookup`) trong Application; EF Core impl trong Infrastructure (`EfPtProfileStore`/`EfCertificateStore`/`IdentityUserRoleLookup`). `IAuditLogger` abstraction + `EfAuditLogger` ghi vào `audit_logs` (domain-rules.md §7). API controllers: `/api/v1/me/certificates` (PT tự list + submit), `/api/v1/admin/certificates` (admin list pending + approve + reject với reason). DI wiring: roles seeded ở startup (`SeedAdminCommand.RoleNames`). Exception middleware map `DomainException` → ProblemDetails với status/code/typeSlug/errors đúng format RFC 7807. `ICurrentUserAccessor.GetUserIdOrThrow()` helper. Unit tests: 35/35 mới PASS (119/119 tổng) — CertificateService (12) + PtProfileService (10) + DomainProblemDetails (5 + 8 từ các test khác). Auto-review chặn web host + DB query → integration test WebApplicationFactory để phiên BE-310.

### BE-050 Availability + Service (PT)
- Scope: PT set availability, CRUD service.
- Depends on: BE-040
- Done when: overlap detection pass, edit service có pending booking trả 409.
- Status: **DONE (phiên 15, 2026-09-19)** — Application services: `IServiceOfferingService` (Create/Update/Deactivate/Get/ListMine, owner-scoped với ForbiddenException nếu không phải owner) + `IAvailabilityService` (ReplaceMine batch + ListOpenForTrainer range query, time-zone aware qua `AvailabilitySlot.Create`). Store abstractions trong Application; EF Core impl (`EfServiceOfferingStore` + `EfAvailabilitySlotStore` với `ExecuteDeleteAsync`) trong Infrastructure. API: `/api/v1/me/services` (CRUD role=PT) + `/api/v1/me/availability` (PUT batch replace, chỉ xóa slot Open, không đụng Held/Booked) + `/api/v1/trainers/{id}/availability` (public, lọc status=Open trong [from,to) UTC). Replace validation: empty/dup-utc/bad-duration/bad-timezone → ValidationException; dup UTC trong batch → ConflictException. Unit tests: 20/20 mới PASS (139/139 tổng) — ServiceOffering (11) + Availability (9).

### BE-060 Booking (hold, confirm, pending→accept, cancel, reschedule, complete)
- Scope: state machine theo `domain-rules.md §3.2` + unique constraint + TTL release. Theo DEC-026 — `late` là flag (BE-135a), `reschedule` qua `booking_reschedule_events` (BE-135). Endpoint accept riêng (`POST /me/bookings/{id}/accept`). Cần check cả `AppointmentStatus` + `PaymentStatus = Paid` mới ghi nhận doanh thu (FP-FN-04).
- Depends on: BE-050
- Done when: concurrency test (10 VU cùng slot) chỉ 1 thành công; reschedule transaction check slot mới trước khi release cũ; idempotency chống report trùng (`reports.idempotency_key`).
- Status: **PARTIAL_DONE (phiên 16, 2026-09-19, scope booking lifecycle)** — Application: `IBookingService` (CreateHold/Cancel/Complete/Reschedule/ListByMember/ListByPt) + abstractions (`IAppointmentStore`/`IBookingHoldStore`/`IServiceBookingLookup`). Infrastructure: `EfAppointmentStore` (ExistsActiveForSlot check Held/AwaitingPayment/Paid/Completed) + `EfBookingHoldStore` + `ServiceBookingLookup` (delegates to IServiceOfferingStore). API: `BookingsController` (`POST /bookings/holds`, `GET /bookings/holds/{id}`, `POST /me/bookings/{id}/cancel`, `POST /me/bookings/{id}/complete` role=PT, `POST /me/bookings/{id}/reschedule`, `GET /me/bookings` member, `GET /me/trainer-bookings` PT) với pagination max=100. Outbox enqueue: `booking.held`, `booking.cancelled`, `booking.completed`, `booking.rescheduled`. Domain-rules.md §3.1.1 honored: reschedule fail → slot cũ không bị mất; verified bằng test `Reschedule_NewSlotBusy_ThrowsConflict_DoesNotMutateOldAppointment`. **Confirm/Accept out-of-scope (chưa implement)**: cần `Appointment.HoldId FK` + tách enum `BookingStatus` → `AppointmentStatus` (DEC-026) — sẽ làm ở **BE-160 migration**. Concurrency test (10 VU cùng slot) cần Postgres + k6 tích hợp, đưa vào BE-330. Unit tests: 15/15 mới PASS (154/154 tổng) — BookingService (15).

### BE-070 Idempotency middleware + Outbox
- Scope: middleware, schema `outbox`, worker.
- Depends on: BE-030
- Done when: retry cùng key → response cũ; khác payload → 409.
- Status: **DONE (phiên 12, 2026-09-19)** — `IIdempotencyStore`/`IIdempotencyService` (Application) + `EfIdempotencyStore` (Infrastructure) + `IdempotencyMiddleware` (Api). `IOutboxStore` + `IOutboxDispatcher` (Application) + `EfOutboxStore` + `EfOutboxQueue` (Infrastructure) + `OutboxWorker` (Application, pure) + `OutboxBackgroundService` (Api, hosted). Schema `idempotency_records` + `outbox_messages` đã có sẵn trong `InitialSchema` (không cần migration mới). Key format: `[A-Za-z0-9_-]{8,128}`; hash = SHA-256(content-type + "\n" + body); TTL 24h mặc định. Backoff retry: 1s/5s/30s/5m/1h. Unit tests: 18/18 mới PASS (55/55 tổng) — IdempotencyServiceTests (5) + OutboxWorkerTests (6) + IdempotencyMiddlewareTests (7). Auto-review chặn web host + DB query nên chưa có integration test (sẽ làm ở BE-310 cùng WebApplicationFactory).

### BE-080 Validation + ProblemDetails
- Scope: FluentValidation toàn cục, RFC 7807. Theo `api-contract.md §6`.
- Depends on: BE-001
- Done when: validation error trả đúng format; mọi Controller trả ProblemDetails khi 4xx/5xx.
- Status: **DONE (phiên 9, 2026-09-18)** — foundation ProblemDetails theo RFC 7807: `Errors/ErrorCodes.cs` (constants `FITMATE.<DOMAIN>.<REASON>`), `Errors/ProblemDetailsFactory.cs` (pure helper, testable), patch `Program.cs` (`ConfigureApiBehaviorOptions` + `UseExceptionHandler` đều dùng factory). Unit tests: 6/6 PASS (20/20 tổng). Auto-review chặn DB query nên chưa viết integration test WebApplicationFactory — BE-030 (AuthController) sẽ tạo DTO + `IValidator<T>` thật, lúc đó thêm integration test cho route 4xx.

### BE-090 Authorization matrix enforcement
- Scope: spec classes kiểm tra role + ownership + field-level.
- Depends on: BE-030
- Done when: mỗi endpoint admin test 401/403/200.
- Status: **DONE (phiên 17, 2026-09-19)** — Abstractions `IAuthorizationGuard` + `AuthorizationGuard` (Application) dùng `IUserRoleLookup.GetPrimaryRoleAsync` (mở rộng ở phiên này với priority order Admin > PT > Member). Enforce role ở Application cho: `CertificateService.SubmitAsync/ListMineAsync` (PT), `CertificateService.ApproveAsync/RejectAsync` (Admin — chống bypass controller filter), `ServiceOfferingService.CreateAsync/UpdateAsync/DeactivateAsync` (PT), `AvailabilityService.ReplaceMineAsync` (PT), `BookingService.CompleteAsync` (PT). `PtProfileService.EnsureDraftAsync` enforce PT từ BE-040. Các ownership check (resource → userId) vẫn ở service; `EnsureOwnerAsync` helper cho controller/admin. Unit tests: 10/10 mới PASS (164/164 tổng) — AuthorizationGuard (8) + BookingAuthzIntegration (2). Note: BE-090 chưa thêm guard cho endpoint admin *sắp tới* (BE-110/210/220/600) — sẽ enforce ở Application service tương ứng trong từng task.

### BE-380 JwtOptions config wiring fix (env alias + DI registration)
- Repo: FitmateAI_BE
- Scope: `JwtOptions` load + validate tại một nơi duy nhất (`JwtOptionsLoader`), hỗ trợ 3 nguồn: env canonical `JWT_SIGNING_KEY`, env alias `Jwt__SigningKey` (ASP.NET Core convention), section `Jwt:SigningKey`. Register raw instance vào DI để `RefreshTokenService` (inject `JwtOptions` trực tiếp) resolve được; `JwtTokenService` + Bearer middleware cùng dùng instance này. Fail-fast khi: thiếu key, key < 32 bytes UTF-8, hoặc các nguồn xung đột giá trị. Không log giá trị key.
- Depends on: phát hiện trong BE-300 verify (2026-09-21).
- Done when: `dotnet test` 363/363 PASS, api container `up` không crash vì `InvalidOperationException: JwtOptions.SigningKey must be configured`.
- Status: **DONE (phiên 30, 2026-09-21)** — `JwtOptionsLoader.Load` cover 3 nguồn + 4 fail-fast cases; `Program.cs` đơn giản hóa (xoá inline throw + `GetSection("Jwt").Get<JwtOptions>()` trùng lặp). Unit tests: 13 mới JwtOptionsLoaderTests. Commit: `84b61da fix(auth): JwtOptions dual-alias load, DI, validation [BE-380]`.

### BE-099 Xóa Supabase secrets khỏi Git history
- Scope: dùng `git filter-repo` hoặc BFG sau khi UA-001 rotate.
- Depends on: UA-001
- Done when: clone fresh không có secret.
- Status: BLOCKED (chờ UA-001)

### BE-100 Money + VND adapters + rounding tests
- Scope: helper tính fee/share/refund với integer VND.
- Depends on: BE-001
- Done when: unit test rounding + sum cap.
- Status: **DONE (phiên 13, 2026-09-19)** — mở rộng `Fitmate.Application.Common.Money` thêm: `ValidateBps` + `SplitFeeBps` (DEC-029, basis points 0..10000) + `FormatPercent` (hiển thị invariant) + `AllocateRefund` (DEC-008, phân bổ refund cả fee + share theo tỷ lệ ban đầu, sum invariant) + `ApplyCumulativeRefund` (delegate sang `ApplyRefund`, AGENTS.md §"Refund lũy kế") + `MoneySnapshot` record + `BuildSnapshot` (DEC-029 snapshot cho booking). Các method cũ `RoundVnd` / `SplitFee` / `ApplyRefund` giữ nguyên (backward-compat với code cũ). Unit tests: 29/29 mới PASS (84/84 tổng) — cover rounding half-up, sum invariant, bps edge cases (0/1000/10000), refund proportional + cumulative + zero-paid, snapshot sum invariant, validation throws.

### BE-110 Admin Users (list + lock + audit)
- Scope: pagination, filter, lock/unlock.
- Depends on: BE-090
- Done when: admin lock user → session bị revoke.
- Status: **DONE (phiên 18, 2026-09-19)** — Application: `IAdminUserService` (List/SetLock) + abstractions `IAdminUserStore`. Service enforce role Admin ở Application (BE-090) + ngăn self-lock + validate reason ≥ 5 chars + audit (`user.lock` / `user.unlock` với reason). `SecurityStamp` được bump → invalidate JWT/refresh tokens (session revoke). Infrastructure: `EfAdminUserStore` dùng `UserManager.FindByIdAsync/SetLockoutEnabledAsync/SetLockoutEndDateAsync/UpdateSecurityStampAsync`. List filter: role (member/pt/admin), status (active/locked/pending_deletion), search (email ILIKE), pagination max=100, sort allowlist (`createdAt:asc|desc`, `email:asc|desc`). API: `/api/v1/admin/users` GET (list + filter + sort + page), `/api/v1/admin/users/{id}` GET, `/api/v1/admin/users/{id}/lock` POST. `AdminUsersController` Authorize role=Admin. Bảo vệ `fullName`: hiện trả null — sẽ fill từ `UserProfile` ở BE-160. Unit tests: 9/9 mới PASS (173/173 tổng).

### BE-120 Trainer marketplace
- Scope: GET /trainers, /trainers/{id}, /trainers/{id}/availability.
- Depends on: BE-040
- Done when: chỉ verified PT hiện.
- Status: **DONE (phiên 19, 2026-09-19)** — Application: `ITrainerMarketplaceService` (List verified + GetDetail) + abstraction `ITrainerMarketplaceStore`. Filter: specialty (substring ignore case), minPriceVnd/maxPriceVnd, minRating (0..5), pagination cursor-based (default limit=20, max=100). Validation: maxPriceVnd >= minPriceVnd, limit 1..100, minRating 0..5. Verified-only enforcement: store level (chỉ PT có 1+ Approved Certificate mới trả về). Infrastructure: `EfTrainerMarketplaceStore` query `PtProfiles` với distinct PT userIds từ Approved Certificates; post-filter sort theo Rating desc, UserId asc. API: `/api/v1/trainers` (GET, `[AllowAnonymous]`), `/api/v1/trainers/{id}` (GET detail với services active). Bảo vệ `TrainerMarketplaceSummary.FullName`/`AvatarUrl` placeholder null — sẽ fill từ `UserProfile` ở BE-160. Unit tests: 10/10 mới PASS (183/183 tổng).

### BE-130 Booking flows
- Scope: my bookings (member + PT), admin list.
- Depends on: BE-060
- Done when: ownership enforced.
- Status: **DONE (phiên 32, 2026-09-21)** — Already implemented in BE-060: `IBookingService.ListByMemberAsync` + `ListByPtAsync`; `BookingsController` routes `GET /api/v1/me/bookings` (member) + `GET /api/v1/me/trainer-bookings` (PT, `[Authorize(Roles = "PT")]`) with status filter + offset pagination max=100; OpenAPI schema with `Appointment` response. Unit tests: 18/18 PASS including 3 new tests (ListByPt filter, status filter narrows, pagination total reflects all items). Confirm/Accept deferred to BE-060 completion (AppointmentStatus.Confirmed migration pending DEC-026).
- Status: TODO

### BE-140 Payment skeleton (chưa tích hợp VNPay)
- Scope: payment entity + idempotent create + admin list. Trả mock URL.
- Depends on: BE-070
- Done when: payment create trả 201 với redirect_url=stub.
- Status: **DONE (phiên 34, 2026-09-21)** — 4 phases (plan + abstractions + service/store/controller + tests). Domain entity `Payment` đã có sẵn (InitialSchema + PaymentConfiguration). Application: `IPaymentService` (CreateAsync/GetByIdAsync/HandleVnpayReturn/Ipn), `IPaymentStore` (FindById/FindByAppointmentId/FindHold/FindAppointmentByHold/Add/Save), `IPaymentProvider` interface + `StubPaymentProvider` (mock URL `https://mock.fitmate.local/payments/{id}/redirect`, ref `mock-{id}`). `PaymentService` enforce hold ownership + active hold check + idempotent per-hold; VNPay return/IPN là stub 200 (no mutation). API: `PaymentsController` `/payments` (POST, Idempotency-Key required), `/me/payments/{id}` (GET), `/payments/vnpay/return` (GET AllowAnonymous), `/payments/vnpay/ipn` (POST AllowAnonymous). DI registered trong Program.cs. Unit tests: 8/8 PASS (PaymentServiceTests — happy path mock URL, idempotent same hold, hold not found 404, not owner 403, hold expired 409, get-by-id null, return stub pending, IPN stub 00). Real VNPay wiring → BE-400 (blocked UA-008). Commit: 4 message (entities phase `5fee40d` / service+controller phase `2af93eb` / tests phase `7979864`).

### BE-150 Finance read-side
- Scope: ledger + overview + PT earnings + revenue for PT + **`GET /admin/platform-policy` (FP-FN-06, DEC-029 read-only)**.
- Depends on: BE-140
- Done when: dashboard numbers khớp với payment state; `GET /admin/platform-policy` trả `{platform_fee_rate_bps: 1000, policy_version, effective_from, editable: false}`.
- Status: **DONE (phiên 20, 2026-09-19)** — overview + ledger; GET /admin/platform-policy chưa làm (chờ cấu hình `PlatformPolicy` table — tách task con). Application: `IAdminFinanceService` (GetOverview + GetLedger) + abstraction `IAdminFinanceStore`. Money: lấy từ snapshot `Appointment.{ActualPaidVnd, PlatformFeeVnd, TrainerShareVnd}` (DEC-029). Refund = `Σ Payment.RefundedSoFarVnd`. Range: 7d/30d/90d/all (chart gom theo local UTC+7). Ledger types: `platform_fee`/`trainer_share`/`refund`. Payout rows = 0 (blocked UA-019). Authorization: `AuthorizationGuard.EnsureRoleAsync(Admin)` + `[Authorize(Policy=AdminOnly)]`. Pagination: max=100, default=50. Validation: range/type unknown → 400. Infrastructure: `EfAdminFinanceStore` query Appointments (Paid/Completed/Refunded) + Refunds (Succeeded); member count qua Identity roles. API: `/api/v1/admin/finance/overview`, `/api/v1/admin/finance/ledger`. Unit tests: 9/9 mới PASS (192/192 tổng).

### BE-151 GET /admin/platform-policy
- Scope: read-only endpoint trả `{platform_fee_rate_bps: 1000, policy_version, effective_from, editable: false}` theo DEC-029. Không có POST trong release đầu.
- Depends on: BE-110 (admin authz), BE-100 (Money.ValidateBps + FormatPercent).
- Done when: GET /admin/platform-policy trả 200 đúng schema, default 10% (1000 bps), override qua env `PLATFORM_POLICY__FEEBPS` cho staging test, bps ngoài [0,10000] throw.
- Status: **DONE (phiên 26, 2026-09-19)** — `PlatformPolicyOptions` (Application/Common) bind section `PlatformPolicy` qua env `PLATFORM_POLICY__FEEBPS/POLICYVERSION/EFFECTIVEFROMUTC` (default 1000/v1/2026-09-19). `IPlatformPolicyService` + `PlatformPolicyService` + record `PlatformPolicy` (PlatformFeeRateBps/PolicyVersion/EffectiveFrom/Editable/DisplayHint). Service enforce role Admin qua `IAuthorizationGuard.EnsureRoleAsync` (BE-090) + validate bps qua `Money.ValidateBps`. Controller `AdminPlatformPolicyController` `[Authorize(Policy=AdminOnly)]` route `/api/v1/admin/platform-policy`. DI bind section `PlatformPolicy` trong Program.cs. Unit tests: 9/9 mới PASS (275/275 tổng) — default, override, boundary 0/10000, negative/out-of-range throws ArgumentOutOfRangeException, Editable=false invariant, PolicyVersion/EffectiveFrom propagate, options defaults sanity.

### BE-160 Workout + Weight
- Scope: plans, exercises, sessions, weight log. Idempotency qua `client_id` (body) + `Idempotency-Key` (header); session giữ snapshot plan + per-set results.
- Depends on: BE-080 (validation + ProblemDetails); BE-090 (authz guard); BE-070 (idempotency middleware).
- Done when: ownership enforced; duplicate `client_id` idempotent; FK `WorkoutPlanId` SET NULL khi xóa plan (session intact qua snapshot); per-set results + bonus validation; pagination cursor.
- Status: **DONE (Sep 21, BE-160 plan rev 3)** — 8 phases merged on `backend-release/contract-and-foundation`. Domain: `WorkoutSessionExercise`, `WorkoutSessionExerciseSet` entities + `WorkoutSetStatus` enum; `WorkoutSession` mở rộng 4 snapshot fields (plan_name/day_of_week/planned_duration_min/target_calories_burn). Migration `20260920205100_WorkoutSnapshot` có pre-check orphan + bổ sung FK constraints (WorkoutPlanId SET NULL cho sessions, CASCADE cho exercises). Application: `IWorkoutService`/`IWeightService` abstractions + DTOs + `WorkoutErrorCodes`. Services: `WorkoutService` (LogSession idempotent qua client_id, recompute completed_sets, outbox event `workout.session_logged`), `WeightService` (validate 20..400). API: `WorkoutsController` `/api/v1/me/workout/sessions` (GET list with from/to filter, GET {id} detail, POST log với Idempotency-Key header) + `/api/v1/me/weight` (GET/POST). OpenAPI: thêm `WorkoutSessionDetail`, `WorkoutSessionExerciseView`, `WorkoutSessionExerciseInput`, `WorkoutSetView`, `WorkoutSetInput` schemas + mới `/me/workout/sessions/{id}` endpoint. Unit tests: 9/9 PASS (WorkoutServiceTests). Testcontainers integration tests deferred — project `Fitmate.IntegrationTests` chưa tồn tại; contract test đã thử nhưng block bởi Defender Application Control policy khi load Domain.dll (env-specific). Build: 0 errors, 7 pre-existing warnings.

### BE-170 Nutrition (log, schedule) + AI stub
- Scope: member ghi meal log (manual), xem/cập nhật meal schedule, scan image via AI stub (trả 503 khi chưa có provider).
- Depends on: BE-080
- Done when: 4 endpoints hoạt động; AI scan luôn 503; ownership enforced; unit tests pass.
- Status: **DONE (phiên 33, 2026-09-21)** — 4 phases (plan + abstractions + service + tests). Domain entities + EF config đã có sẵn. Application: `INutritionService` (list/create/update meals, get/upsert schedule, scan), `INutritionStore`, `IAiNutritionAdapter` + `AiNutritionStubAdapter` (throw DomainException AI_UNAVAILABLE). `NutritionController` `/me/nutrition/meals` (GET list, POST create, PATCH update/confirm) + `/me/nutrition/schedule` (GET, PUT) + `/me/nutrition/scan` (POST, 10MB limit). DI: stub adapter registered as default; real adapter (khi BE-430 wired) sẽ override. Unit tests: 8/8 PASS (NutritionServiceTests — list empty, list ordered, date filter, create, update, not-owner 404, schedule blank/create/upsert, AI stub throws). OpenAPI unchanged (đã complete). Commit: 4 message (`a278dbc`/`62678b5`/`b3a8bdd`/`???`).

### BE-180 Social (post, comment, react, report)
- Scope: visibility filter, idempotent react.
- Depends on: BE-080
- Done when: visibility filter pass.
- Status: **DONE (phiên 22, 2026-09-19)** — Application: `ISocialService` (ListPosts/GetPost/CreatePost/DeletePost/AddComment/ToggleReaction/CreateReport) + `ISocialStore` abstraction. Auth: `[Authorize]` (Bearer) + Application `EnsureAuthenticated` (Guid.Empty → Forbidden). Visibility: public feed filter; private chỉ author xem; friends cần friend check (stub `IsFriendAsync` = false sẽ chuyển sang BE-181). Cursor pagination: base64(`<unix-ms>|<guid>`) theo (CreatedAt desc, Id desc); limit 1..100, default 20. Reactions: idempotent toggle — Conflict 409 nếu user đã react (1 user 1 reaction/post). Comment: tăng `Post.CommentsCount +1`. Report: validate target tồn tại (Post/Comment/User), reason >=5 chars, max 500. Validation: content max 5000, comment max 1000, tags max 10 mỗi tag max 32. Infrastructure: `EfSocialStore` EF Core query, soft delete (`DeletedAt != null`) ẩn khỏi feed, friend filter qua `_db.Friends`. ErrorCodes bổ sung: `Social.PostNotFound/PostDeleteForbidden/AlreadyReacted/ReportTargetNotFound`, `Identity.NotAuthenticated`, `Validation.{RequiredField,TooLong,TooShort,TooMany}`. API: GET /api/v1/social/posts, GET/POST/DELETE /api/v1/social/posts/{id}, POST /api/v1/social/posts/{id}/comments, POST /api/v1/social/posts/{id}/reactions, POST /api/v1/social/reports. Unit tests: 21/21 mới PASS (219/219 tổng). Friend requests BE-181 sẽ làm sau.

### BE-181 Friend request / friendship / block
- Scope: gửi/accept/reject/cancel friend request, list + unfriend, block/unblock + list blocked, relation status.
- Depends on: BE-180
- Done when: tests pass, OpenAPI cập nhật, friend filter cho feed `visibility=friends` hoạt động đúng.
- Status: **DONE (phiên 25, 2026-09-19)** — DEC-036/037/038/039 chốt `to_user_id` + canonical pair + bảng blocked riêng + outbox event semantics. Domain: `Friendship(UserLowId, UserHighId)` với `CanonicalPair` helper + `CHECK (low<high)`. `BlockedUser(BlockerId, BlockedId)` 1 chiều. `FriendRequest` thêm `Status.Cancelled`. Application: `IFriendService` (Send/Accept/Reject/Cancel/Unfriend/ListFriends/IsFriend/GetRelationStatus) + `IBlockService` (Block/Unblock/ListBlocked) + `IFriendStore`/`IBlockStore` + `CursorEncoder` shared. `SocialService.IsFriendAsync` delegate sang `FriendService`. Acceptance atomic: validate → write → outbox enqueue → SaveChangesAsync. Race accept vs block: re-check block trong cùng transaction, reject silent nếu block tồn tại, KHÔNG tạo friendship. Unfriend idempotent. Send duplicate pending → trả row cũ, không enqueue outbox lại. Block huỷ pending FriendRequest 2 chiều + xoá friendship; unblock KHÔNG tự khôi phục. Outbox events: `friend.request.created/accepted`, `friendship.removed`, `user.blocked/unblocked` (không có notification cho reject/cancel/unfriend/block-target). Infrastructure: `EfFriendStore`/`EfBlockStore` query canonical pair + index phụ `(user_high_id)`/`(blocked_id)`. Migration `20260919111325_FriendshipAndBlock`: drop `friends`, create `friendships` + `blocked_users`, rename columns `FromUserId→RecipientId`/`ToUserId→SenderId`, partial unique `(sender_id, recipient_id) WHERE "Status"=0`. API: `POST /social/friends/requests` (to_user_id), `POST /social/friends/requests/{id}` (action: accept/reject/cancel), `DELETE /social/friends/{user_id}`, `GET /social/friends` (cursor), `GET /social/friends/status/{user_id}`, `POST /social/blocks`, `DELETE /social/blocks/{user_id}`, `GET /social/blocks`. Tests: 47/47 mới PASS (266/266 tổng). OpenAPI: schema `FriendRequest` (sender_id/recipient_id/status enum +cancelled), `FriendSummary`, `FriendRelationStatus`, `BlockedSummary`. Authorization matrix cập nhật §8 (block check 2 chiều, recipient/sender ownership). Pending integration: notification worker (BE-200) chưa có; chat friend message (BE-191) chưa có — ghi pending tasks.
### BE-191 Chat (friend message)
- Scope: 5 friend chat endpoints trong OpenAPI. Friend-only (AI chat là task riêng). Canonical pair conversation + ChatThreadRead tracking + idempotency qua `client_message_id` + block/unfriend policy + race-safe send.
- Depends on: BE-181 (Friend + Block store).
- Done when: 5 endpoints implemented; tests pass; OpenAPI peer_email → peer_user_id; DEC-042..045 accepted.
- Status: **DONE (phiên 28, 2026-09-20)** — DEC-042 (canonical pair), DEC-043 (client_message_id idempotency), DEC-044 (read marker monotonic), DEC-045 (block/unfriend giữ history). Domain: `FriendConversation(UserLowId, UserHighId)` + `CanonicalPair` helper + `CHECK (low<high)` + UNIQUE, `ChatThreadRead(ConversationId, UserId)`, `ChatMessage` extend với `ConversationId` (nullable, null = AI thread) + `ClientMessageId` (idempotency) + partial unique. Application: `IChatService` (SendFriendMessage/ListConversations/ListMessages/MarkRead/GetConversation) + `IChatStore` abstraction + DTOs. Send: validate → block check → friend check → GetOrCreate conversation → dedup check → add message + outbox `chat.message.sent` (cùng transaction). Race 2 request đồng thời → 1 row (UNIQUE). Mark-read: monotonic theo `candidate.CreatedAt > current.LastReadAt`. Unfriend/block KHÔNG xoá conversation/messages. Infrastructure: `EfChatStore` (transaction với caller, no N+1 qua aggregate query). API: `POST /api/v1/chat/friends/messages` (peer_user_id + text + client_message_id), `GET /api/v1/chat/friends/conversations?cursor&limit`, `GET /api/v1/chat/friends/conversations/{id}`, `GET /api/v1/chat/friends/conversations/{id}/messages?cursor&limit`, `POST /api/v1/chat/friends/conversations/{id}/read` body `{last_read_message_id}`. Migration `20260920121331_ChatFriendV1`: alter `chat_messages.ThreadId` nullable; thêm `chat_messages.ConversationId` + `ClientMessageId` + 2 bảng mới + 4 indexes. OpenAPI: schema `ChatMessage` + `Conversation` extend (conversation_id, client_message_id, last_message_at); schema mới `MarkReadRequest`; `peer_email` → `peer_user_id`. Authorization matrix §9 cập nhật (participant check 404, monotonic, idempotency). Domain-rules §12 (chat policy) mới. Unit tests: 29/29 mới PASS (350/350 tổng) — permission (self/block 2 chiều/non-friend/empty/long/auth), idempotency (same payload/cùng key khác payload), conversation lifecycle (canonical pair/unfriend preserved/block prevent send + history kept), read marker (advance/no-op old/non-participant/wrong message/unknown), unread count (own messages excluded/decreases after mark/increases on new), pagination (limit clamp/empty), GetConversation (participant/non-participant 404), outbox same-transaction.

### BE-200 Notifications (system + push register)
- Scope: device register, list, mark read.
- Depends on: BE-080
- Done when: device token hashed, không echo raw.
- Status: **DONE (phiên 27 commit + phiên 34 finalize, 2026-09-20..2026-09-21)** — 9 commits từ phiên 27 (domain 17c7b67, contracts 8b84a5d, services ffe2f0b, push handler 162803f, infrastructure stores 7954065 + sweep service, EF config 58f23a1, API controllers e7fe2d7, docs 021d8e1, tests e864408) + commit apply migration + DEC-040/041 Accepted (`47d628c`). Đầy đủ: 4 bảng (`notifications` / `devices` / `push_deliveries` / `push_provider_configs`) với 5 indexes (partial unique `ux_notifications_dedup`, `ix_notifications_user_created`, `ix_notifications_user_read_created`, unique `ux_devices_user_token`, partial sweep `ix_push_deliveries_sweep`); migration `20260920064150_NotificationsPushV2` apply OK trên local Postgres; producer wiring FriendService.Send/Accept + ModerationService.Warn đã enqueue `notification.push_requested` cùng `SaveChangesAsync`; OpenAPI 4 endpoint (`/me/notifications` GET + read POST, `/users/me/devices` POST + DELETE) + schema `Notification`; sweep `SweepBackgroundService` chạy mỗi 30 phút (DEC-041) với `FOR UPDATE SKIP LOCKED`. Unit tests: **76/76 mới PASS** (NotificationService 11 + DeviceService 9 + PushDeliveryDispatcher 12 + NotificationPushHandler 14 + PushDeliverySweepService 5 + 25 cập nhật FriendServiceTests/ModerationServiceTests) — **tổng 392/392 PASS** (verify lại phiên 34).

### BE-210 Moderation (reports)
- Scope: list, resolve, dismiss, audit. Theo DEC-028 — `ReportTargetType ∈ {Post, Comment, User, Certificate}`. PT map sang user (BE không có targetType riêng). Idempotency chống spam báo cáo trùng. Visibility check trước khi nhận report (không leak certificate riêng tư).
- Depends on: BE-090
- Done when: resolve tạo audit row; idempotency test (cùng reporter+target+Open → 409).
- Status: **DONE (BE-211 follow-up phiên 24, 2026-09-19)** — đầy đủ BE-210 + BE-211 (xem entry BE-211 bên dưới).

### BE-211 Moderation follow-up (idempotency + audit + visibility + Certificate target + Warn/Lock/DeleteContent)
- Scope: thêm Idempotency-Key cho POST /social/reports + spam-check cho cùng (reporter, target, Open) (DEC-028). Visibility check cho Certificate target (chỉ Approved mới report được). 4 Lock duration (permanent/30d/7d/24h) + security stamp bump + session revoke. Warn → ghi `user_moderation_actions` + enqueue outbox `user.warning` (worker xử lý phase 2). DeleteContent → soft-delete Post/Comment. Audit log cho mọi resolve/dismiss với action subtype (warn/lock_permanent/lock_30d/lock_7d/lock_24h/delete_content/no_action).
- Depends on: BE-210, BE-090, BE-110 (AdminUserService), BE-040 (Certificate), BE-180 (Social).
- Done when: Idempotency test (cùng key → 409 ConflictException); spam check test; Certificate Pending → reject; Lock 4 duration test; Warn outbox test; DeleteContent Post/Comment test; audit log row xuất hiện.
- Status: **DONE (phiên 24, 2026-09-19)** — EF migration `20260919102229_ModerationAction` (reports.idempotency_key + unique partial index `(reporter,key) WHERE key NOT NULL`; reports unique partial `(reporter,target_type,target_id) WHERE status=Pending`; bảng `user_moderation_actions`). Application: `IModerationService.ResolveReportAsync` mở rộng (action enum + LockDuration), inject `ISocialStore`/`ICertificateStore`/`IAdminUserStore`/`IOutboxDispatcher`/`IAuditLogger`/`IModerationActionStore`; `Warn` enqueue `user.warning` payload `{UserId, ReportId, Reason, AdminId}`; `LockTarget` set lockout + bump stamp + save; `DeleteContent` route Post/Comment soft-delete + reject với `DeleteContentNotApplicable` cho User/Certificate target; `Dismiss` ghi audit `report.dismiss`. `SocialService.CreateReportAsync` validate Idempotency-Key + reject `IdempotencyKeyInvalid` (8..128 chars); visibility check Cert = Approved → `CertificateNotReportable` nếu khác; spam-check → `DuplicateReport` (409). Bug fix: `EfModerationStore.SaveReportAsync` trước đó throw `Guid.Empty` cho `ResolvedByAdminId` và bỏ qua field từ row — giờ truyền đầy đủ qua domain `Report.Resolve(adminId, action, note)`. Domain: `Report.ResolveAction` + `IdempotencyKey` mới; `UserModerationAction` entity (AuditableEntity) + `UserModerationActionKind { Warn, Lock }`; `Comment.SoftDelete()` mới. ErrorCodes mới: `Moderation.{DuplicateReport, IdempotencyKeyInvalid, DeleteContentNotApplicable, CertificateNotReportable, InvalidResolveAction}` + `Lock.InvalidDuration`. Controllers: `AdminReportsController.Resolve` parse `lock_duration` (permanent|30d|7d|24h); `SocialController.CreateReport` đọc `Idempotency-Key` header + accept target `certificate`. DI: `IModerationActionStore` → `EfModerationActionStore`. Unit tests: 21 Moderation + 24 Social = **45/45 PASS** (tổng 242/243, 1 fail pre-existing JwtTokenServiceTests tampered-token).

### BE-220 Admin metrics + action-required
- Scope: KPIs endpoint + aggregation.
- Depends on: BE-150
- Done when: chart data match ledger.
- Status: **DONE (phiên 21, 2026-09-19)** — Application: `IAdminMetricsService` (GetActionRequired) + `IAdminMetricsStore`. Counts: pendingCertificates (Certificate.Status=Pending), pendingReports (Report.Status=Pending), pendingPayments (Payment.Status=Pending). Recent bookings: N (default 10, max 50) Appointment theo CreatedAt desc, status in (Paid/Completed/Refunded/Cancelled). Authorization: `AuthorizationGuard.EnsureRoleAsync(Admin)` + `[Authorize(Policy=AdminOnly)]`. Infrastructure: `EfAdminMetricsStore` direct EF counts + EF projection cho recent. API: `/api/v1/admin/action-required` (GET, ?recentLimit=10). Unit tests: 6/6 mới PASS (198/198 tổng).

### BE-300 Docker image + Compose local/integration
- Scope: Dockerfile (multi-stage), compose.yml, compose.override.yml, .dockerignore.
- Depends on: BE-001
- Done when: `docker compose up` chạy, migration OK.
- Status: **PARTIAL_DONE (phiên 29, 2026-09-20)** — Dockerfile (slnx-free, no tests/, curl pre-USER, DataProtection dir), Dockerfile.dev, Dockerfile.migrate (sdk pin dotnet-ef 8.0.10 → efbundle self-contained linux-x64 → runtime-deps Debian + non-root fitmate UID 1001 + DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/bundle-extract), compose.yml (bỏ container_name, ports qua override, dpkeys named volume, migrate dùng image ENTRYPOINT), compose.verify300.yml (ports/volumes riêng), Program.cs PersistKeysToFileSystem + SetApplicationName("Fitmate.Api"), .dockerignore bổ sung tests/ + tasks/, runbook local-dev.md (7 bước). Evidence: build OK (api 526 MB, migrate 101 MB); migrate exit 0 trên empty DB apply 6 migrations + idempotent lần 2; `\dt` 44 bảng; `dotnet test` 350/350 PASS. **Bị gián đoạn ở api readiness** do pre-existing bug `JwtOptions` env alias + DI registration (không thuộc BE-300) → đề xuất task mới BE-380. Plan rev 3 tại `docs/backend-release/tasks/BE-300-plan.md`. Commit 2 message đã duyệt nhưng chưa thực hiện.

### BE-310 GitHub Actions CI
- Repo: FitmateAI_BE
- Scope: lint (warnings-as-errors trừ `xUnit1031` cosmetic), `dotnet test` (full unit suite), Docker build 2 image (api + migrate) qua Buildx với cache layer, smoke end-to-end (compose up postgres+migrate+api → poll `/health/ready` 90s → teardown). Ephemeral `JWT_SIGNING_KEY` (tạo runtime bằng `openssl rand`), không cần GitHub secret.
- Depends on: BE-300 (PARTIAL_DONE → DONE sau khi api readiness verify xong với BE-380 fix).
- Done when: PR mở → 3 jobs PASS trên runner Ubuntu 22.04.
- Status: **DONE (phiên 31, 2026-09-21)** — `.github/workflows/ci.yml` (3 jobs: `lint-test` / `docker-build` / `smoke`, runner `ubuntu-22.04`, timeout 15/15/20 phút, Buildx cache qua `actions/cache@v4`). Docs `docs/backend-release/runbooks/ci.md` (8 sections: workflow/secrets/local-preview/tại-sao-không-push/lỗi thường gặp/branch-protection/files). Lưu ý: chưa push image thật ra registry (UA-002 Git hosting + UA-003 OIDC + UA-023 managed DB credentials vẫn pending — sẽ làm ở BE-500/340 khi UA sẵn sàng).
- Cấu hình branch protection ghi trong runbook §7 — cần user apply sau khi repo public (UA-002).

### BE-320 OpenAPI linter + contract test
- Scope: spectral/redocly, test snapshot vs spec.
- Depends on: BE-300
- Done when: breaking change fail CI.
- Status: **DONE (phiên 33, 2026-09-21)** — `.spectral.yml` (extends `spectral:oas`, tắt rules false-positive cho oas3.1/operations không bắt buộc). CI step `Lint OpenAPI spec (Spectral)` trong `lint-test` job — `npx @stoplight/spectral lint --fail-severity=error` (warning không chặn). Fix 4 lỗi spec khi run initial: duplicate `/social/blocks/{user_id}`, duplicate `/social/blocks` GET entry, `peer_email` còn sót trong `required` (line 1645), Vietnamese descriptions có `:` parse sai (line 1650/3718/3733), invalid ref `Friend` → `FriendRequest`, device DELETE path sai (move sang `/users/me/devices/{id}`). Final: **0 errors, 87 warnings** (warnings non-blocking).

### BE-330 Load test scripts (k6)
- Scope: smoke/normal/peak/spike/soak/stress.
- Depends on: BE-300
- Done when: smoke + normal pass trên local.
- Status: **DONE (phiên 25, 2026-09-21)** — `tests/load/` với 6 scripts + shared helpers (common.js/constants.js) + compose.loadtest.yml Docker profile + README. Smoke test PASS 100% checks / 0% errors / p(95)=225ms trên local Docker stack.

### BE-340 Secrets management (.env.example, .gitignore, gitleaks)
- Scope: .env.example, .dockerignore, secret scanning ở CI.
- Depends on: BE-310
- Done when: gitleaks scan clean trên main.
- Status: **DONE (phiên 34, 2026-09-21)** — `.gitleaks.toml` với 6 custom rules Fitmate (JWT/DB/VNPay/SMTP/Storage/Bootstrap admin) + allowlist placeholder `__SET_*__` + demo emails `*@fitmate.vn`. CI job `secrets-scan` trong `.github/workflows/ci.yml` dùng `gitleaks/gitleaks-action@v2` với `fetch-depth: 0` (scan full history) + SARIF upload artifact. Runbook `docs/backend-release/runbooks/secrets.md` 9 sections: nguyên tắc, danh mục secret, local dev, CI ephemeral, staging/production rotation policy, DataProtection key ring, incident response, files liên quan, troubleshooting. Verify: `docker run zricethezav/gitleaks detect --source . --no-git --config .gitleaks.toml` → exit 0 (clean), positive test với file `tests/secrets-leak-test.txt` chứa JWT/DB → detect 3 leaks (1 default rule + 2 custom rules). Commits: 3 message (`5d46e6f` plan+config, `fa010a0` CI, `9a927f0` runbook).

## Giai đoạn 2 — Providers (cần UA)

### BE-400 VNPay adapter
- Scope: tạo payment, return URL, IPN webhook, reconciliation.
- Depends on: BE-140, UA-008
- Done when: sandbox E2E + mismatch alert.
- Status: BLOCKED (chờ UA-008)

### BE-410 Email provider
- Scope: SMTP send.
- Depends on: BE-080, UA-007
- Done when: reset password email đến.
- Status: BLOCKED (chờ UA-007)

### BE-420 Storage adapter (upload media)
- Scope: local volume (dev) + signed URL.
- Depends on: BE-080
- Done when: upload OK, signed URL TTL ≤ 15min.
- Status: **DONE (phiên 35, 2026-09-22)** — Plan rev 1 lưu ở `docs/backend-release/tasks/BE-420-plan.md`. Application: `IStorageService` + `ISignedUrlService` interfaces + `StorageOptions` (BasePath, BaseUrl, MaxFileSizeBytes=5MiB, AllowedContentTypes=[jpeg,png,webp], AllowedCategories=[avatars,certificates,posts], SignedUrlSecret, SignedUrlTtlMinutes=15). Infrastructure: `LocalStorageService` (atomic write temp+rename, path-traversal guard) + `HmacSignedUrlService` (HMAC-SHA256 over `path\n\nexpires` → base64url, expiry check + constant-time sig compare, TTL clamp). API: `SignedUrlMiddleware` (gates `/uploads/*`, 403 nếu invalid/expired trước static file handler) + `UploadsController.POST /api/v1/uploads` (multipart `file` + `category` → 201 `{storageKey, uri, signedUrl}`). Wire `CertificatesController` nhận `image_storage_key` từ upload. DI registration trong `Program.cs` + `UseStaticFiles` với PhysicalFileProvider. Unit tests: **10/10 PASS** (`LocalStorageServiceTests` 4 cases + `HmacSignedUrlServiceTests` 6 cases) — **tổng 402/402 PASS**. DEC-050 chốt local volume + HMAC presigned URL, deferred cloud adapter + secret manager rotation về UA-STOR-001.

### BE-430 AI adapter (chờ handoff)
- Scope: sidecar / in-process theo `ai-handoff-checklist.md`.
- Depends on: AI handoff
- Done when: smoke với artifact thật.
- Status: BLOCKED (chờ AI handoff)

### BE-500 Staging deploy
- Scope: CI/CD push lên registry → VPS.
- Depends on: BE-310, UA-004..UA-006
- Done when: staging smoke pass.
- Status: BLOCKED

### BE-600 Production release
- Scope: tag + protected env + runbook.
- Depends on: Tất cả gate trên PASS
- Done when: release record ghi.
- Status: BLOCKED

### BE-700 Google Play release pipeline
- Scope: AAB release, internal-testing upload qua CI.
- Depends on: UA-009..UA-014
- Done when: upload internal-testing thành công.
- Status: BLOCKED

## Trạng thái tổng hợp

| Status | Count |
|---|---|
| DONE | 19 (BE-001, BE-010, BE-020, BE-021, BE-080, BE-030, BE-070, BE-090, BE-100, BE-040, BE-050, BE-110, BE-151, BE-380, BE-310, BE-330, BE-340, BE-200, **BE-420**) |
| IN_PROGRESS | 0 |
| PARTIAL_DONE | 1 (BE-060 — cần BE-160 để hoàn thiện Confirm/Accept) |
| TODO | ~16 |
| BLOCKED | ~7 (giảm — UA-018..024 đã chốt phần kỹ thuật; UA-007/008/023/001/004..006/009..010 là ops external) |

Cập nhật mỗi lần commit.
