# Runbook — Payment anomaly

> Khi có dấu hiệu bất thường trong payment (refund nhiều, chargeback, mismatch giữa DB và provider).

## Triệu chứng

- Số tiền trong DB ≠ provider dashboard.
- Refund nhiều bất thường.
- User báo "thanh toán xong nhưng booking chưa confirm".
- Webhook IPN fail liên tục.

## Bước xử lý

1. **Dừng tiếp nhận payment mới** (nếu cần):
   - Disable VNPay tạm thời qua feature flag / config.
2. **Reconcile**:
   - Chạy `POST /admin/payments/reconcile` (admin endpoint).
   - So sánh DB vs provider.
3. **Tìm gap**:
   - Booking paid trong DB nhưng provider không thấy → webhook bị miss → replay.
   - Provider thấy paid nhưng DB không → IPN bị mất → gọi manual query API provider.
4. **Audit**:
   - Liệt kê booking trong kỳ nghi ngờ.
   - Xem audit log admin refund.
5. **Refund hàng loạt** (nếu cần):
   - Dùng `POST /admin/bookings/{id}/refund` với reason rõ.
6. **Notify**:
   - Users bị ảnh hưởng.
   - Provider nếu có dispute.

## Sau sự cố

- Viết postmortem.
- Sửa idempotency / webhook handler.
- Thêm alert cho mismatch.

## Phòng ngừa

- Reconciliation job chạy mỗi giờ.
- Alert khi mismatch > X% hoặc > Y VND.
- IPN retry policy rõ ràng.
