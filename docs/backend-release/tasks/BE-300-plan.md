# BE-300 — Docker image + Compose local/integration — Plan & Scope (rev 3)

> Phiên 29 dự kiến (2026-09-20). rev 3 sau khi user duyệt 4 điều chỉnh.
> Plan là checklist sống — KHÔNG sửa khi đang code trừ khi user duyệt.

## 1. Goal

Bộ Docker asset chạy end-to-end cho **local development + integration test**:
- `postgres:16-alpine` (data + healthcheck)
- `mailhog` (SMTP local)
- `api` (Fitmate.Api image, multi-stage build, non-root user, healthcheck, DataProtection key ring persistent)
- `migrate` (one-shot job chạy EF migration **bundle** self-contained trên runtime image Debian, không cần SDK/dotnet-ef trên host lẫn SDK trong runtime image)

Prereq: Docker Engine 24+ / Docker Desktop + Compose v2. **KHÔNG yêu cầu .NET SDK trên host.**

## 2. Phát hiện kiểm tra thực tế

| # | Vấn đề | Xử lý |
|---|--------|-------|
| 1 | Repo root có `Fitmate.slnx` (XML solution mới của .NET 8), KHÔNG có `Fitmate.sln`. Dockerfile `COPY Fitmate.sln` sẽ fail. | rev 3: bỏ COPY solution (xem §3.1). |
| 2 | Dockerfile thừa `COPY tests/` + restore UnitTests khi runtime chỉ cần API. | Bỏ khỏi Dockerfile. |
| 3 | EF Core 8.0.10 → `dotnet-ef` phải pin 8.0.10 trùng version. | Pin trong Dockerfile. |
| 4 | App đọc DB_HOST / DB_PORT / DB_NAME / DB_USER / DB_PASSWORD qua `builder.Configuration` (Program.cs dòng 67–71) → bundle từ `-s src/Fitmate.Api` tự pick env. | KHÔNG thêm `--connection` trong bundle command. |
| 5 | `JwtOptions.SigningKey` map env `JWT_SIGNING_KEY` → `Jwt:SigningKey`. `.env.example` đã dùng đúng tên. | Runbook hướng dẫn generate và đặt đúng. |
| 6 | DEC-030 đã chốt "Local: PostgreSQL Docker Compose" → không cần DEC mới. | |
| 7 | BE-330 = load tests (k6) → không gán backup/restore vào BE-330. | rev 2 đã sửa. |
| 8 | `container_name` cố định `fitmate-postgres` / `fitmate-api` / `fitmate-mailhog` / `fitmate-migrate` → 2 stack dev sẽ xung đột. | rev 3 bỏ `container_name` để Compose tự đặt `<project>-<service>-<n>`. Verify dùng `--project-name`. |
| 9 | DataProtection key ring lưu ephemeral → recreate container mất khả năng giải mã Push token. | rev 3: persistence LOCAL vào scope (`PersistKeysToFileSystem` + named volume + non-root writable). |

## 3. Scope

### 3.1. Dockerfile (api runtime)

- Bỏ hẳn `COPY Fitmate.sln` / `COPY Fitmate.slnx` và restore theo csproj trực tiếp.
- Stage build chỉ copy `src/` (Domain → Application → Infrastructure → Api theo project ref) và restore `src/Fitmate.Api/Fitmate.Api.csproj` (dotnet tự resolve project refs).
- Stage build KHÔNG copy `tests/`.
- Stage runtime: install `curl` trước `USER fitmate`, dọn apt cache.
- Stage runtime: tạo dir `/home/fitmate/.aspnet/DataProtection-Keys` + chown cho `fitmate:fitmate` (sẽ mount named volume lên đây).
- ENTRYPOINT + HEALTHCHECK như cũ.

### 3.2. Dockerfile.migrate (mới)

- Stage 1 (sdk:8.0):
  - COPY src/ + (không cần solution).
  - Pin `dotnet-ef` version 8.0.10 qua `dotnet tool install --global dotnet-ef --version 8.0.10`.
  - `dotnet ef migrations bundle --self-contained --runtime linux-x64 -p src/Fitmate.Infrastructure -s src/Fitmate.Api -o /out/efbundle`.
    - `--self-contained`: bundle độc lập với runtime image.
    - `--runtime linux-x64`: bundle cho glibc Debian/Ubuntu, KHÔNG dùng musl.
- Stage 2 (mcr.microsoft.com/dotnet/runtime-deps:8.0-bookworm-slim, Debian):
  - `USER fitmate`.
  - COPY `--from=build /out/efbundle /app/efbundle`.
  - `chmod +x /app/efbundle`.
  - EXPOSE không cần.
  - HEALTHCHECK KHÔNG — job one-shot.
  - ENTRYPOINT `["/app/efbundle"]`.
  - Working dir `/app` (efbundle tự load config từ `-s` mapping).

### 3.3. Compose (rev 3 verify-safe)

- Bỏ `container_name` cố định → Compose tự đặt tên theo project prefix.
- Bỏ `version` (Compose v2 không cần).
- `api`:
  - Build context `.`, dockerfile `src/Fitmate.Api/Dockerfile`.
  - Volume named `fitmate-dpkeys:/home/fitmate/.aspnet/DataProtection-Keys` (name cố định vì path mount phải khớp).
  - Ports `127.0.0.1:8080:8080`.
  - Env `ASPNETCORE_ENVIRONMENT=Development`, `DB_HOST=postgres`, `DB_PORT=5432`, `DB_SSLMODE=prefer`, `FeatureFlags__PushEnabled=false` cho local (để smoke test không bị message "push dispatcher stale" trong log).
  - `depends_on: postgres: condition: service_healthy`.
- `migrate`:
  - Build context `.`, dockerfile `Dockerfile.migrate`.
  - BỎ `command:` (image ENTRYPOINT chạy bundle).
  - Bỏ `container_name` (Compose tự đặt).
  - `profiles: ["migrate"]`.
  - `env_file: .env` cho JWT_SIGNING_KEY và các env khác (efbundle không cần nhưng env vẫn load — không sao).
  - Env `DB_HOST=postgres`, `DB_PORT=5432`, `DB_NAME=...`, `DB_USER=...`, `DB_PASSWORD=...`, `DB_SSLMODE=prefer`.
  - `depends_on: postgres: condition: service_healthy`.
  - `restart: "no"`.
- `postgres`:
  - Bỏ `container_name`.
  - Volume named `fitmate-postgres-data:/var/lib/postgresql/data`.
  - Ports `127.0.0.1:5432:5432`.
- `mailhog`:
  - Bỏ `container_name`.
  - Ports `127.0.0.1:1025:1025`, `127.0.0.1:8025:8025`.
- Volumes (cuối file):
  - `fitmate-postgres-data` (name cố định — verify dùng project khác).
  - `fitmate-dpkeys` (name cố định).

### 3.4. override (dev hot reload — rev 3)
- `api`: dùng `Dockerfile.dev`, mount `./src:/src/src:ro`, env `DOTNET_USE_POLLING_FILE_WATCHER=true`, command `dotnet watch run --project src/Fitmate.Api/Fitmate.Api.csproj`.
- KHÔNG mount DataProtection vào override (host filesystem không tương thích).

### 3.5. .dockerignore
- Bổ sung `tests/` (sau khi Dockerfile đã bỏ COPY tests).
- Bổ sung `tasks/` (BE-300 plan không cần trong image).

### 3.6. Program.cs — DataProtection LOCAL persistence
- Đăng ký `AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo("/home/fitmate/.aspnet/DataProtection-Keys")).SetApplicationName("Fitmate.Api")`.
- Set ApplicationName giúp api + worker dùng chung key ring (không cần share volume cho worker nếu workers chưa deploy; phase 2 BE-340 sẽ chốt).
- Verify: protect 1 payload trước recreate → unprotect sau recreate vẫn OK.
- Bảo vệ production key qua secret manager/certificate → ghi DEC-??? phase 2 (BE-340 secrets management). Local volume KHÔNG coi là giải pháp bảo mật production hoàn chỉnh — ghi rõ trong runbook.

### 3.7. Runbook `docs/backend-release/runbooks/local-dev.md` (new)
- Prereq: Docker Engine 24+ / Docker Desktop + Compose v2. KHÔNG yêu cầu .NET SDK.
- Bước 0 (one-time per repo): `cp .env.example .env` → edit `DB_PASSWORD`, `JWT_SIGNING_KEY` (= `openssl rand -base64 64`).
- Bước 1: `docker compose build`.
- Bước 2: `docker compose up -d postgres mailhog` → wait `docker compose ps` STATE=healthy.
- Bước 3: `docker compose --profile migrate up migrate` → exit 0 (re-run = "no migrations applied"). Exit non-zero → xem `docker compose logs migrate`.
- Bước 4: `docker compose up -d api` → poll `curl -fsS http://127.0.0.1:8080/health/ready` (timeout 60s).
- Bước 5: Browse http://127.0.0.1:8025 — chỉ verify Mailhog chạy, KHÔNG verify email backend tích hợp.
- Cleanup: `docker compose down --volumes` (xóa fitmate-postgres-data + fitmate-dpkeys nếu cần reset).
- Verify recipe (tách biệt): `docker compose -p fitmate-verify300 -f compose.yml -f compose.verify300.yml up -d` với override port riêng để không đụng stack local.

### 3.8. Deferred với task ID thực tế

Từ `tasks.md`:
- **BE-310** GitHub Actions CI: lint, test, docker build, smoke container.
- **BE-320** OpenAPI linter + contract test.
- **BE-330** Load test scripts (k6). KHÔNG gán backup/restore cho BE-330 (đúng rule user).
- **BE-340** Secrets management (.env.example, .gitignore, gitleaks). DataProtection prod key / secret manager có thể chốt ở BE-340.

Các deferred CHƯA có task ID — cần tạo mới sau khi user duyệt:
- **BE-350** Auto-migrate on startup với pg_advisory_lock (chỉ khi nhiều api instance) — KHÔNG bắt buộc.
- **BE-360** Backup / restore runbook (volume PostgreSQL + DataProtection keys + WAL archive).

## 4. Verification plan (tách biệt hoàn toàn)

Mỗi verify dùng **Compose project riêng + cổng host riêng + volume riêng**:
- Project: `fitmate-verify300` (qua `--project-name`).
- File override: `compose.verify300.yml` map ports `127.0.0.1:18080:8080`, `127.0.0.1:18025:8025`, `127.0.0.1:11025:1025`, `127.0.0.1:15432:5432`.
- Volume: `verify300-postgres-data` (đặt qua override).
- Volume: `verify300-dpkeys` (đặt qua override).
- `.env` clone từ `.env.example` với giá trị mới + `JWT_SIGNING_KEY=$(openssl rand -base64 64)`.

Cú pháp:
```
docker compose -p fitmate-verify300 -f compose.yml -f compose.verify300.yml --profile migrate run --rm migrate
docker compose -p fitmate-verify300 -f compose.yml -f compose.verify300.yml up -d api
docker compose -p fitmate-verify300 -f compose.yml -f compose.verify300.yml down -v
```

Bước kiểm chứng:

| # | Hành động | Expect |
|---|---------|--------|
| 1 | `docker compose -p fitmate-verify300 build` | 0 error, 2 image `fitmate-verify300-api` + `fitmate-verify300-migrate`. |
| 2 | up postgres+mailhog | 2 container up, healthy. |
| 3 | `run --rm migrate` (volume verify300 trống) | exit 0; `\dt` show ~35 bảng. |
| 4 | `run --rm migrate` lần 2 | exit 0, "No migrations were applied. Database is already up to date." |
| 5 | `docker compose exec postgres psql -U fitmate -d fitmate -c "\dt"` | danh sách bảng đầy đủ. |
| 6 | `up -d api` → poll `/health/ready` 60s | 200. |
| 7 | DataProtection verify: | |
|    | a. `docker compose exec api bash -c 'echo -n "hello" \| dotnet Fitmate.Api.dll --protect-token hello 2>/dev/null \|\| true'` — không có CLI. Phải dùng cách khác. | |
|    | **Cách thực tế**: viết integration script PowerShell/Bash gọi 1 endpoint admin tạm (ví dụ `POST /admin/test/protect` chỉ có khi `ASPNETCORE_ENVIRONMENT=Development`) — ĐÂY LÀ OUT OF SCOPE (code mới). | |
|    | **Cách thực tế chấp nhận được**: dùng notification device register (BE-200) end-to-end: POST `/api/v1/users/me/devices` với token ngẫu nhiên → token được encrypted vào DB → recreate api → query DB column `EncryptedToken` rồi Decrypt thông qua internal hook (cần thêm endpoint test) — OUT OF SCOPE. | |
|    | **Cách thực tế (rev 3)**: smoke test thủ công — login app mobile/admin web → push token register → kill container api → recreate → token vẫn register/unregister OK vì key ring mount qua named volume. Ghi nhận evidence thủ công (log + curl). | |
|    | **Cách verify tự động**: thêm 1 test endpoint DEV-ONLY `/internal/dev/protect-roundtrip` chỉ build khi `Environment=Development` → protect 1 string → trả ciphertext → recreate → gọi `/unprotect` → compare plaintext. Đây là test infrastructure, không phải production endpoint. **OUT OF SCOPE rev 3** — quyết định dùng cách verify bằng evidence thủ công + add TODO for BE-340. | |
| 8 | `docker compose exec api id` | `uid=1001(fitmate) gid=1001(fitmate)`. |
| 9 | `docker compose exec api touch /home/fitmate/.aspnet/DataProtection-Keys/.write-test` | OK. `docker compose exec api rm /home/fitmate/.aspnet/DataProtection-Keys/.write-test`. |
| 10 | `curl -fsS http://127.0.0.1:8025/` (port verify) | 200 HTML Mailhog. |
| 11 | `docker compose config 2>&1` (KHÔNG chạy trong CI; chỉ check local có leak?) — Compose **không mask** secret; document. | runbook cảnh báo. |
| 12 | `docker compose -p fitmate-verify300 down` (không -v) | containers xóa, volume verify300-postgres-data + verify300-dpkeys CÒN. `docker compose -p fitmate-verify300 up -d api` lại | data còn. |
| 13 | `docker compose -p fitmate-verify300 down -v` | xóa cả volume verify300 → reset. |
| 14 | `dotnet test` host (chạy local, không qua docker) | ghi số PASS thực tế, không cố định. |

**Quan trọng về DoD DataProtection (rev 3)**: DoD KHÔNG chấp nhận "recreate → mất khả năng giải mã" → bắt buộc key ring qua named volume + `PersistKeysToFileSystem` + `chmod` cho non-root. Verify bằng evidence thủ công ghi trong handoff (xem bước 7): recreate container → register 1 device token mới thành công → query DB thấy ciphertext → recreate + recreate lần nữa → đăng ký device khác vẫn OK. **Bằng chứng giải mã round-trip sau recreate chỉ kiểm chứng được khi có test endpoint / cách tự động — ghi pending cho task test infrastructure** (không thuộc BE-300).

## 5. Files & changes

| File | Thay đổi |
|------|----------|
| `src/Fitmate.Api/Dockerfile` | Bỏ COPY solution + COPY tests; install curl + cleanup; mkdir + chown DataProtection dir. |
| `Dockerfile.migrate` (new) | sdk:8.0 stage (pin dotnet-ef 8.0.10 + bundle self-contained linux-x64) → runtime-deps:8.0-bookworm-slim stage (efbundle + non-root). |
| `compose.yml` | Bỏ container_name cố định; bind ports `127.0.0.1`; volumes named ở root; depends_on healthy. |
| `compose.verify300.yml` (new, optional commit) | Override ports + volume names cho verify. Commit vì tái sử dụng. |
| `src/Fitmate.Api/Program.cs` | `AddDataProtection().PersistKeysToFileSystem(...).SetApplicationName("Fitmate.Api")`. |
| `.dockerignore` | Bổ sung `tests/`. |
| `docs/backend-release/runbooks/local-dev.md` (new) | Quickstart 7 bước + cleanup + verify recipe. |
| `docs/backend-release/tasks.md` | BE-300 → DONE (sau khi verify). |
| `docs/backend-release/handoff.md` | Phiên 29 entry — chỉ evidence thực. |

## 6. Risks

- **EF Core / dotnet-ef version drift**: pin 8.0.10 ở Dockerfile (test). Nếu repo bump EF → bump Dockerfile cùng.
- **Bundle RID**: build trên host Windows/Mac ARM → bundle phải `--runtime linux-x64` để chạy trong container Debian. Nếu team on Apple Silicon, có thể cần `--runtime linux-arm64` + Debian arm64 image.
- **Bundle exit non-zero trong image**: Compose run --rm propagate exit code → CI có thể dùng exit để gate. Build đảm bảo `/app/efbundle` có quyền exec + non-root.
- **DataProtection secret production**: BE-300 chỉ lo local. Production secret manager / certificate rotation → BE-340.
- **`.dockerignore` đổi `tests/`** có thể vô tình loại file test resource cần thiết cho bundle — kiểm tra bundle build không tham chiếu file nào trong `tests/` (EF Core migrations không tham chiếu). OK.
- **Mac Docker Desktop**: bind mount `./src` cho dev override chậm — known issue, không fix.
- **Compose port hosting**: dev verify dùng `127.0.0.1:18080` v.v. tránh đụng cổng thật. Phải có compose.verify300.yml.

## 7. Commit plan (đã duyệt)

1. `chore(docker): fix builds and migration workflow [BE-300]`
2. `docs(docker): document local setup and verification [BE-300]`

## 8. Definition of Done

- `docker compose -p fitmate-verify300 build` OK.
- DB trống → migrate exit 0; re-migrate idempotent exit 0.
- `up -d api` → `/health/ready` 200 trong 60s.
- DataProtection key ring qua named volume, non-root writable.
- UID 1001 trong container api.
- Mailhog UI port 8025 mở được (chỉ chứng minh Mailhog chạy).
- Runbook đủ zero-to-running theo 7 bước + verify recipe tách biệt.
- Handoff ghi evidence thực tế (số tests PASS, command + output quan trọng). KHÔNG cố định số.
- KHÔNG đánh DONE BE-300 trước khi hết verify.
- Commit messages duyệt, không AI trailers.

## 9. Deferred (task ID rõ ràng từ tasks.md)

- BE-310 GitHub Actions CI.
- BE-320 OpenAPI linter + contract test.
- BE-330 Load test scripts (k6) — KHÔNG gán backup/restore.
- BE-340 Secrets management (gitleaks + DataProtection prod key + secret manager).

## 10. Pending — chưa có task ID

- **BE-350**: Auto-migrate on startup với pg_advisory lock (chỉ khi multi-instance api) — không bắt buộc, migration job vẫn hợp lệ.
- **BE-360**: Backup/restore runbook (PostgreSQL volume + DataProtection keys).
- **BE-370**: Test infrastructure để verify DataProtection key ring tự động (dev-only endpoint / contract test).
