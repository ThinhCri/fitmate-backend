# Fitmate Backend — Release Notes & Source of Truth

> Tài liệu nguồn chuẩn cho hệ thống backend Fitmate. Mọi thay đổi API, schema, nghiệp vụ phải bắt đầu ở đây trước khi cập nhật implementation và frontend.

## Vai trò của repo này

Repo này là **single source of truth** cho:
- API contract (OpenAPI 3.1) — phục vụ cả Android app và Admin Web.
- Domain rules (booking, money, time, role, ownership).
- Authorization matrix.
- Data model + migrations.
- CI/CD, deployment, runbook.
- Quyết định sản phẩm & kỹ thuật (DEC-).

## Stack (đã chốt với user)

| Hạng mục | Lựa chọn | Lý do |
|---|---|---|
| Runtime | .NET 8 LTS | User chốt; phù hợp self-host, EF Core ecosystem tốt. |
| Web framework | ASP.NET Core Web API | Chuẩn .NET 8. |
| ORM | EF Core 8 + Npgsql | Migration có kiểm soát, không dùng `update` tự động. |
| Database | PostgreSQL 16 (self-hosted, Docker Compose) | User chốt self-host, không còn phụ thuộc Supabase. |
| Auth | JWT bearer do .NET cấp + BCrypt password | Custom JWT có kiểm soát, không vendor lock. |
| Payment | VNPay sandbox (giai đoạn 2) | User chốt. |
| Storage | Local volume + signed URL (giai đoạn 2) | Self-host. |
| Realtime | Không dùng cho MVP | Có thể thêm sau khi có nhu cầu thật. |
| Deploy | Docker Compose trên 1 VPS | User chốt; không K8s ở giai đoạn này. |

## Nguyên tắc bắt buộc

1. **Không commit secrets.** Mọi secret đi qua `.env` (local) hoặc secret manager (CI/prod). Repo chỉ có `.env.example`.
2. **Không dùng fake payment / fake AI trong production.** Adapter phải reject khi cấu hình sai.
3. **Tiền là integer VND.** Không dùng floating point cho nghiệp vụ tài chính.
4. **Timestamp lưu UTC.** API trả UTC + timezone sản phẩm (Asia/Ho_Chi_Minh).
5. **API versioning** theo URL path (`/api/v1/...`). Không phá contract cũ.
6. **Authorization kiểm tra ở server**, không phụ thuộc FE ẩn nút.
7. **Mọi endpoint admin**: filter/sort/pagination ở server; allowlist fields; giới hạn page size.
8. **Không có admin default password.** Bootstrap an toàn qua secret manager.
9. **Không commit dữ liệu thật** (user, health, chat, payment) dưới bất kỳ định dạng nào.

## Cấu trúc thư mục tài liệu

```
docs/backend-release/
├── README.md                       # file này
├── architecture.md                 # kiến trúc & module boundaries
├── feature-api-matrix.md           # Feature → API → Test matrix
├── domain-rules.md                 # quy tắc nghiệp vụ (booking, money, time, role)
├── authorization-matrix.md         # bảng quyền theo vai trò + field-level
├── decisions.md                    # DEC-001.. quyết định kiến trúc/sản phẩm
├── data-model.md                   # ERD + ràng buộc + chỉ mục
├── migrations.md                   # chiến lược expand → migrate → contract
├── api-contract.md                 # quy ước API (versioning, error, pagination, idempotency, upload)
├── openapi/
│   └── fitmate-v1.yaml             # OpenAPI 3.1 spec — nguồn chuẩn
├── android-integration-guide.md    # hướng dẫn cho FE Android
├── admin-integration-guide.md      # hướng dẫn cho Admin Web
├── ai-contract.md                  # đề xuất contract cho nhóm AI bàn giao
├── ai-handoff-checklist.md         # checklist khi nhận gói AI
├── capacity-plan.md                # mô hình tải + giả định
├── load-test-report.md             # kết quả đo (chưa có khi mới tạo)
├── deployment-runbook.md           # staging/prod deploy
├── runbooks/                       # incident response
│   ├── be-down.md
│   ├── db-failover.md
│   ├── payment-anomaly.md
│   └── secret-leak.md
├── release-checklist.md            # gate trước khi release
├── user-actions.md                 # việc user + nhóm AI phải làm
├── tasks.md                        # tiến độ các task backend
└── handoff.md                      # điểm đang dừng, việc tiếp theo
```

## Quy trình thay đổi

Mỗi thay đổi tuân theo chu kỳ:
1. Cập nhật `docs/backend-release/<file liên quan>` (đặc biệt `openapi/fitmate-v1.yaml`, `domain-rules.md`, `decisions.md`).
2. Cập nhật implementation.
3. Cập nhật tests + matrix.
4. Commit local riêng từng phần.
5. Cập nhật `handoff.md`.

## Trạng thái hiện tại

- **2026-09-18**: Khởi tạo docs nền tảng. BE Spring Boot cũ sẽ được thay thế bằng .NET 8 + Postgres self-hosted.
- Implementation: chưa (BE-001..BE-099 tasks).
- Tests: chưa.
- Staging: chưa.
- Production: **KHÔNG** tự deploy.

## Local dev workflow

Một lần duy nhất:

```bash
cp .env.example .env
# Sửa DB_PASSWORD (dev), JWT_SIGNING_KEY (>= 32 bytes base64) trong .env
docker compose up -d postgres
dotnet tool install -g dotnet-ef
```

Hằng ngày:

```bash
# Apply EF migrations (load env tự động từ .env)
powershell -ExecutionPolicy Bypass -File ./scripts/ef-update.ps1

# Hoặc các action khác:
powershell -ExecutionPolicy Bypass -File ./scripts/ef-update.ps1 -Action migrations   # list pending/applied
powershell -ExecutionPolicy Bypass -File ./scripts/ef-update.ps1 -Action script      # generate idempotent SQL
powershell -ExecutionPolicy Bypass -File ./scripts/ef-update.ps1 -Action info        # DbContext info

# Run API
dotnet run --project src/Fitmate.Api

# Tests
dotnet test
```

> Script `scripts/ef-update.ps1` đọc `.env` (fallback `.env.example`), set env vars (`DB_HOST`, `DB_PORT`, `DB_NAME`, `DB_USER`, `DB_PASSWORD`), rồi gọi `dotnet ef` tương ứng. Mặc định `DB_HOST=localhost` cho local dev (postgres container publish port 5432). Trong docker compose, đổi `DB_HOST=postgres`.
