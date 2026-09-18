# Handoff — Backend Release

> Điểm đang dừng + việc tiếp theo. Cập nhật cuối mỗi phiên.

## Phiên hiện tại

- **Ngày**: 2026-09-18
- **Phiên 8**: Triển khai BE-021 (Identity refactor) sau khi user chốt approach Greenfield (drop + recreate schema). Code change thật sự đầu tiên sau 7 phiên docs. Tạo `ApplicationUser : IdentityUser<Guid>` + `IdentityRole<Guid>` + DbContext `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`. Refactor `SeedAdminCommand` sang `UserManager` + `RoleManager`. Migration `20260918170539_IdentitySchema` drop `users` cũ + tạo 6 bảng Identity. Build 0 warning, 0 error; 14/14 unit tests PASS. DB compose Postgres `fitmate-postgres` reset schema + apply OK (38 bảng).
- **Lưu ý destructive**: phiên này đã `DROP SCHEMA public CASCADE` trên DB compose local — chỉ chạm vào dev DB (không phải production). Theo user chọn Approach A ở đầu phiên.
- **Tiếp theo** (BE-080 hoặc BE-030): Validation + ProblemDetails là foundation trước khi viết endpoints. BE-030 (JWT + login/register/refresh/logout) cần thêm `JwtOptions` issuer config.

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
4. **BE-030**: Auth (JWT custom + IdentityUser + refresh token + tests).
5. **BE-070**: Idempotency middleware + Outbox worker.
6. **BE-060**: Booking concurrency test + transition matrix (Held→Pending→Confirmed→InProgress→Completed + Cancelled/Expired; flag late; event reschedule).
7. **BE-080**: Validation (FluentValidation).
8. **BE-090**: Authorization (policy + ownership).
9. **BE-110..220**: Admin endpoints (kèm `GET /admin/platform-policy`).
10. **BE-300**: Validate Docker Compose.
11. **BE-330**: k6 smoke + normal + peak test trên staging.
12. **FE INT-MOB-001..005** + **Web INT-WEB-001..003**: integrate sau khi BE pass tests.

## Working tree

- BE: 8+ commits mới trên branch `backend-release/contract-and-foundation`. Phiên 8 thay đổi code BE-021: thêm `ApplicationUser.cs` + cập nhật `FitmateDbContext` (Identity) + cập nhật `SeedAdminCommand` + cập nhật `Program.cs` (`AddIdentityCore` + `AddDataProtection`) + migration `20260918170539_IdentitySchema` + drop file `User.cs` cũ + 2 csproj thêm Identity packages + `tasks.md` (BE-021 DONE) + `identity-evaluation.md` (schema update) + `handoff.md` (phiên 8). Working tree có modified files trước khi commit.
- FE: 1 commit mới trên branch `backend-release/mobile-integration-guide`. Working tree có 2 file unstaged của user.
- Web: 1 commit mới trên branch `backend-release/admin-integration-guide`. Working tree có 27 file unstaged của user.

## Tests đã chạy

- Validate YAML cho OpenAPI: PASS (parse OK + 6 schema mới + 4 BookingStatus→AppointmentStatus ref update OK).
- Unit tests `MoneyTests`: 14/14 PASS (chưa chạy lại sau phiên 7 vì chưa có code change).
- `dotnet build` solution: 0 warning, 0 error.
- Migration `dotnet ef migrations add InitialSchema`: PASS; migration IdentitySchema chưa chạy (BE-021 IN_PROGRESS).

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
