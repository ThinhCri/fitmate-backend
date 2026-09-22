# Admin Web Integration Guide

> Baseline update 2026-09-22: tài liệu này giữ chi tiết/lịch sử; khi có mâu thuẫn, dùng [README](README.md), [domain rules](domain-rules.md), [tasks](tasks.md) và DEC-051. Không dùng giá/quota, trạng thái DONE, mock adapter hoặc route giả định trong tài liệu cũ làm bằng chứng production. Nguồn policy cập nhật: [release sources](policy-and-runtime-sources.md).

> Hướng dẫn tích hợp Fitmate Admin Web (React + Vite) với BE .NET mới.

## 1. Nguồn chuẩn

- OpenAPI: `docs/backend-release/openapi/fitmate-v1.yaml` (BE repo).
- Quy ước API: `docs/backend-release/api-contract.md`.
- Domain rules: `docs/backend-release/domain-rules.md`.
- Authorization: `docs/backend-release/authorization-matrix.md`.

## 2. Base URL

| Môi trường | Base URL |
|---|---|
| Production | `https://api.fitmate.vn/api/v1` |
| Staging | `https://staging-api.fitmate.vn/api/v1` |
| Local | `http://localhost:8080/api/v1` |

Đặt trong `web/.env` dưới dạng `VITE_API_BASE_URL`. KHÔNG hardcode trong code. `.env.example` chỉ có placeholder.

## 3. Auth cho admin web

### 3.1 Khuyến nghị: HttpOnly cookie + CSRF

- Login gọi `POST /auth/login`. BE set cookie:
  - `access_token` HttpOnly Secure SameSite=Strict, path `/`, max-age 900.
  - `refresh_token` HttpOnly Secure SameSite=Strict, path `/api/v1/auth`, max-age 2592000.
  - `XSRF-TOKEN` (NON-HttpOnly) để client đọc.
- Mọi mutation kèm header `X-CSRF-Token: <XSRF-TOKEN>` đọc từ cookie.
- Cookie path khác nhau để refresh token không bị gửi tới endpoint khác.

### 3.2 Alternative: Bearer token trong memory

- Có thể dùng nếu web cần gọi BE từ domain khác (CORS). Lưu token trong memory, refresh qua endpoint riêng.
- Không lưu JWT trong localStorage.

### 3.3 Quyết định

- Mặc định: **HttpOnly cookie + CSRF** cho cùng domain (production). Cho staging nếu FE + BE cùng domain, dùng cookie.
- Nếu FE hosted khác domain (vd: `admin.fitmate.vn` vs `api.fitmate.vn`), dùng **bearer + refresh** kèm CORS allowlist.

## 4. CORS

| Origin | CORS policy |
|---|---|
| `https://admin.fitmate.vn` (prod) | `Access-Control-Allow-Origin: <origin>`, `Allow-Credentials: true`, allowed methods GET/POST/PUT/PATCH/DELETE/OPTIONS |
| `https://staging-admin.fitmate.vn` | Same |
| `http://localhost:5173` (dev) | Same |

KHÔNG dùng `Access-Control-Allow-Origin: *` kèm credentials.

## 5. CSRF

- BE cấp `XSRF-TOKEN` cookie NON-HttpOnly.
- FE axios/fetch interceptor đọc cookie này và gắn vào header `X-CSRF-Token` cho mọi mutation.
- BE verify CSRF middleware cho non-GET.

## 6. HTTP client

- `axios` hoặc `fetch` wrapper. Khuyến nghị `fetch` để giảm bundle size.
- Interceptor:
  - Gắn `Authorization` nếu dùng bearer.
  - Gắn `X-CSRF-Token` nếu dùng cookie.
  - Auto refresh khi 401.
  - Map lỗi về cùng shape.

## 7. Filter / sort / pagination ở server

Admin web hiện đang filter/sort/page ở client với mock data. Khi integrate:
- Filter/sort gọi server qua query params.
- Pagination offset-based với `page`, `pageSize`.
- KHÔNG tải toàn bộ rồi tự tổng hợp (BE có thể trả hàng triệu row).

## 8. Allowlist fields

Admin endpoint trả `AdminUserSummary` với field tối thiểu cần cho UI:
- `id, email, full_name, role, status, is_verified, certificate_status, created_at, last_active_at`.
- KHÔNG trả `password_hash`, `email_verification_token`, v.v.

## 9. Dashboard definitions

Mọi KPI/chart phải có:
- definition (vd: "tổng doanh thu Fitmate = sum platform_fee_vnd trong kỳ").
- source (endpoint, query param).
- timestamp + timezone.
- filter (vd: `range=7d|30d|90d|all`).
- aggregation (`sum`, `count`).
- cancelled/refunded handling (đã làm ở `domain-rules.md §3`).

## 10. Conflict & validation

- 409 (vd: đã duyệt cert): hiển thị toast "Chứng chỉ đã được duyệt".
- 400 (validation): map errors theo field, hiển thị inline.

## 11. Refetch / cache invalidation

- Sau mutation admin (approve cert, lock user, refund):
  - Refetch list liên quan.
  - Optimistic update optional.
- Không dùng cache không kiểm soát cho số liệu tài chính.

## 12. Upload

- Certificate reject reason bắt buộc ≥ 5 ký tự.
- Refund force yêu cầu amount + reason.
- Lock user yêu cầu reason.

## 13. Mapping module web hiện → endpoint mới

| Component web hiện tại | Endpoint mới |
|---|---|
| `Login.jsx` | `POST /auth/login` |
| `Overview.jsx` | `GET /admin/finance/overview?range=7d\|30d\|all`, `GET /admin/action-required` |
| `UserManagement.jsx` | `GET /admin/users`, `POST /admin/users/{id}/lock` |
| `TrainerManagement.jsx`, `CertificateModeration.jsx` | `GET /admin/certificates?status=pending`, `POST /admin/certificates/{id}/approve\|reject` |
| `BookingManagement.jsx` | `GET /admin/bookings`, `POST /admin/bookings/{id}/refund` |
| `FinanceManagement.jsx`, `FinancialReports.jsx` | `GET /admin/finance/overview`, `GET /admin/finance/ledger`, `GET /admin/finance/pt/{id}/earnings` |
| `ViolationReports.jsx`, `PostModeration.jsx` | `GET /admin/reports`, `POST /admin/reports/{id}/resolve\|dismiss` |
| `Settings.jsx` | `POST /users/me/password` (admin self-change), `GET /admin/audit` |
| `AiTokenAnalytics.jsx` | `GET /admin/metrics/ai-tokens` (cần AI provider) |
| `PremiumManagement.jsx` | `GET /admin/premium/users` (cần user chốt nghiệp vụ) |
| `StudentChatModal.jsx`, `MentorDashboard.jsx` | Cần quyết định sản phẩm (chưa chốt) |
| `Topbar.jsx` notifications | `GET /me/notifications`, `POST /me/notifications/{id}/read` (admin user cũng có notification?) — cần chốt |

## 14. Demo admin (chỉ Development)

- Bootstrap qua CLI: `dotnet run --project src/Fitmate.Api -- create-admin --email admin@fitmate.vn --password <from-stdin>`.
- KHÔNG commit password.
- Login form FE đã hardcode `admin123` — phải thay bằng call API thật.

## 15. Smoke tests

Sau khi wire client, chạy theo `docs/backend-release/release-checklist.md` (gate "Admin integration-ready").

## 16. Việc thuộc về admin web (Web repo) — tách task

- `INT-WEB-001` Thay mock bằng API client.
- `INT-WEB-002` Migrate 7 module + 5 module phụ.
- `INT-WEB-003` Smoke end-to-end với staging.
