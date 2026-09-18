# Handoff — Backend Release

> Điểm đang dừng + việc tiếp theo. Cập nhật cuối mỗi phiên.

## Phiên hiện tại

- **Ngày**: 2026-09-18
- **Phiên 2**: Đánh giá Identity, Managed Postgres, Google Play policy, capacity plan v2, evidence-based audit BE cũ. **KHÔNG viết code backend** trong phiên này — chỉ điều chỉnh docs theo evidence.

## Phiên 1 (đã làm)

- BE `ff95843` — `docs(be): foundation — source-of-truth docs, OpenAPI v1, .env.example, Docker Compose skeleton [BE-099-partial]` (32 files, 6542 insertions).
- BE `0e40dcc` — `docs(be): handoff — phase 1 complete (foundation)`.
- BE `04e7541` — `feat(be): scaffold .NET 8 solution + 28 domain entities + EF Core schema + Money adapters + 14 unit tests PASS [BE-001, BE-010, BE-020, BE-080, BE-100]` (70 files, 6735 insertions).
- FE `bb8207f` — `docs(fe): backend integration pointer + mobile/.env.example [INT-MOB-prep]` (3 files).
- Web `76766f8` — `docs(web): backend integration pointer + .env.example [INT-WEB-prep]` (3 files).

## Đã làm (phiên 2)

- **Đọc toàn bộ BE cũ** (`FitmateAI_BE\src\main\java\com\fitmate\backend\`) — 12 entity, 2 controller, 2 service, 3 repository, 3 endpoint thực.
- Phát hiện 16 vấn đề nghiêm trọng (evidence-based). Tóm tắt:
  - JWT giả (UUID random), `permitAll` cho mọi route, CORS wildcard credentials.
  - `PtProfile.isVerified=true` tự động khi đăng ký PT (không qua admin).
  - `LocalDateTime` không có timezone, `experience_years` VARCHAR, JSON-as-text.
  - Không có refresh token, session, audit, idempotency, outbox, soft delete.
  - Schema cũ cover ~3/69 endpoint mới (97% cần viết mới).
- **Đọc mobile domain.ts** — 9 type chính. Phát hiện AppointmentState lệch giữa 3 client (mobile 6, web 4, BE 7).
- **Đọc web mockData.js** — phát hiện `paymentMethod` lệch, `targetType` lệch, `platformPolicy` chưa có API.
- **Viết 3 file mới**:
  - `audit-old-backend.md` — kiểm kê chi tiết BE cũ + gap analysis.
  - `identity-evaluation.md` — đánh giá ASP.NET Core Identity (chốt DEC-021).
  - `managed-postgres-comparison.md` — so sánh 4 managed Postgres (DO/RDS/Neon/Supabase) theo 5 tiêu chí.
  - `vnpay-googleplay-policy.md` — VNPay integration + Google Play policy cho PT booking.
- **Cập nhật**:
  - `decisions.md` — thêm DEC-021..025 (Identity, Managed Postgres, Google Play, Migration scope, Staging).
  - `capacity-plan.md` — capacity v2 với SPOFs + thresholds + điều kiện nâng cấp.
  - `data-model.md` — Identity schema + ghi chú migration = greenfield.
  - `tasks.md` — cập nhật BE-001 DONE, thêm BE-021 (Identity), BE-099 (xóa Supabase secret).
  - `feature-api-matrix.md` — section Gap Analysis với evidence từ 3 nguồn.
  - `user-actions.md` — thêm UA-018..024 (chốt 6 quyết định sản phẩm + managed Postgres + Google Play).
  - `handoff.md` (file này).

## Việc cần làm tiếp

### Phiên sau (khi user trả lời UA-018..024)

**KHÔNG code backend** cho đến khi user trả lời các câu hỏi sau:
- **UA-018**: AppointmentStatus enum (mobile 6 ↔ web 4 ↔ BE 7) — chốt 1 schema thống nhất.
- **UA-019**: paymentMethod enum (BE chỉ có `Vnpay` hay multi-provider?).
- **UA-020**: Report targetType (`post|comment|user|certificate` hay `post|user|pt`?).
- **UA-021**: Managed Postgres provider (DO / AWS RDS / Neon / Supabase) + tier.
- **UA-022**: platformFeeRate API (read-only hay cho phép admin sửa?).
- **UA-024**: Verify Google Play policy mới nhất.

### Sau khi có câu trả lời

1. Cập nhật `decisions.md` (DEC-026..030).
2. Cập nhật OpenAPI spec + `feature-api-matrix.md` cho khớp.
3. **BE-021**: tích hợp ASP.NET Core Identity (DEC-021) — thay `User` bằng `ApplicationUser : IdentityUser<Guid>`, thêm 4 bảng Identity.
4. **BE-030**: Auth (JWT custom + IdentityUser + refresh token + tests).
5. **BE-070**: Idempotency middleware + Outbox worker.
6. **BE-060**: Booking với concurrency test.
7. **BE-080**: Validation (FluentValidation).
8. **BE-090**: Authorization (policy + ownership).
9. **BE-110..220**: Admin endpoints.
10. **BE-300**: Validate Docker Compose (`compose up postgres` + `dotnet ef database update`).
11. **BE-330**: k6 smoke + normal + peak test trên staging.
12. **FE INT-MOB-001..005** + **Web INT-WEB-001..003**: integrate sau khi BE pass tests.

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
| B-006 | UA-018..022 chốt quyết định sản phẩm | User | Xem `user-actions.md` §UA-018..022 |
| B-007 | UA-021 chốt managed Postgres | User | Xem `managed-postgres-comparison.md` §5 |
| B-008 | UA-024 verify Google Play policy | User | Tra cứu lại trước khi submit |

## Việc user / nhóm AI

- Xem `user-actions.md` UA-001..UA-024. Các UA mới cần trả lời sớm: **UA-018..024**.
- Bắt đầu từ UA-001 (rotate Supabase) và UA-002 (cấp Git hosting) — vẫn blocker cho BE-099.

## Bước tiếp theo (kế hoạch phiên sau)

1. **Chờ user trả lời UA-018..024** trước khi code backend tiếp.
