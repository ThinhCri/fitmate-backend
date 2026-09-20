# Local development & integration test with Docker Compose

> BE-300. Phạm vi: local dev + integration test. KHÔNG dùng cho production — xem DEC-030 + UA-023 cho managed Postgres / secret manager.

## Prerequisites

- **Docker Engine 24+** hoặc **Docker Desktop** với **Compose v2** (`docker compose` không phải `docker-compose`).
- **KHÔNG yêu cầu .NET SDK** trên host. Toàn bộ build + run diễn ra trong container.
- Tài khoản có quyền chạy Docker (Linux: thuộc group `docker`; macOS/Windows: đã cấp quyền cho Docker Desktop).
- Dung lượng trống ≥ 5 GB cho image build + volume Postgres.

## 1. Chuẩn bị `.env`

```bash
cp .env.example .env
```

Sửa các biến **bắt buộc** trong `.env`:
- `DB_PASSWORD` — bất kỳ chuỗi nào >= 8 ký tự. Compose sẽ fail nếu để placeholder (`__SET_LOCAL_PASSWORD__`).
- `JWT_SIGNING_KEY` — generate bằng:
  ```bash
  openssl rand -base64 64
  ```
  Copy output (≥ 64 bytes base64) vào biến. Code dùng `JwtOptionsLoader` (BE-380) hỗ trợ cả `JWT_SIGNING_KEY` (canonical, như trong .env.example) và `Jwt__SigningKey` (ASP.NET Core alias) — dùng 1 trong 2 đều được. Không đặt chuỗi yếu như `123456`.

Các biến khác trong `.env.example` đã có default hợp lý cho local — để nguyên nếu chưa cần override.

## 2. Build images

```bash
docker compose build
```

Build 2 image:
- `<project>-api` — Fitmate.Api multi-stage, non-root, `curl` cho healthcheck, DataProtection key dir pre-created.
- `<project>-migrate` — EF Core migration bundle, self-contained, runtime-deps Debian.

Lần đầu build mất thời gian tải base image (~10 phút tuỳ mạng). Không phải SLA — chỉ là mục tiêu.

## 3. Khởi động Postgres + Mailhog

```bash
docker compose up -d postgres mailhog
docker compose ps      # chờ STATE=healthy (postgres ~3-5s)
```

`pg_isready` healthcheck của service postgres phải PASS trước khi api/migrate chạy.

## 4. Apply migrations (one-shot)

```bash
docker compose --profile migrate up migrate
```

- Exit code `0` = áp dụng hoặc không có migration mới (idempotent).
- Exit non-zero = **không** chạy tiếp bước 5; xem `docker compose logs migrate`.
- Re-run an toàn: bundle kiểm tra `__EFMigrationsHistory` rồi báo "No migrations were applied. The database is already up to date.".

## 5. Khởi động API

```bash
docker compose up -d api
```

Poll readiness (timeout 60s):
```bash
for i in $(seq 1 60); do
  curl -fsS http://127.0.0.1:8080/health/ready && break
  sleep 1
done
```

- `/health/ready` trả 200 chỉ khi `DatabaseHealthCheck` (`Fitmate.Infrastructure.Persistence.DatabaseHealthCheck`) pass — nghĩa là app **không chỉ HTTP server chạy** mà còn kết nối được DB.
- `/health/live` trả 200 chỉ khi HTTP server up — dùng cho Kubernetes/Compose restart loop.

Container chạy với UID **1001** (`fitmate`). Verify:
```bash
docker compose exec api id               # uid=1001(fitmate)
docker compose exec api touch /home/fitmate/.aspnet/DataProtection-Keys/.write-test \
  && docker compose exec api rm /home/fitmate/.aspnet/DataProtection-Keys/.write-test
```

## 6. Mailhog UI

Mở browser: <http://127.0.0.1:8025>.

Chỉ chứng minh Mailhog service chạy. **Không** chứng minh email backend đã tích hợp đúng — phải tự trigger 1 flow email rồi kiểm tra inbox trong UI.

## 7. Cleanup

```bash
docker compose down                  # dừng containers, giữ volume
docker compose down -v               # kèm volumes (reset DB + DataProtection keys)
```

## Tách biệt stack verify (không đụng stack local đang chạy)

Dùng Compose project riêng + port host riêng + volume riêng:

```bash
docker compose -p fitmate-verify300 \
  -f compose.yml -f compose.verify300.yml \
  build

docker compose -p fitmate-verify300 -f compose.yml -f compose.verify300.yml up -d postgres mailhog
docker compose -p fitmate-verify300 -f compose.yml -f compose.verify300.yml --profile migrate run --rm migrate
docker compose -p fitmate-verify300 -f compose.yml -f compose.verify300.yml up -d api

curl -fsS http://127.0.0.1:18080/health/ready

docker compose -p fitmate-verify300 -f compose.yml -f compose.verify300.yml down -v
```

Stack này listen port **18080** (API), **18025** (Mailhog UI), **11025** (SMTP), **15432** (Postgres) — không xung đột với stack mặc định.

## Known limitations

- DataProtection key ring lưu local Docker volume. **KHÔNG** coi là giải pháp bảo mật production — production key phải qua secret manager / certificate + rotation procedure (BE-340 secrets management).
- Mac Docker Desktop mount `./src` cho dev override chậm. Workaround: dùng compose stack thường (không override dev) và publish image sau mỗi thay đổi source.
- Self-contained bundle target `linux-x64` (glibc). Nếu host là Apple Silicon, bundle vẫn chạy được trong container `linux-x64`, nhưng phải cấu hình Docker qemu emulation.

## Bảo mật local

- `.env` đã có trong `.gitignore`. **Không commit**.
- `docker compose config` không mask secret mặc định; KHÔNG paste output vào PR/chat. Ghi nhận rủi ro.
- `JWT_SIGNING_KEY` chỉ dùng local. Production = secret manager (UA-023).
