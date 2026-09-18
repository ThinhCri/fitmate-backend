# API Contract Conventions

> Quy ước bắt buộc cho mọi endpoint. Chi tiết schema trong `openapi/fitmate-v1.yaml`.

## 1. Versioning

- URL prefix: `/api/v1/...`.
- Mỗi breaking change → version mới `/api/v2/...`. Không phá v1 trong 6 tháng sau khi có v2 stable.
- Additive change (thêm field optional, thêm endpoint mới) OK trong v1.

## 2. IDs & enums

- ID là string `cuid` (collision-resistant, sortable). Implementation dùng thư viện `cuid2`.
- Enum string trong JSON, không phải integer.
- **Enum string ổn định, thống nhất casing** — `AppointmentStatus` / `PaymentStatus` / `RefundStatus` dùng **PascalCase**. Ví dụ: `"Held"`, `"Pending"`, `"Paid"`. KHÔNG dùng snake_case trong OpenAPI (lịch sử phiên bản cũ có `held`, `awaiting_payment` đã deprecated).
- Status transition enums + matrix xem `domain-rules.md §3`.

### 2.1 Trạng thái mapping chuẩn giữa 3 client

> Theo DEC-026. Mobile dùng 6 giá trị, web dùng 4. BE canonical là 7 giá trị tách riêng giữa lịch và payment/refund.

| Mobile (`Appointment.status`) | Web (`Booking.status`) | BE `AppointmentStatus` |
|---|---|---|
| `pending` (chờ PT xác nhận) | `pending` | `Held` hoặc `Pending` |
| `confirmed` | — | `Confirmed` |
| `completed` | `completed` | `Completed` |
| `cancelled` | `cancelled` | `Cancelled` |
| `late` (attendance flag) | — | (giữ status, flag `is_late_attendee=true`) |
| `rescheduled` (event) | — | (Cancelled → new booking với `previous_booking_id`) |
| — | `in_progress` | `InProgress` |
| — | — | `Expired` |

**Dữ liệu mơ hồ / thiếu dữ kiện**: render `"—"` ở FE, **không map bằng suy đoán** (DEC-034). Ví dụ record mobile cũ `status='late'` không đủ dữ kiện suy thành `Cancelled` hay `Completed` — adapter trả `null`.

### 2.2 Late & Rescheduled

- `late` không phải status — là flag `is_late_attendee` trên appointment. Server KHÔNG tự set dựa trên thời gian, không tự kết luận no-show.
- `rescheduled` không phải status — là **event** qua bảng `booking_reschedule_events`. Booking cũ ở trạng thái `Cancelled`; booking mới có `previous_booking_id` trỏ về event.

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
- **Phí nền tảng lưu theo basis points** (`platform_fee_rate_bps`, integer). `1000 bps = 10.00%`. OpenAPI ghi rõ `description` để frontend/đối tác không nhầm giá trị 10 vs 0.10. Booking snapshot lưu `platform_fee_rate_bps` + `platform_fee_vnd` + `policy_version`.
- **`platform_fee_rate_bps` không nhận từ client**. Server đọc từ config (`PLATFORM_FEE_BPS` env + policy version). Xem DEC-029.

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

## 16. Payment provider interface (DEC-027)

- Application layer giữ interface `IPaymentProvider`:
  - `CreatePaymentAsync(CreatePaymentRequest) → PaymentResponse`
  - `VerifySignature(IDictionary<string,string>) → bool`
  - `QueryTransactionAsync(string providerTxnRef) → ProviderTransactionStatus`
  - `RefundAsync(RefundRequest) → RefundResponse`
  - `ParseProviderReturn(IDictionary<string,string>) → PaymentReturnResult`
- Release đầu chỉ có 1 implementation: `VnpayPaymentProvider`. **Không viết nhiều adapter giả** không dùng đến (DEC-033).
- `method` field trên `Payment` chỉ nhận `vnpay`. Mọi provider khác → 400 `FITMATE.PAYMENT.PROVIDER_NOT_SUPPORTED`.
- Sandbox/production:
  - Development env → SDK sandbox URL, fake return OK.
  - Production env → SDK production URL **bắt buộc** config thật. Fail-fast ở startup nếu secret rỗng hoặc URL chứa "sandbox".
- Phí provider (nếu provider thu) tách khỏi platform fee. Lưu ở `Payment.provider_fee_vnd`, trừ vào `trainer_share_vnd` theo BR sau.
- `method` ≠ provider. QR / card / bank là **channel** phụ, model có thể đính kèm nếu provider trả về. Không phải enum API.

## 17. Transition matrix cho Appointment

- Mỗi transition đi qua endpoint riêng (vd `POST /me/bookings/{id}/accept`, `POST /me/bookings/{id}/cancel`, ...). Xem `domain-rules.md §3.2`.
- Client **không được PATCH `appointment.status`** tùy ý — server từ chối với 400 `FITMATE.BOOKING.INVALID_TRANSITION` và không có field đó trong schema.
- Mọi transition ghi audit (`actor_id`, `actor_role`, `from_status`, `to_status`, `reason?`, `created_at`).
