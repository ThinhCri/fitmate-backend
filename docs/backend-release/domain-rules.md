# Domain Rules

> Quy tắc nghiệp vụ bắt buộc cho mọi implementation. Mọi thay đổi phải cập nhật file này + `decisions.md`.

## 1. Vai trò (Roles)

| Role | Mô tả | Quyền tổng quát |
|---|---|---|
| `member` | Hội viên | CRUD trên data của mình; đặt booking; marketplace; chat; social. |
| `pt` | Huấn luyện viên | Tất cả quyền của member + quản lý cert, availability, services, my bookings, my revenue. |
| `pt_verified` | PT đã được admin duyệt cert | Hiện trên marketplace; nhận booking. |
| `admin` | Quản trị nền tảng | Quản lý user, cert, booking, payment, finance, moderation, audit, settings. **Không** xem mặc định chat riêng / dữ liệu sức khỏe thô. |

User **không** tự nâng role hay tự set `isVerified`. Chỉ admin.

## 2. Trạng thái tài khoản

| State | Ý nghĩa | Hành vi |
|---|---|---|
| `active` | Hoạt động bình thường | Mọi quyền. |
| `locked` | Bị admin khóa hoặc hệ thống khóa (spam, payment fraud) | Không login; refresh token bị revoke; API trả 403 `FITMATE.AUTH.ACCOUNT_LOCKED`. |
| `pending_deletion` | User yêu cầu xóa; chờ retention xử lý | Không login mới; data giữ cho finance/audit. |
| `deleted` | Đã xóa theo retention | Username/email không tái sử dụng. |

## 3. Booking — state machine & tiền

### 3.1 Trạng thái

> Theo DEC-026: **không gộp** trạng thái lịch / thanh toán / hoàn tiền vào một enum.
> - `AppointmentStatus` chỉ mô tả vòng đời của buổi hẹn.
> - `PaymentStatus` mô tả vòng đời của payment intent.
> - `RefundStatus` mô tả vòng đời của refund.

```
held → pending → confirmed → in_progress → completed
                                 ↘ cancelled → refunded
                                 ↘ cancelled → expired (TTL)
```

| `AppointmentStatus` | Ý nghĩa |
|---|---|
| `Held` | Slot tạm giữ, TTL 5 phút. |
| `Pending` | Đã tạo booking, đang chờ PT xác nhận. |
| `Confirmed` | PT đã xác nhận lịch (KHÔNG đồng nghĩa đã thanh toán). |
| `InProgress` | Buổi tập đã bắt đầu. |
| `Completed` | PT xác nhận hoàn thành. |
| `Cancelled` | Member, PT hoặc admin hủy. |
| `Expired` | Giữ chỗ/yêu cầu hết hạn. |

| `PaymentStatus` | Ý nghĩa |
|---|---|
| `Pending` | Đã tạo payment intent, chờ redirect/IPN. |
| `Paid` | Provider xác nhận thanh toán thành công. |
| `Failed` | Thanh toán thất bại hoặc user hủy ở trang provider. |
| `Refunded` | Đã hoàn toàn bộ `actual_paid_vnd`. |
| `PartialRefunded` | Đã hoàn một phần; còn dư nợ. |

| `RefundStatus` | Ý nghĩa |
|---|---|
| `Pending` | Admin yêu cầu, chờ provider confirm. |
| `Succeeded` | Provider xác nhận đã chuyển tiền. |
| `Failed` | Provider từ chối (giữ nguyên payment không thay đổi). |

### 3.1.1 Sự kiện (event) — không phải trạng thái

- **`is_late_attendee: bool`**: thông tin attendance/arrival. Member đến trễ → flag `true`. **KHÔNG tự kết luận no-show** chỉ vì quá giờ.
- **`BookingRescheduleEvent`**: sự kiện đổi lịch có audit. Fields:
  - `booking_id`, `requested_by`, `requested_at`.
  - `old_slot_start_utc`, `new_slot_start_utc`.
  - `accepted_by?`, `accepted_at?`.
  - `status`: `Pending | Accepted | Rejected | Cancelled`.
  - `reason?`.
- Booking có thêm field `previous_booking_id?` để trace lịch sử reschedule.
- Reschedule có **transaction** check slot mới trước khi release slot cũ. Nếu đổi lịch thất bại → slot cũ **không bị mất**.

### 3.2 Transition matrix (AppointmentStatus)

| From → To | Trigger | Actor | Guard |
|---|---|---|---|
| (none) → Held | `POST /bookings/holds` | Member | Slot available, service hợp lệ |
| Held → Pending | `POST /bookings/confirm` (sau Payment.Paid) | Member | Hold chưa hết TTL, payment=Paid |
| Held → Expired | cron TTL | system | Hold.quota > TTL (5 phút) |
| Pending → Confirmed | PT đồng ý | PT của booking | status hiện tại = Pending |
| Confirmed → InProgress | Slot bắt đầu | system auto (slot_start_utc - 5 min) hoặc PT manual | status = Confirmed, payment = Paid |
| InProgress → Completed | PT xác nhận | PT của booking | status = InProgress |
| {Held, Pending, Confirmed, InProgress} → Cancelled | Member / PT / Admin yêu cầu cancel | Owner hoặc admin | chưa Completed, chưa Refunded |
| Pending → Expired | admin expire (PT không phản hồi trong N ngày) | admin | status = Pending, payment quá hạn |

**Client KHÔNG được PATCH `appointment.status` tùy ý**. Mỗi transition qua endpoint riêng.

### 3.3 Snapshot tại thời điểm thanh toán

Mỗi booking lưu immutable snapshot:
- `price_listed_vnd` — giá niêm yết.
- `discount_vnd` — giảm giá (nếu có).
- `actual_paid_vnd` — integer VND thực trả (không âm, không vượt `price_listed_vnd - discount_vnd`).
- `platform_fee_rate_bps` — basis points (vd: `1000` = 10.00%).
- `platform_fee_vnd` — tính từ `actual_paid_vnd × platform_fee_rate_bps / 10000`, half-up.
- `trainer_share_vnd` = `actual_paid_vnd - platform_fee_vnd`.
- `policy_version` — phiên bản `domain-rules.md` đã dùng (vd: `v1`).
- **`payment_fee_rate_bps` không nhận từ client** — luôn lấy từ server config (DEC-029). Booking snapshot ghi lại giá trị thực dùng để tính.

### 3.4 Hủy & hoàn tiền (chính sách phát triển hiện tại)

| Thời điểm hủy | Hoàn tiền | Ghi nhận doanh thu |
|---|---|---|
| Trước khi `Payment.Paid` | Không có gì để hoàn | Không |
| Sau `Payment.Paid`, trước `Appointment.Completed` | Hoàn toàn bộ `actual_paid_vnd` | Thu nhập PT chưa ghi nhận |
| Sau `Appointment.Completed` | Qua quy trình admin duyệt; có thể partial/full | Refund tạo điều chỉnh kỳ hoàn |
| Sau `Payment.Paid`, quá giờ hẹn không diễn ra | Cần quyết định sản phẩm (no-show policy) — **chưa chốt** | — |

**Đã chốt (BR)**:
- Không tự động `Appointment.Completed` khi quá giờ.
- Không có phí phạt hủy (chưa).
- Refund giảm phần PT + phí tương ứng.
- Hoàn nhiều lần dùng tính lũy kế để tránh lệch làm tròn.
- Tổng refund không vượt tổng đã thanh toán.
- Không tính trùng khi retry.
- **Điều kiện bắt đầu buổi tập và ghi nhận doanh thu** phải kiểm tra CẢ `AppointmentStatus = Confirmed/InProgress/Completed` LẪN `PaymentStatus = Paid`.

### 3.5 Money rules

- Mọi cột tiền: `BIGINT` (đồng VND).
- `platform_fee_rate_bps` lưu `INTEGER`, đơn vị basis points (`1000` = 10.00%). Không float.
- Làm tròn half-up ở đồng gần nhất (`(x + 5) / 10` cho integer xu phát sinh, hoặc `(long)Math.Round(x, MidpointRounding.AwayFromZero)` cho decimal đầu vào).
- Refund cộng dồn: `refunded_so_far + new_refund ≤ actual_paid_vnd`.
- Refund idempotent qua `idempotency_key`.
- Provider fee (nếu có) tách khỏi platform fee; trừ vào share PT nếu BR chốt sau.

### 3.6 Concurrency

- Unique constraint `(trainer_id, slot_start_utc)` khi `AppointmentStatus ∈ {Held, Pending, Confirmed, InProgress, Completed}`.
- Không chỉ check availability rồi insert; insert phải nằm trong transaction với constraint.
- Giữ chỗ có TTL: cron job mỗi 1 phút release slot hết hạn.
- Webhook payment: idempotent qua `payment_id`, chấp nhận out-of-order đến khi trạng thái terminal.

## 4. Thời gian

- DB lưu `TIMESTAMPTZ` (UTC).
- API trả ISO-8601 UTC + field `timezone` của user (mặc định `Asia/Ho_Chi_Minh`).
- Slot PT lưu local `slot_start_local` + `slot_start_utc` + `trainer_timezone`.
- Lọc ngày theo `slot_start_local` của PT (không phải UTC) khi query admin theo "ngày".

## 5. Dữ liệu sức khỏe & chat riêng tư

- Thu thập tối thiểu: cân nặng, chiều cao, giới tính, ngày sinh, mục tiêu tập, tình trạng y tế (tự khai), dị ứng.
- Không log nội dung chat riêng (AI + friend).
- Admin **không** mặc định đọc được:
  - Nội dung chat AI cá nhân.
  - Log sức khỏe thô (vd: meal detail, weight log) — chỉ aggregate.
  - Kết quả AI estimate (trừ khi có moderation liên quan).
- Audit cho mỗi lần admin đọc ngoại lệ (vd: mở chat cá nhân khi có report).
- Retention: chưa chốt, sẽ đề xuất trong `data-model.md`.

## 6. Upload & media

- Size limit: image ≤ 5MB, video ≤ 50MB, audio ≤ 20MB, certificate image ≤ 10MB.
- Content-type whitelist (`image/jpeg`, `image/png`, `image/webp`, `video/mp4`, `audio/mpeg`).
- Storage key: `{context}/{entity_id}/{uuid}.{ext}` — không để user kiểm soát path.
- Public read: chỉ avatar/PT certificate được coi là public sau khi admin duyệt. Mọi file khác qua signed URL TTL ≤ 15 phút.
- Không lưu production upload trên ephemeral disk.

## 7. Audit

Hành động bắt buộc audit:
- Admin duyệt / từ chối certificate.
- Admin lock / unlock user.
- Admin refund.
- Admin resolve / dismiss report.
- Admin force-complete booking.
- Role change.
- AI admin override.

Mỗi audit row: `actor_id`, `actor_role`, `action`, `target_type`, `target_id`, `reason?`, `metadata?`, `created_at`. Không log secrets.

## 8. AI usage (đề xuất, chờ nhóm AI)

- AI **không** quyết định quyền, giá, payment, role.
- Output AI có confidence; FE render "ước tính" khi `source === 'ai_estimate'`.
- Khi AI unavailable: trả `FITMATE.AI.UNAVAILABLE` có cấu trúc. Không fallback sang fake.
- Chức năng không cần AI vẫn chạy khi AI down.

## 9. Quyết định cần user chốt trước production

- [ ] Hạn hủy booking (giờ).
- [ ] No-show policy.
- [ ] Ai xác nhận hoàn thành (chỉ PT? hay cả member confirm?).
- [ ] Tranh chấp (dispute) flow.
- [ ] Ai duyệt refund — admin riêng hay theo quyền finance.
- [ ] Pay-out PT — tần suất, ngưỡng tối thiểu, kênh.
- [ ] Ai chịu provider fee (Fitmate hay PT).
- [ ] Data retention từng loại dữ liệu.
- [ ] Account deletion: hard delete vs anonymize sau bao lâu.
- [ ] Mức giá min/max cho service PT.
