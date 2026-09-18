# Fitmate Backend

> Backend .NET 8 cho hệ thống Fitmate (Android app + Admin Web).

## Bắt đầu

- **Tài liệu nguồn chuẩn**: [docs/backend-release/README.md](docs/backend-release/README.md)
- **API contract (OpenAPI 3.1)**: [docs/backend-release/openapi/fitmate-v1.yaml](docs/backend-release/openapi/fitmate-v1.yaml)
- **AGENTS.md cho AI agents**: [AGENTS.md](AGENTS.md)

## Stack

- .NET 8 LTS + ASP.NET Core Web API
- EF Core 8 + Npgsql
- PostgreSQL 16 self-hosted (Docker Compose)
- JWT custom (HS512) + BCrypt password
- Serilog structured logging

## Trạng thái hiện tại

- 2026-09-18: Repo Spring Boot cũ đã thay thế bằng .NET 8. Đang ở giai đoạn scaffold docs + triển khai từng task theo [docs/backend-release/tasks.md](docs/backend-release/tasks.md).
- Xem [docs/backend-release/handoff.md](docs/backend-release/handoff.md) để biết việc tiếp theo.

## Lưu ý

- **KHÔNG commit secrets.** Chỉ có `.env.example`. Cấu hình thật đi qua `.env` (local) hoặc secret manager (CI/prod).
- **Supabase credentials cũ** (trong `src/main/resources/application.properties` của Spring Boot scaffold) đã được rotate nhưng cần xóa khỏi Git history. Xem DEC-014 + UA-001.
