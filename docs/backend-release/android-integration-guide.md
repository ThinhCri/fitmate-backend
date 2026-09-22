# Android Integration Guide

> Baseline update 2026-09-22: tài liệu này giữ chi tiết/lịch sử; khi có mâu thuẫn, dùng [README](README.md), [domain rules](domain-rules.md), [tasks](tasks.md) và DEC-051. Không dùng giá/quota, trạng thái DONE, mock adapter hoặc route giả định trong tài liệu cũ làm bằng chứng production. Nguồn policy cập nhật: [release sources](policy-and-runtime-sources.md).

> Hướng dẫn tích hợp Android app với BE .NET mới. Đọc `docs/mobile-redesign/decisions.md` (FE repo) trước để hiểu nguyên tắc mobile.

## 1. Nguồn chuẩn

- OpenAPI: `docs/backend-release/openapi/fitmate-v1.yaml` (BE repo).
- Quy ước API: `docs/backend-release/api-contract.md`.
- Domain rules: `docs/backend-release/domain-rules.md`.
- Authorization: `docs/backend-release/authorization-matrix.md`.

## 2. Base URL & môi trường

| Môi trường | Base URL |
|---|---|
| Production | `https://api.fitmate.vn/api/v1` |
| Staging | `https://staging-api.fitmate.vn/api/v1` |
| Local emulator | `http://10.0.2.2:8080/api/v1` (Android emulator trỏ về host) |
| Local device | `http://<host-lan-ip>:8080/api/v1` |

Base URL đặt trong `mobile/.env` (`EXPO_PUBLIC_API_BASE_URL`), KHÔNG hardcode trong code. `.env.example` chỉ có placeholder.

## 3. Auth

### 3.1 Token

- `access_token` lưu trong `expo-secure-store` (Keychain/Keystore).
- `refresh_token` lưu `expo-secure-store`. (BE có thể trả cookie HttpOnly cho web; Android nhận qua body.)
- TTL: access 15 phút, refresh 30 ngày.

### 3.2 Storage helper

FE đã có `src/services/secureStorage.ts`. Mở rộng thêm:
- `getAccessToken()`, `setAccessToken()`, `clearTokens()`.
- `getRefreshToken()`, `setRefreshToken()`.
- `refreshIfNeeded()`: tự refresh khi access sắp hết hoặc 401.

### 3.3 Auto refresh

- Interceptor trên HTTP client: nếu response 401, gọi `/auth/refresh` 1 lần, retry request. Nếu refresh fail → logout.

## 4. HTTP client

- Dùng `fetch` (đã có sẵn trong RN).
- Tạo `src/services/apiClient.ts` với:
  - Base URL từ env.
  - Header `Authorization: Bearer <token>`.
  - Header `Accept-Language: vi` (nếu BE hỗ trợ).
  - `Idempotency-Key` cho mutation tài chính.
  - Auto refresh.
  - Map lỗi 4xx/5xx về domain error shape (`{ code, message, traceId, errors }`).
  - Timeout mặc định 15 giây.

## 5. Pagination

| Loại | Query | Response |
|---|---|---|
| Cursor (feed, message) | `?cursor=<id>&limit=20` | `{ items, nextCursor }` |
| Offset (admin) | `?page=1&pageSize=25` | `{ items, total, page, pageSize }` |

## 6. Error handling

- 400 → hiển thị `errors` map theo field.
- 401 → refresh, nếu fail → logout.
- 403 → toast "không có quyền".
- 404 → empty state.
- 409 (slot taken, idempotency conflict) → thông báo + retry hint.
- 410 (hold expired) → bắt user tạo hold mới.
- 422 (validation business) → hiển thị field errors.
- 429 → toast + `Retry-After`.
- 503 (AI unavailable) → fallback UX "Tính năng AI tạm thời không khả dụng".

## 7. Local sync & idempotency

- Workout session: gửi kèm `client_id` (UUID v4) để chống duplicate khi retry.
- Payment create: gửi kèm `Idempotency-Key`.
- Outbox-style local queue: optional. Nếu dùng, lưu mutation chưa gửi ở AsyncStorage, retry khi online.

## 8. Upload

- Multipart với `Content-Type: multipart/form-data`.
- Client gửi metadata qua form fields.
- Lấy signed URL từ BE khi cần hiển thị private file (vd: certificate).

## 9. Realtime (chưa có ở MVP)

- Polling cho notification: mỗi 30 giây khi app foreground.
- Không dùng WebSocket cho MVP.

## 10. Mock adapter hiện tại → real API

`mobile/src/services/mockUserService.ts` đang giả lập. Khi integrate:

| Hiện tại | Thay bằng |
|---|---|
| `mockUserService.login()` | `apiClient.post('/auth/login', ...)` |
| `mockUserService.register()` | `apiClient.post('/auth/register', ...)` |
| `mockUserService.getCurrentUser()` | cache + `apiClient.get('/users/me')` |
| `mockUserService.saveUser()` | `apiClient.patch('/users/me', ...)` (partial) |

Không thay đổi `AuthContext.tsx` API; chỉ swap implementation trong service.

## 11. AI unavailable

- `POST /me/nutrition/scan` trả `503` + `code=FITMATE.AI.UNAVAILABLE`. FE render "Tính năng AI đang bận, thử lại sau" — KHÔNG dùng fake result.
- `POST /chat/ai/messages` tương tự.

## 12. Demo users (chỉ development/staging)

```
user@fitmate.vn / 123456   → member
pt@fitmate.vn / 123456     → PT verified
admin@fitmate.vn / <set via CLI>
```

KHÔNG commit password production. Password demo chỉ tồn tại khi `ASPNETCORE_ENVIRONMENT=Development`.

## 13. Smoke tests cho integration

Sau khi wire client, chạy thủ công theo checklist ở `docs/backend-release/release-checklist.md` (gate "Android integration-ready").

## 14. Việc thuộc về mobile (FE repo) — tách task

Mọi thay đổi code FE để integrate BE sẽ thuộc task riêng, không nằm trong gói này. Task mẫu:
- `INT-MOB-001` Thay mock bằng API client (`apiClient.ts`, `secureStorage` mở rộng).
- `INT-MOB-002` Migrate AuthContext sang real API.
- `INT-MOB-003` Migrate booking/payment.
- `INT-MOB-004` Migrate workout/nutrition/social/chat.
- `INT-MOB-005` Smoke end-to-end với staging.
