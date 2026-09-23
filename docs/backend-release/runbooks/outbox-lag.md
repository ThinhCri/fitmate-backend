# Runbook — Outbox lag & Dead-letter recovery

> Khi Outbox queue bị tồn đọng (lag), worker gặp lỗi lặp lại, hoặc message rơi vào Dead-Letter Queue.

## 1. Triệu chứng & Cảnh báo (Alerts)

- Cảnh báo: `fitmate_outbox_queue_depth > 100` trong 5 phút liên tục.
- Cảnh báo: `fitmate_outbox_oldest_age_seconds > 300` (có tin nhắn chờ quá 5 phút chưa được xử lý).
- Cảnh báo: `fitmate_outbox_dead_letter_total` tăng > 0 (tin nhắn đã retry 5 lần thất bại).
- Hiện tượng nghiệp vụ: Đặt lịch thành công nhưng hội viên không nhận được thông báo/email; giao dịch thanh toán chậm cập nhật trạng thái.

## 2. Các bước điều tra (Investigation)

1. **Kiểm tra trạng thái Outbox thông qua Admin API**:
   ```http
   GET /api/v1/admin/outbox/stats
   Authorization: Bearer <Admin_JWT>
   ```
   Xem số lượng `pending_count`, `dead_letter_count`, và `oldest_pending_age_seconds`.

2. **Xem danh sách các tin nhắn trong Dead-Letter Queue**:
   ```http
   GET /api/v1/admin/outbox/dead-letter?limit=20
   Authorization: Bearer <Admin_JWT>
   ```
   Ghi nhận `type`, `attempts`, `last_error` của từng message.

3. **Kiểm tra log của OutboxBackgroundService**:
   - Tìm kiếm log `Outbox: handler <Type> failed for <Id>` hoặc `moved to DEAD_LETTER`.
   - Đối chiếu CorrelationId/TraceId để xác định nguyên nhân: lỗi kết nối bên thứ 3 (Push provider, VNPay), schema payload không hợp lệ, hay DB timeout.

## 3. Quy trình xử lý (Mitigation & Recovery)

### Tình huống A: Lỗi tạm thời (Network timeout, Third-party service down)
1. Xác nhận dịch vụ bên ngoài (ví dụ FCM / Expo / Gateway) đã phục hồi.
2. Kiểm tra container `fitmate-api` và background worker vẫn đang hoạt động.
3. Nếu tin nhắn đã rơi vào `dead_letter`, thực hiện Replay có kiểm toán từng tin nhắn:
   ```http
   POST /api/v1/admin/outbox/dead-letter/{id}/replay
   Authorization: Bearer <Admin_JWT>
   ```
4. Kiểm tra worker xử lý và cập nhật `Status = "done"`.

### Tình huống B: Payload JSON bị lỗi format hoặc handler bug
1. **Tuyệt đối không xóa trực tiếp dòng trong DB**.
2. Phân tích `last_error` và `payload_json`.
3. Nếu cần sửa code handler (bug fix):
   - Triển khai hotfix lên API.
   - Sau khi deploy hoàn tất, gọi endpoint replay cho các tin nhắn bị ảnh hưởng.
4. Đảm bảo mọi thao tác replay đều được ghi lại audit log với `admin_id` thực hiện.

## 4. Kiểm toán và Phòng ngừa (Audit & Prevention)

- Mọi lệnh replay đều ghi nhận người thực hiện và timestamp.
- Định kỳ dọn dẹp các tin nhắn đã hoàn thành (`status = "done"`) sau 30 ngày thông qua `PurgeDoneOlderThanAsync`.
- Không tăng số lần retry vô hạn; giới hạn tối đa `MaxAttempts = 5` để bảo vệ worker không bị nghẽn (starvation).

