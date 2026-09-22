# BE-140 — Payment Skeleton (no VNPay) — Plan

> **Goal:** Member tạo payment intent cho hold, BE trả mock redirect URL, idempotent retry an toàn. Real VNPay integration thuộc BE-400 (chờ UA-008).

## 1. Existing state (verified 2026-09-21)

### OpenAPI
- ✅ `POST /payments` (Idempotency-Key, body `{hold_id, method:'vnpay'}` → 201 `Payment`)
- ✅ `GET /payments/vnpay/return` (user redirect)
- ✅ `POST /payments/vnpay/ipn` (webhook)
- ✅ `GET /me/payments/{id}` (member xem payment của mình)
- ✅ `POST /me/bookings/{id}/refund` (refund qua admin)
- ✅ Schema `Payment`, `PaymentStatus`, `Refund`, `RefundStatus`

### Domain
- ✅ `Appointment.AttachPayment(paymentId, actualPaid, ...)` ready
- ✅ `BookingHold` (TTL 5min) — endpoint consumer

### Not implemented
- ❌ `Payment` domain entity
- ❌ `Refund` domain entity
- ❌ `IPaymentService` + `PaymentService`
- ❌ `IPaymentStore` + `EfPaymentStore`
- ❌ `IPaymentProvider` (interface only — per DEC-027/YAGNI; stub now, real at BE-400)
- ❌ `PaymentController` (4 endpoints)
- ❌ Unit tests

## 2. Scope

### In scope

1. **`POST /payments`** — Idempotency-Key required, validate hold active, compute fee/share snapshot via `Money.SplitFeeBps` (DEC-008/029), return 201 with mock `redirect_url = https://mock.local/payments/{id}`.
2. **`GET /payments/vnpay/return`** — Always 200 + `{status: 'pending', message: 'Real VNPay integration pending UA-008'}`. No state mutation.
3. **`POST /payments/vnpay/ipn`** — Stub: 200 + log; do NOT mutate state. Real verify+webhook in BE-400.
4. **`GET /me/payments/{id}`** — Get payment by id with ownership check.
5. **`GET /me/revenue` + `GET /me/revenue/{bookingId}`** — PT revenue endpoints (queries only, no mutation).

### Out of scope

- Real VNPay signing/verification/IPN → BE-400 (BLOCKED UA-008).
- Refund processing → BE-150 already has `/admin/finance/refund` or admin endpoint exists; will wire later.
- Outbox `payment.created` event (no consumer yet).

## 3. Design decisions

### Provider interface

Per DEC-027/YAGNI: chỉ define `IPaymentProvider` interface với 1 method `CreatePaymentAsync(CreatePaymentRequest)` → `(providerTxnRef, redirectUrl)`. Stub returns deterministic mock URL. Real `VnpayPaymentProvider` thuộc BE-400.

### Idempotency

`POST /payments` yêu cầu `Idempotency-Key` (per DEC-011). BE-070 middleware đã có. Endpoint chỉ cần forward.

### Money

Reuse `Money.SplitFeeBps(actualPaidVnd, feeBps)` từ BE-100. Default `feeBps = 1000` (10%) từ config `PLATFORM_POLICY__FEEBPS`.

### Hold relationship

`Payment.HoldId` (FK to `BookingHold.Id`) — nhiều payments có thể tồn tại cho 1 hold (retry) nhưng chỉ 1 successful → update Appointment status = Paid via `AttachPayment`.

## 4. Acceptance criteria

- `dotnet build` 0 errors.
- `PaymentService` unit tests ≥ 6: create+idempotent, hold not found, hold expired, hold already has paid payment, ownership, amount validation, return URL stub returns valid format.
- IPN stub returns 200.
- All 5 endpoints wired with DI.
- OpenAPI unchanged.

## 5. Commit phases

1. **`feat(be): add Payment entity, abstractions, DTOs and error codes [BE-140]`** — domain entities + abstractions + DTOs.
2. **`feat(be): add PaymentService, EF store, stub provider and controller [BE-140]`** — service + store + stub + controller + DI.
3. **`test(be): add PaymentServiceTests with 6 cases [BE-140]`** — unit tests.
4. **`docs(be): mark BE-140 done, add session 34 handoff [BE-140]`** — tasks.md + handoff.md.

Total: 4 commits.
