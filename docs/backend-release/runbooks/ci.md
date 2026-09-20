# CI (GitHub Actions) — Fitmate Backend

> BE-310. Phạm vi: lint + test + docker build + smoke container trên PR / push lên `main` hoặc `backend-release/*`.
> KHÔNG push image ra registry — đó là BE-500 staging deploy + BE-340 secrets. CI chỉ chứng minh build/run OK.

## 1. Workflow file

`.github/workflows/ci.yml` — 3 jobs, mỗi job gate độc lập:

| Job | Trigger | Mục đích |
|---|---|---|
| `lint-test` | PR + push | `dotnet restore` + `dotnet build` (warnings-as-errors, trừ `xUnit1031` cosmetic) + `dotnet test`. Upload TRX artifact. |
| `docker-build` | Sau khi `lint-test` PASS | Build 2 image (`api`, `migrate`) bằng Buildx + cache layers qua `actions/cache`. Không push. |
| `smoke` | Sau khi `docker-build` PASS | `docker compose up postgres` → wait healthy → run `migrate` job → `up api` → poll `/health/ready` 90s → teardown. |

## 2. Khi nào workflow chạy

```yaml
on:
  push:
    branches: [main, 'backend-release/*']
  pull_request:
    branches: [main, 'backend-release/*']
```

- **Mọi PR** vào `main` / `backend-release/*`: chạy `lint-test` + `docker-build` + `smoke`. Branch protection nên yêu cầu `lint-test` PASS trước khi merge.
- **Push lên `main` hoặc `backend-release/*`**: chạy đầy đủ.

## 3. Secrets & ephemeral data

Smoke job generate JWT signing key ephemeral qua `openssl rand -base64 64`, ghi vào `.env` tạm trong workspace. Job cleanup step xóa `.env` và `docker compose down -v` ở cuối. **Không cần GitHub secret** để chạy smoke.

Khi BE-500 / BE-340 deploy thật, lúc đó cần:
- `JWT_SIGNING_KEY` (production 64-byte base64)
- `DB_HOST` / `DB_PASSWORD` / `DB_SSLMODE` cho managed Postgres
- Container registry credentials (qua OIDC — UA-003)

## 4. Local preview trước khi push

Nếu muốn chạy workflow local (tùy chọn, không bắt buộc cho mọi PR):

```bash
# Yêu cầu: Docker + Act (https://github.com/nektos/act)
winget install nektos.act   # Windows
brew install act             # macOS

# Act cần biết Docker socket trên Windows:
$env:DOCKER_HOST = "npipe:////./pipe/dockerDesktopLinuxEngine"

# Trước khi chạy: tạo actrc pre-select image + platform
New-Item -Path "$env:USERPROFILE\AppData\Local\act\actrc" -ItemType File -Force
Set-Content -Path "$env:USERPROFILE\AppData\Local\act\actrc" -Value @"
--container-architecture linux/amd64
-P ubuntu-22.04=node:20-bookworm-slim
"@

# Chạy từng job:
act -j lint-test
act -j docker-build
act -j smoke
```

**Lưu ý đã verify (phiên 31):**
- `act -j lint-test -n` (dry-run) PASS — act phát hiện đầy đủ 6 step (Checkout / Setup .NET / Restore / Build / Test / Upload artifact), công nhận triggers + secrets + env mapping đúng.
- `act -j lint-test` (real run) PASS 4/6 step rồi FAIL vì thiếu system lib `libicu` trong `node:20-bookworm-slim` (act default image). Đây là expected limitation của Act local — GitHub runner thật dùng image Ubuntu 22.04 đầy đủ có sẵn libicu + curl.
- Khi apply lên GitHub Actions thật, workflow chạy không cần thêm system libs vì `ubuntu-22.04` runner có sẵn.
- Nếu muốn Act local chạy đến bước `dotnet test` cần image chứa libicu — không khuyến nghị vì tốn effort, chỉ verify trên GH Actions thật.

Act emulate Ubuntu runner bằng Docker container — cần ≥ 4 GB RAM cho `lint-test` + `docker-build`. `smoke` cần Docker daemon đang chạy.

## 5. Tại sao chỉ làm CI chứ chưa push image

- **UA-002** (Git hosting) chưa xác nhận repo đã public trên GitHub.
- **UA-003** (OIDC CI) chưa có credentials cho container registry.
- **BE-500 / BE-340** chưa có staging VPS / managed Postgres / DataProtection secret manager (DEC-030 baseline).

CI chỉ chứng minh build + run OK trên môi trường sạch. Khi các UA sẵn sàng, BE-500 sẽ extend workflow thêm:
1. `docker login` qua OIDC.
2. Tag image `<short-sha>` + digest.
3. Push lên registry.
4. SSH tới VPS, pull + restart container.
5. Verify `/health/ready` từ bên ngoài.

## 6. Lỗi thường gặp

| Triệu chứng | Nguyên nhân thường gặp | Cách gỡ |
|---|---|---|
| `lint-test` fail với `warning CS8602/CS8604` | Nullability chưa chuẩn | Fix code (không tắt nullability). |
| `lint-test` fail với `NU1603 Microsoft.Extensions.Hosting.Abstractions 8.0.10 was not found` | Pin version drift | Bump package version trong csproj. |
| `docker-build` fail tại `dotnet ef migrations bundle` | EF Core version drift | Pin đúng 8.0.10 trong Dockerfile.migrate. |
| `smoke` timeout tại `Wait for api readiness` | Bug runtime, thường do config / DI | Đọc `docker compose logs api` trước khi fail. |
| `smoke` timeout tại `Wait for postgres healthy` | Postgres init fail (volume cũ / lock) | Re-run với `docker compose down -v` trước. |

## 7. Cấu hình branch protection (recommend)

Sau khi repo public trên GitHub (UA-002), Settings → Branches → Branch protection rules → `main`:

- ✅ Require status checks to pass before merging
- ✅ Require branches to be up to date before merging
- Search checks: `Lint and test`, `Docker build (api + migrate)`, `Smoke (compose up + readiness)`
- ✅ Require linear history (không merge commit, rebase trước)
- ✅ Do not allow bypassing the above settings

## 8. Files

| File | Vai trò |
|---|---|
| `.github/workflows/ci.yml` | Workflow định nghĩa 3 jobs. |
| `src/Fitmate.Api/Dockerfile` | Build api image (BE-300). |
| `src/Fitmate.Api/Dockerfile.dev` | Dev override (hot reload). |
| `Dockerfile.migrate` | Build migrate bundle image (BE-300). |
| `compose.yml` | Service definitions. |
| `compose.override.yml` | Local-dev port bindings (commit khi có). |
| `compose.verify300.yml` | Verify-recipe port + volume overrides. |
| `.env.example` | Template env — KHÔNG commit giá trị thật. |
| `.dockerignore` | Loại `tests/`, `tasks/` khỏi build context. |
