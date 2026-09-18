# API Contract Conventions

> Quy ước bắt buộc cho mọi endpoint. Chi tiết schema trong `openapi/fitmate-v1.yaml`.

## 1. Versioning

- URL prefix: `/api/v1/...`.
- Mỗi breaking change → version mới `/api/v2/...`. Không phá v1 trong 6 tháng sau khi có v2 stable.
- Additive change (thêm field optional, thêm endpoint mới) OK trong v1.

## 2. IDs & enums

- ID là string `cuid` (collision-resistant, sortable). Implementation dùng thư viện `cuid2`.
- Enum string trong JSON, không phải integer.
- Status transition enums xem `domain-rules.md`.

## 3. Pagination

- Cursor-based cho feed (post, comment, message): `cursor` + `limit` (default 20, max 100). Response trả `nextCursor`.
- Offset-based cho admin table: `page` (1-based) + `pageSize` (default 25, max 100). Response trả `{ items, total, page, pageSize }`.

## 4. Search / filter / sort

- Admin endpoint filter/sort/pagination **ở server**.
- Allowlist field cho filter và sort (không nhận field tuỳ ý).
- Search: Postgres full-text, simple query (không operator nâng cao).

## 5. Validation

- FluentValidation cho mọi DTO đầu vào.
- Validation error trả `400` + `ProblemDetails` với `errors` map field → messages.

## 6. Error model

- Tuân theo RFC 7807 (`ProblemDetails`).
- Trường bắt buộc:
  - `type`: URI trỏ tới docs.
  - `title`: ngắn gọn.
  - `status`: HTTP status.
  - `detail`: mô tả.
  - `code`: mã ổn định `FITMATE.<DOMAIN>.<REASON>`.
  - `traceId`: GUID để match log.
  - `errors`: optional map `field → string[]`.

Ví dụ:

```json
{
  "type": "https://docs.fitmate.vn/errors/booking/slot-taken",
  "title": "Slot already taken",
  "status": 409,
  "code": "FITMATE.BOOKING.SLOT_TAKEN",
  "detail": "Slot 2026-09-20T09:00:00Z with trainer tu.pt@fitmate.vn is no longer available.",
  "traceId": "7c9e6679-7425-40de-944b-e07fc1f90ae7"
}
```

## 7. Auth header

- `Authorization: Bearer <access_token>` cho mọi endpoint trừ public.
- Refresh qua cookie `refresh_token` (HttpOnly, Secure, SameSite=Lax) + CSRF token cho web; hoặc body `refresh_token` cho mobile.

## 8. Time

- Mọi timestamp UTC ISO-8601 với `Z` suffix.
- Field optional `timezone` cho client khai báo timezone user.
- Server so sánh thời gian bằng UTC; convert sang timezone cho response khi cần.

## 9. Money

- Integer VND. Field `*_vnd` là number (JSON integer).
- Không có field tiền ở dạng decimal.

## 10. Upload

- Multipart `multipart/form-data` với field `file` + metadata ở form fields.
- Server kiểm tra content-type, size, magic number.
- Response trả `url` (signed URL) hoặc `key` để client tự lấy signed URL.

## 11. Idempotency

- Header `Idempotency-Key: <uuid>` cho mọi mutation có side effect tài chính.
- TTL 24 giờ. Key + payload hash lưu DB.
- Retry cùng key + payload → trả response cũ.
- Retry cùng key + payload khác → 409 `FITMATE.IDEMPOTENCY.CONFLICT`.

## 12. Rate limiting

- Memory-based (AspNetCoreRateLimit) cho MVP.
- Endpoint nhạy cảm (login, register, forgot password, payment create):
  - 5 req / 1 phút / IP.
  - 20 req / 1 giờ / IP.
- Endpoint chat AI: 30 req / 1 phút / user.
- 429 trả ProblemDetails với `Retry-After`.

## 13. Filtering & sorting fields

Mỗi admin endpoint có allowlist rõ trong OpenAPI. Ví dụ `GET /api/v1/admin/bookings`:

- `status`: enum BookingStatus.
- `ptId`: cuid.
- `memberId`: cuid.
- `dateFrom`, `dateTo`: ISO date.
- `sort`: enum `createdAt:desc`, `createdAt:asc`, `slotStart:desc`, `slotStart:asc`. Default `createdAt:desc`.

## 14. ProblemDetails extension

- `errors`: `{ "fieldName": ["message"] }`.
- `metadata`: optional, cho admin debug.

## 15. Không bao giờ trả

- Password hash.
- Stack trace (chỉ trong dev).
- JWT signing key.
- Provider payload thô (chỉ trả qua admin endpoint cụ thể).
- Health / weight / meal detail cho user khác (trừ admin moderation exception với audit).
