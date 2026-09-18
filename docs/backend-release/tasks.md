# Tasks — Backend Release

> Tiến độ các task triển khai backend. ID dạng BE-NNN. Status: TODO / IN_PROGRESS / BLOCKED / DONE / SKIP.

## Giai đoạn 1 — Nền tảng (không cần provider)

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
- Scope: thay `User` bằng `ApplicationUser : IdentityUser<Guid>` + 6 bảng Identity (Roles, UserRoles, UserClaims, RoleClaims, UserTokens, UserLogins). `Program.cs` đăng ký `AddIdentity`. Seed roles Admin/PT/Member.
- Depends on: BE-020. Đã chốt qua DEC-032 (không còn phụ thuộc UA-021/UA-018 cho mục tiêu code; UA-021 chỉ chặn provisioning managed DB).
- Done when: migration tạo 8 bảng Identity (7 bảng Identity + 1 bảng UserProfile), `dotnet ef database update` chạy được.
- Status: **IN_PROGRESS** (bắt đầu ở phiên 7 ngay sau khi user chốt DEC-026..030).

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
- Status: TODO (bắt đầu khi BE-021 DONE).

### BE-040 Trainer + Certificate
- Scope: PT profile, certificate submission, admin approve/reject.
- Depends on: BE-030
- Done when: PT register kèm cert status=pending, admin approve set verified.
- Status: TODO

### BE-050 Availability + Service (PT)
- Scope: PT set availability, CRUD service.
- Depends on: BE-040
- Done when: overlap detection pass, edit service có pending booking trả 409.
- Status: TODO

### BE-060 Booking (hold, confirm, pending→accept, cancel, reschedule, complete)
- Scope: state machine theo `domain-rules.md §3.2` + unique constraint + TTL release. Theo DEC-026 — `late` là flag (BE-135a), `reschedule` qua `booking_reschedule_events` (BE-135). Endpoint accept riêng (`POST /me/bookings/{id}/accept`). Cần check cả `AppointmentStatus` + `PaymentStatus = Paid` mới ghi nhận doanh thu (FP-FN-04).
- Depends on: BE-050
- Done when: concurrency test (10 VU cùng slot) chỉ 1 thành công; reschedule transaction check slot mới trước khi release cũ; idempotency chống report trùng (`reports.idempotency_key`).
- Status: TODO.

### BE-070 Idempotency middleware + Outbox
- Scope: middleware, schema `outbox`, worker.
- Depends on: BE-030
- Done when: retry cùng key → response cũ; khác payload → 409.
- Status: TODO

### BE-080 Validation + ProblemDetails
- Scope: FluentValidation toàn cục, RFC 7807. Theo `api-contract.md §6`.
- Depends on: BE-001
- Done when: validation error trả đúng format; mọi Controller trả ProblemDetails khi 4xx/5xx.
- Status: TODO.

### BE-090 Authorization matrix enforcement
- Scope: spec classes kiểm tra role + ownership + field-level.
- Depends on: BE-030
- Done when: mỗi endpoint admin test 401/403/200.
- Status: TODO

### BE-099 Xóa Supabase secrets khỏi Git history
- Scope: dùng `git filter-repo` hoặc BFG sau khi UA-001 rotate.
- Depends on: UA-001
- Done when: clone fresh không có secret.
- Status: BLOCKED (chờ UA-001)

### BE-100 Money + VND adapters + rounding tests
- Scope: helper tính fee/share/refund với integer VND.
- Depends on: BE-001
- Done when: unit test rounding + sum cap.
- Status: TODO

### BE-110 Admin Users (list + lock + audit)
- Scope: pagination, filter, lock/unlock.
- Depends on: BE-090
- Done when: admin lock user → session bị revoke.
- Status: TODO

### BE-120 Trainer marketplace
- Scope: GET /trainers, /trainers/{id}, /trainers/{id}/availability.
- Depends on: BE-040
- Done when: chỉ verified PT hiện.
- Status: TODO

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
- Status: TODO.

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
- Status: TODO

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
- Status: TODO.

### BE-220 Admin metrics + action-required
- Scope: KPIs endpoint + aggregation.
- Depends on: BE-150
- Done when: chart data match ledger.
- Status: TODO

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
| DONE | 3 (BE-001, BE-010, BE-020) |
| IN_PROGRESS | 1 (BE-021) |
| TODO | ~25 |
| BLOCKED | ~7 (giảm — UA-018..024 đã chốt phần kỹ thuật; UA-007/008/023/001/004..006/009..010 là ops external) |

Cập nhật mỗi lần commit.
