# Handoff — Backend Release

> Điểm đang dừng + việc tiếp theo. Cập nhật cuối mỗi phiên.

## Phiên hiện tại

- **Ngày**: 2026-09-18
- **Phiên**: Khởi tạo docs nền tảng backend release + scaffold .NET 8 solution.
- **Commits trong phiên**:
  - BE `ff95843` — `docs(be): foundation — source-of-truth docs, OpenAPI v1, .env.example, Docker Compose skeleton [BE-099-partial]` (32 files, 6542 insertions).
  - BE `0e40dcc` — `docs(be): handoff — phase 1 complete (foundation)`.
  - BE `04e7541` — `feat(be): scaffold .NET 8 solution + 28 domain entities + EF Core schema + Money adapters + 14 unit tests PASS [BE-001, BE-010, BE-020, BE-080, BE-100]` (70 files, 6735 insertions).
  - FE `bb8207f` — `docs(fe): backend integration pointer + mobile/.env.example [INT-MOB-prep]` (3 files).
  - Web `76766f8` — `docs(web): backend integration pointer + .env.example [INT-WEB-prep]` (3 files).

## Đã làm

- Khảo sát 3 repo, xác minh cấu trúc thực tế.
- Chốt quyết định sản phẩm với user:
  - Stack: .NET 8 + ASP.NET Core + EF Core + Npgsql + Postgres self-hosted.
  - Payment: VNPay sandbox.
  - Deploy: Docker Compose trên VPS.
  - Contract-first cho cả 2 frontend.
- Tạo branch `backend-release/*` ở cả 3 repo.
- Tạo bộ docs nền tảng trong `docs/backend-release/` của BE repo (32 file).
- OpenAPI spec hợp lệ: 69 endpoints, 53 schemas, 16 tags.
- Tạo `.env.example`, `.dockerignore`, `.gitignore` chuẩn .NET.
- Tạo `compose.yml` + `compose.override.yml` skeleton.
- Tạo `AGENTS.md` mới cho BE repo.
- Tạo pointer + `.env.example` cho FE repo và Web repo (không đụng working tree của user).
- **Scaffold .NET 8 solution thành công**:
  - Solution: `Fitmate.slnx` (5 projects: Domain, Application, Infrastructure, Api, UnitTests).
  - **28 domain entities** với business invariants (User, Certificate, Appointment, Payment, Outbox, Post, Reaction, ChatThread, WorkoutSession, MealLog, Notification, Device, Report, ...).
  - **EF Core 8 + Npgsql**: 12 configuration files mapping 28 entities → 25 bảng Postgres.
  - **Initial migration** `20260918145647_InitialSchema` tạo toàn bộ schema.
  - **Application adapters**: Money (SplitFee half-up, ApplyRefund cap), Clock (UTC↔timezone), PasswordHasher (BCrypt), TokenHasher (SHA256).
  - **Program.cs**: DI wiring, ProblemDetails (RFC 7807), Health checks (live + ready với DB check), Exception handler.
  - **Dockerfile** multi-stage (build SDK 8.0 → runtime ASP.NET 8.0), non-root user, healthcheck.
  - **DatabaseHealthCheck** cho /health/ready.
  - **14 unit tests PASS** cho Money helpers (rounding + fee split + refund cap).
  - Build: 0 warning, 0 error.
  - Tests: 14/14 PASS.
- Working tree của user giữ nguyên ở cả 3 repo.

## Việc cần làm tiếp

### Phiên sau (khi user xác nhận)

- **BE-030 Auth**: JWT custom (HS512), BCrypt verify, register/login/refresh/logout + endpoints + tests.
- **BE-070 Idempotency middleware + Outbox worker** (BackgroundService).
- **BE-060 Booking**: state machine + unique constraint + TTL release.
- **BE-080 Validation**: FluentValidation toàn cục.
- **BE-090 Authorization**: filter/policy cho role + ownership.
- **BE-110..220**: Admin endpoints.
- **BE-300 Docker Compose**: fix Dockerfile.dev reference, validate compose up.
- **BE-310 CI**: GitHub Actions (lint + test + docker build).
- **BE-320 OpenAPI linter**: spectral.
- **BE-330 Load test scripts**: k6 smoke trước.
- **FE INT-MOB-001..005**: HTTP client + secure storage + AuthContext real.
- **Web INT-WEB-001..003**: HTTP client + migrate 7+5 modules.

## Working tree

- BE: 5 commits mới trên branch `backend-release/contract-and-foundation`. Working tree clean.
- FE: 1 commit mới trên branch `backend-release/mobile-integration-guide`. Working tree có 2 file unstaged của user.
- Web: 1 commit mới trên branch `backend-release/admin-integration-guide`. Working tree có 27 file unstaged của user.

## Tests đã chạy

- Validate YAML cho OpenAPI: PASS.
- Unit tests `MoneyTests`: 14/14 PASS.
- `dotnet build` solution: 0 warning, 0 error.
- Migration `dotnet ef migrations add InitialSchema`: PASS (chưa apply vì chưa có Postgres local).

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

## Việc user / nhóm AI

- Xem `user-actions.md` để biết tất cả UA-001..UA-017.
- Bắt đầu từ UA-001 (rotate Supabase) và UA-002 (cấp Git hosting).

## Bước tiếp theo (kế hoạch phiên sau)

1. Viết BE-030 Auth (JWT + register/login/refresh/logout + tests).
2. Viết BE-070 Idempotency middleware + Outbox worker.
3. Viết BE-060 Booking với concurrency test (10 VU cùng slot chỉ 1 thành công).
4. Chạy `docker compose up postgres` + `dotnet ef database update` để verify schema apply được.
5. Chạy k6 smoke test.
