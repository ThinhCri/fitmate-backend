# Fitmate Backend — Kiến trúc

> Nguyên tắc: **Modular monolith trên ASP.NET Core 8**, PostgreSQL self-hosted, Docker Compose. Không microservices, không K8s, không Redis/cache trừ khi đo được bottleneck thật.

## 1. Tổng quan

```
┌─────────────────┐         ┌─────────────────┐
│  Android App    │         │  Admin Web SPA  │
│ (Expo / RN)     │         │  (Vite + React) │
└────────┬────────┘         └────────┬────────┘
         │ HTTPS/JWT                 │ HTTPS/Session-or-JWT
         ▼                            ▼
┌─────────────────────────────────────────────────────┐
│        ASP.NET Core 8 Web API (single process)       │
│ ┌──────────────────────────────────────────────┐    │
│ │ Modules (vertical slices):                    │    │
│ │  • Identity       (auth, users, profile)      │    │
│ │  • Trainers       (PT profiles, certificates) │    │
│ │  • Booking        (slots, appointments)       │    │
│ │  • Payment        (VNPay adapter, refunds)    │    │
│ │  • Finance        (ledger, platform fee)      │    │
│ │  • Workout        (plans, sessions, logs)     │    │
│ │  • Nutrition      (meal log, scan)            │    │
│ │  • Social         (posts, comments, reports)  │    │
│ │  • Chat           (AI companion, friends)     │    │
│ │  • Notifications  (system, reminder)          │    │
│ │  • Moderation     (admin reports & actions)   │    │
│ │  • Admin          (dashboards, audit)         │    │
│ └──────────────────────────────────────────────┘    │
│ ┌──────────────────────────────────────────────┐    │
│ │ Cross-cutting:                                │    │
│ │  • Auth/JWT, RBAC                              │    │
│ │  • Validation, ProblemDetails (RFC 7807)      │    │
│ │  • OpenAPI, health, metrics, structured logs  │    │
│ │  • Money (VND integer), Time (UTC + TZ)       │    │
│ │  • Idempotency, Outbox                        │    │
│ └──────────────────────────────────────────────┘    │
└─────────────────┬───────────────────────────────────┘
                  │ EF Core / Npgsql
                  ▼
           ┌──────────────────┐
           │ PostgreSQL 16    │
           │ (self-hosted)    │
           └──────────────────┘
```

## 2. Phân lớp (layered trong monolith)

| Lớp | Trách nhiệm | Quy ước |
|---|---|---|
| `Api` | Controllers, request/response DTOs, validation, ProblemDetails | Không chứa logic nghiệp vụ. |
| `Application` | Use cases (commands/queries), orchestration, idempotency | Phụ thuộc `Domain` + abstractions của infra. |
| `Domain` | Entities, value objects, domain events, invariants | Không phụ thuộc EF Core / ASP.NET. |
| `Infrastructure` | EF Core DbContext, repositories, adapters (payment, storage, AI), outbox | Phụ thuộc `Application` abstractions. |

Quy tắc:
- Domain không reference EF Core hay ASP.NET.
- Infrastructure reference Application abstractions, không ngược lại.
- Controllers chỉ map DTO ↔ Command/Query, không gọi repo trực tiếp.

## 3. Module boundaries (Bounded contexts)

Mỗi bounded context có:
- Aggregate root riêng (vd: `Booking`, `Payment`, `Certificate`).
- Schema/namespace riêng trong Postgres (vd: `booking.appointments`, `payment.transactions`).
- Không join chéo schema trong query production. Liên kết qua id + Application service.
- Tests độc lập.

Các context:
- `identity` (users, profile, sessions)
- `trainers` (PT, certificates, services, availability)
- `booking` (appointments, slots, holds)
- `payment` (transactions, refunds, ledger)
- `finance` (read-only projections)
- `workout` (plans, exercises, sessions, logs)
- `nutrition` (meal log, schedule)
- `social` (posts, comments, reactions, reports)
- `chat` (AI companion, friend chat)
- `notifications`
- `moderation`
- `admin`

## 4. Cross-cutting concerns

### 4.1 Auth & RBAC

- JWT bearer do BE cấp, access token ngắn hạn (15 phút), refresh token 30 ngày lưu DB với hash + device info.
- Roles: `member`, `pt`, `admin`. PT có thêm flag `isVerified` và `certificateStatus`.
- Authorization kiểm tra ở Application layer (không chỉ ở Controller). Field-level (vd: PT chỉ đọc booking của mình) enforce qua spec.

### 4.2 Validation

- FluentValidation cho mọi DTO đầu vào.
- Trả `ProblemDetails` (RFC 7807) với `errors` map theo field.
- Validation không phụ thuộc DB (khi cần lookup, làm ở Application service).

### 4.3 Error model

- Mã lỗi ổn định dạng `FITMATE.<DOMAIN>.<REASON>` (vd: `FITMATE.AUTH.INVALID_CREDENTIALS`, `FITMATE.BOOKING.SLOT_TAKEN`).
- HTTP status theo chuẩn REST.
- Mọi response lỗi có `traceId` để khớp log.

### 4.4 Idempotency

- Endpoint mutation (POST/PUT/DELETE) yêu cầu header `Idempotency-Key` cho hành động có side effect tài chính.
- Lưu key + payload hash + response snapshot trong DB, TTL 24h.
- Retry cùng key + payload khác → trả 409 `FITMATE.IDEMPOTENCY.CONFLICT`.

### 4.5 Outbox

- Domain event → Outbox row cùng transaction DB.
- Worker đọc Outbox → dispatch tới adapter (email, push, payment reconciliation).
- Đảm bảo DB change và side effect không lệch.

### 4.6 Time

- Mọi timestamp trong DB lưu UTC.
- API trả UTC ISO-8601 + field `timezone` của user (mặc định `Asia/Ho_Chi_Minh`).
- Lịch PT dùng local date + local time slot theo timezone PT.

### 4.7 Money

- Integer VND. Cột DB: `BIGINT`.
- Phí nền tảng tính bằng integer division + rounding half-up ở đồng gần nhất.
- Không floating point cho tính toán tài chính.

### 4.8 Audit

- Bảng `audit_log` cho hành động nhạy cảm (admin duyệt PT, refund, lock account, role change).
- Không log secrets, không log payload chat riêng tư.

## 5. Persistence

- EF Core 8 + Npgsql provider.
- Migrations có kiểm soát: `dotnet ef migrations add`, áp dụng qua job riêng trong Compose (không auto).
- Mỗi bounded context có schema riêng để dễ backup/partition sau này.

## 6. Concurrency

- Booking slot: giữ chỗ có TTL, tạo unique constraint `(trainer_id, slot_start)` cho appointment `active` (status not in cancelled/refunded).
- Sử dụng row version (`xmin` hoặc `version` column) cho aggregate quan trọng.
- Transaction ngắn; không giữ transaction mở khi gọi provider.

## 7. Resilience

- Polly: retry có giới hạn + backoff + jitter cho HTTP outbound.
- Circuit breaker cho provider (payment, email).
- Timeout mặc định cho outbound HTTP: 5s.
- Rate limit (memory trong instance, không Redis ở MVP).

## 8. Observability

- Structured log (Serilog) → JSON, có `traceId`, `userId`, `path`, `latencyMs`.
- Metrics: Prometheus endpoint, request rate, p50/p95/p99, error rate, DB pool.
- Health: `/health/live` (process), `/health/ready` (DB + dependencies).

## 9. Phạm vi KHÔNG thuộc kiến trúc MVP

- Microservices, K8s, service mesh.
- Redis cache (chỉ thêm khi có bằng chứng bottleneck).
- Elasticsearch / search engine (dùng Postgres FTS trước).
- Realtime WebSocket (chỉ thêm khi có nhu cầu + đo được chi phí).
- GPU / AI inference server (chờ nhóm AI bàn giao).
