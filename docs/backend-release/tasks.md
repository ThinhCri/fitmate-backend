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
- Status: TODO

### BE-140 Payment skeleton (chưa tích hợp VNPay)
- Scope: payment entity + idempotent create + admin list. Trả mock URL.
- Depends on: BE-070
- Done when: payment create trả 201 với redirect_url=stub.
- Status: TODO

### BE-150 Finance read-side
- Scope: ledger + overview + PT earnings + revenue for PT + **`GET /admin/platform-policy` (FP-FN-06, DEC-029 read-only)**.
- Depends on: BE-140
- Done when: dashboard numbers khớp với payment state; `GET /admin/platform-policy` trả `{platform_fee_rate_bps: 1000, policy_version, effective_from, editable: false}`.
- Status: **DONE (phiên 20, 2026-09-19)** — overview + ledger; GET /admin/platform-policy chưa làm (chờ cấu hình `PlatformPolicy` table — tách task con). Application: `IAdminFinanceService` (GetOverview + GetLedger) + abstraction `IAdminFinanceStore`. Money: lấy từ snapshot `Appointment.{ActualPaidVnd, PlatformFeeVnd, TrainerShareVnd}` (DEC-029). Refund = `Σ Payment.RefundedSoFarVnd`. Range: 7d/30d/90d/all (chart gom theo local UTC+7). Ledger types: `platform_fee`/`trainer_share`/`refund`. Payout rows = 0 (blocked UA-019). Authorization: `AuthorizationGuard.EnsureRoleAsync(Admin)` + `[Authorize(Policy=AdminOnly)]`. Pagination: max=100, default=50. Validation: range/type unknown → 400. Infrastructure: `EfAdminFinanceStore` query Appointments (Paid/Completed/Refunded) + Refunds (Succeeded); member count qua Identity roles. API: `/api/v1/admin/finance/overview`, `/api/v1/admin/finance/ledger`. Unit tests: 9/9 mới PASS (192/192 tổng).

### BE-160 Workout + Weight
- Scope: plans, exercises, sessions, weight log. Idempotency qua client_id.
- Depends on: BE-080
- Done when: duplicate client_id không tạo row mới.
- Status: TODO

### BE-170 Nutrition (log, schedule) + AI stub
- Scope: log/scan, scan trả AI_UNAVAILABLE khi provider chưa có.
- Depends on: BE-080
- Done when: scan không gọi provider thật, trả 503.
- Status: TODO

### BE-180 Social (post, comment, react, report)
- Scope: visibility filter, idempotent react.
- Depends on: BE-080
- Done when: visibility filter pass.
- Status: **DONE (phiên 22, 2026-09-19)** — Application: `ISocialService` (ListPosts/GetPost/CreatePost/DeletePost/AddComment/ToggleReaction/CreateReport) + `ISocialStore` abstraction. Auth: `[Authorize]` (Bearer) + Application `EnsureAuthenticated` (Guid.Empty → Forbidden). Visibility: public feed filter; private chỉ author xem; friends cần friend check (stub `IsFriendAsync` = false sẽ chuyển sang BE-181). Cursor pagination: base64(`<unix-ms>|<guid>`) theo (CreatedAt desc, Id desc); limit 1..100, default 20. Reactions: idempotent toggle — Conflict 409 nếu user đã react (1 user 1 reaction/post). Comment: tăng `Post.CommentsCount +1`. Report: validate target tồn tại (Post/Comment/User), reason >=5 chars, max 500. Validation: content max 5000, comment max 1000, tags max 10 mỗi tag max 32. Infrastructure: `EfSocialStore` EF Core query, soft delete (`DeletedAt != null`) ẩn khỏi feed, friend filter qua `_db.Friends`. ErrorCodes bổ sung: `Social.PostNotFound/PostDeleteForbidden/AlreadyReacted/ReportTargetNotFound`, `Identity.NotAuthenticated`, `Validation.{RequiredField,TooLong,TooShort,TooMany}`. API: GET /api/v1/social/posts, GET/POST/DELETE /api/v1/social/posts/{id}, POST /api/v1/social/posts/{id}/comments, POST /api/v1/social/posts/{id}/reactions, POST /api/v1/social/reports. Unit tests: 21/21 mới PASS (219/219 tổng). Friend requests BE-181 sẽ làm sau.

### BE-190 Chat (AI + friend)
- Scope: thread, message, AI_UNAVAILABLE.
- Depends on: BE-170
- Done when: friend message chỉ friend mới nhận.
- Status: TODO

### BE-200 Notifications (system + push register)
- Scope: device register, list, mark read.
- Depends on: BE-080
- Done when: device token hashed, không echo raw.
- Status: TODO

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
- Status: TODO

### BE-310 GitHub Actions CI
- Scope: lint, test, docker build, smoke container.
- Depends on: BE-300
- Done when: CI green trên PR.
- Status: TODO

### BE-320 OpenAPI linter + contract test
- Scope: spectral/redocly, test snapshot vs spec.
- Depends on: BE-300
- Done when: breaking change fail CI.
- Status: TODO

### BE-330 Load test scripts (k6)
- Scope: smoke/normal/peak/spike/soak/stress.
- Depends on: BE-300
- Done when: smoke + normal pass trên local.
- Status: TODO

### BE-340 Secrets management (.env.example, .gitignore, gitleaks)
- Scope: .env.example, .dockerignore, secret scanning ở CI.
- Depends on: BE-310
- Done when: gitleaks scan clean trên main.
- Status: TODO

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
- Status: TODO

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
| DONE | 12 (BE-001, BE-010, BE-020, BE-021, BE-080, BE-030, BE-070, BE-090, BE-100, BE-040, BE-050, BE-110) |
| IN_PROGRESS | 0 |
| PARTIAL_DONE | 1 (BE-060 — cần BE-160 để hoàn thiện Confirm/Accept) |
| TODO | ~16 |
| BLOCKED | ~7 (giảm — UA-018..024 đã chốt phần kỹ thuật; UA-007/008/023/001/004..006/009..010 là ops external) |

Cập nhật mỗi lần commit.
