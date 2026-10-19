# AGENTS.md — Fitmate Backend (.NET 10)

## Production baseline — đọc trước mọi task mới
Đọc docs/backend-release/README.md và business-requirements.md trước danh sách đọc bên dưới.
Baseline PROD-2026-09-22-v1 / DEC-051 thay các chỉ dẫn cũ mâu thuẫn. tasks.md BE-800..829 là backlog production; DONE lịch sử không là release approval.
Solution hiện dùng Fitmate.slnx; project build có thể nhắm csproj trực tiếp. Không giả SDK hỗ trợ định dạng solution nếu chưa kiểm.
Docker named volume giữ dữ liệu qua container recreation; không “reset bằng xóa container”, không xóa volume user để chữa test.
Mỗi endpoint test các status có ý nghĩa; không tạo403/409 giả chỉ để đủ checklist.
Task tài liệu không tự commit; tuân thủ rule duyệt message. Không đổi code đang dở của user.

> Quy tắc cho AI agents và developers khi làm việc trên repo FitmateAI_BE.

## Vai trò của repo

Repo này là **backend chính** cho cả Android app và Admin Web. Stack hiện tại: **.NET 10 + ASP.NET Core + EF Core + Npgsql + PostgreSQL**; kiểm `global.json` và các project file trước khi đổi SDK.

### Môi trường chạy

| Môi trường | PostgreSQL | Đặc điểm |
|---|---|---|
| **Local dev** | Self-hosted qua Docker Compose (`compose.yml`, user `fitmate`, password `dev`) | Dữ liệu nằm trong named volume và vẫn tồn tại khi recreate/xóa container. Chỉ reset database test cô lập theo runbook; không xóa volume chứa dữ liệu user. |
| **Staging** | Môi trường tách biệt (chốt khi có budget) | Tách biệt credentials/dữ liệu khỏi production. |
| **Production (baseline)** | **Managed** DigitalOcean PostgreSQL Singapore, primary 2 vCPU / 4 GiB + 1 standby HA (chưa có read replica) | `sslmode=require` bắt buộc. Credentials qua secret manager — KHÔNG commit. Xem `decisions.md` DEC-030 và `managed-postgres-comparison.md`. |

**Không tự ý tạo tài nguyên trả phí**. Mọi thao tác lên managed DB chờ user/operator cấp credentials (UA-023).

Tài liệu nguồn chuẩn: [`docs/backend-release/README.md`](docs/backend-release/README.md).

## Trước khi sửa code, BẮT BUỘC đọc theo thứ tự

1. [`docs/backend-release/tasks.md`](docs/backend-release/tasks.md) — task đang chạy, ID cần thao tác.
2. [`docs/backend-release/handoff.md`](docs/backend-release/handoff.md) — điểm dừng, việc đã làm, tiếp theo.
3. [`docs/backend-release/decisions.md`](docs/backend-release/decisions.md) — quyết định đã chốt.
4. [`docs/backend-release/domain-rules.md`](docs/backend-release/domain-rules.md) — quy tắc nghiệp vụ (booking, money, time).
5. [`docs/backend-release/authorization-matrix.md`](docs/backend-release/authorization-matrix.md) — bảng quyền.
6. [`docs/backend-release/openapi/fitmate-v1.yaml`](docs/backend-release/openapi/fitmate-v1.yaml) — API contract.
7. [`docs/backend-release/api-contract.md`](docs/backend-release/api-contract.md) — quy ước API.
8. [`docs/backend-release/feature-api-matrix.md`](docs/backend-release/feature-api-matrix.md) — feature → API → test matrix.

## Nguyên tắc bắt buộc

- **Không commit secrets.** Repo chỉ có `.env.example`. Secret vào `.env` (local) hoặc secret manager (CI/prod).
- **Không tự ý rewrite Git history** (filter-repo, BFG, force-push) khi chưa được user cho phép.
- **Không tự deploy production.** Chuẩn bị pipeline; deploy chỉ khi user/operator duyệt.
- **Tiền là integer VND**, không floating point.
- **Timestamp UTC + timezone riêng** khi cần.
- **Authorization enforce ở Application layer**, không chỉ Controller.
- **Mỗi endpoint admin**: filter/sort/pagination ở server; allowlist fields; giới hạn page size.
- **Idempotency-Key bắt buộc** cho mọi mutation có side effect tài chính.
- **Outbox** cho mọi side effect bất đồng bộ.
- **Không commit `.env`, `*.pfx`, `*.keystore`, `appsettings.*.json` thật, dump DB.**
- **Mỗi migration có `Up` và `Down` rõ ràng**. Migration destructive cần backup + rollback plan.
- **Không seed admin password mặc định.** Bootstrap qua CLI.
- **Không fake payment / fake AI trong production.** Adapter phải fail khi cấu hình sai. Xem `decisions.md` DEC-033.
- **Mỗi endpoint có test**: success, validation error, unauthorized, forbidden, not found, conflict.
- **Money rounding half-up đồng gần nhất.** Tính lũy kế cho refund nhiều lần. Tổng refund ≤ actual_paid.

## Quy ước đặt tên

- Task ID: `BE-NNN`.
- Solution: `Fitmate.slnx`.
- Projects:
  - `src/Fitmate.Api/` — ASP.NET Core entry, controllers, DI composition root.
  - `src/Fitmate.Application/` — use cases, abstractions.
  - `src/Fitmate.Domain/` — entities, value objects, domain events.
  - `src/Fitmate.Infrastructure/` — EF Core, adapters, outbox worker.
  - `tests/Fitmate.UnitTests/`
  - `tests/Fitmate.IntegrationTests/`
  - `tests/Fitmate.ContractTests/`
  - `tests/load/` — k6 scripts.

## Commit

- Write a single English imperative subject of at most 72 characters. Do not include a commit body or trailers.
- For new commits by the repository owner, use author and committer email `votanthinhcri28@gmail.com`. Preserve other contributors' attribution in existing history.
- Do not add `Co-authored-by` trailers, AI attribution, or tool-generated signatures.
- Preserve existing task IDs in the subject; do not invent task IDs or truncate words or brackets when shortening messages.
- Inspect the final message after committing. History rewrites require explicit user authorization for the specific scope.
- Conventional Commits: `<type>(<scope>): <desc> [BE-NNN]`
- Không push/merge/amend/reset --hard.
- Không commit secrets.
- Không commit file rỗng.
- Mỗi commit là một đơn vị có ý nghĩa (không gom cả release vào 1 commit).

## Chạy local

```bash
cp .env.example .env
# Sửa .env (DB password, JWT key, ...)

# Build + run
dotnet restore
dotnet build
dotnet ef database update --project src/Fitmate.Infrastructure --startup-project src/Fitmate.Api
dotnet run --project src/Fitmate.Api

# Test
dotnet test

# Docker Compose (alternative)
docker compose up postgres
docker compose --profile migrate up migrate
docker compose up api
```

## Liên hệ nhanh

- Source of truth: `docs/backend-release/README.md`.
- Tasks: `docs/backend-release/tasks.md`.
- Handoff: `docs/backend-release/handoff.md`.
- Runbooks: `docs/backend-release/runbooks/`.
- User actions: `docs/backend-release/user-actions.md`.

Cuối mỗi phiên, cập nhật `docs/backend-release/handoff.md`.
