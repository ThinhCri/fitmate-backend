# Migration Strategy

> Chiến lược di chuyển schema và data an toàn từ BE Spring Boot + Supabase cũ sang BE .NET + self-hosted Postgres.

## 1. Nguyên tắc

1. **Expand → migrate data → contract**: thêm cột/bảng mới trước, migrate data, đợi code mới dùng, rồi mới drop cũ.
2. **Mỗi migration có up + down rõ ràng**.
3. **Không drop/reset DB** không phải test.
4. **Backup trước mỗi migration có rủi ro** (column drop, data type change).
5. **Production migration chạy qua job riêng** (tool `dotnet ef database update` qua Compose job), không phải container chính; idempotent.
6. **Rollback app không mặc định đảo ngược được schema/data**. Plan rollback cho mỗi bước.

## 2. Phase 1 — BE mới song song BE cũ (nếu cần)

Vì BE cũ Spring Boot chỉ là scaffold (Auth + 1 endpoint) và **chưa có user thật ngoài demo**, ta không cần chạy song song lâu. Phase 1 chủ yếu:

1. Khởi tạo Postgres self-hosted mới.
2. Tạo schema với EF Core migration đầu tiên.
3. Seed demo data phù hợp (`user@fitmate.vn` / `pt@fitmate.vn` / `123456`).
4. Không migrate từ Supabase (vì không có dữ liệu thật cần giữ).

## 3. Phase 2 — Destructive changes

Vì không có dữ liệu cũ cần giữ, ta có thể:
- Drop schema cũ sau khi verify seed hoạt động.
- Hoặc chỉ cần thay BE Spring Boot bằng BE .NET, schema mới hoàn toàn.

## 4. EF Core migration workflow

```bash
# Trong container BE (sau khi Compose lên):
dotnet ef migrations add InitialSchema --project src/Fitmate.Infrastructure --startup-project src/Fitmate.Api
dotnet ef database update
```

Compose chạy migration qua job riêng (`compose.migrate.yml`):

```yaml
services:
  migrate:
    image: ${BE_IMAGE}
    command: ["dotnet","ef","database","update","--no-build"]
    env_file: .env
    depends_on:
      postgres:
        condition: service_healthy
    profiles: ["migrate"]
```

Chỉ chạy khi `docker compose --profile migrate up migrate`. App container không tự chạy migration.

## 5. Seed

- `dotnet run --project src/Fitmate.Api -- seed --env Development` cho local/test.
- Không seed admin.
- Seed demo chỉ khi `ASPNETCORE_ENVIRONMENT=Development` hoặc `--seed-demo` flag.
- Không commit fixture có dữ liệu user thật.

## 6. Rollback

- Mỗi migration có `Down`. Trong production rollback, ta chạy `dotnet ef database update <previous>` trong maintenance window.
- Rollback app version về image trước.
- Có backup trước mỗi lần apply.

## 7. Verify

Sau migration, chạy smoke:
- Health check.
- 1 login demo user.
- 1 endpoint public.
- Count row ở mỗi bảng chính khớp expectation.

Kết quả log + commit SHA + tag trong `release-checklist.md`.
