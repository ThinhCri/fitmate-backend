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

```
created → held → awaiting_payment → paid → completed
                                       ↘ cancelled → refunded
                                       ↘ cancelled → expired
```

| State | Ý nghĩa |
|---|---|
| `held` | Slot tạm giữ, TTL 5 phút. |
| `awaiting_payment` | Đã tạo payment intent. |
| `paid` | Đã thanh toán thành công (IPN xác nhận). |
| `completed` | PT xác nhận hoàn thành. |
| `cancelled` | Member hoặc PT hủy (xem §3.3). |
| `refunded` | Đã hoàn tiền (admin hoặc auto theo policy). |
| `expired` | Hold quá hạn; thanh toán không đến trước deadline. |

### 3.2 Snapshot tại thời điểm thanh toán

Mỗi booking lưu immutable snapshot:
- `price_listed_vnd` — giá niêm yết.
- `discount_vnd` — giảm giá (nếu có).
- `actual_paid_vnd` — integer VND thực trả (không âm, không vượt `price_listed_vnd - discount_vnd`).
- `platform_fee_rate` — phiên bản chính sách (vd: `v1=0.10`).
- `platform_fee_vnd` — tính từ `actual_paid_vnd` tại thời điểm thanh toán, làm tròn half-up đến đồng.
- `trainer_share_vnd` = `actual_paid_vnd - platform_fee_vnd`.
- `policy_version` — phiên bản `domain-rules.md` đã dùng.

### 3.3 Hủy & hoàn tiền (chính sách phát triển hiện tại)

| Thời điểm hủy | Hoàn tiền | Ghi nhận doanh thu |
|---|---|---|
| Trước khi `paid` | Không có gì để hoàn | Không |
| Sau `paid`, trước `completed` | Hoàn toàn bộ `actual_paid_vnd` | Thu nhập PT chưa ghi nhận |
| Sau `paid` và `completed` | Qua quy trình admin duyệt; có thể partial/full | Refund tạo điều chỉnh kỳ hoàn |
| Sau `paid`, quá giờ hẹn không diễn ra | Cần quyết định sản phẩm (no-show policy) — **chưa chốt** | — |

**Đã chốt (BR)**: không tự động `completed` khi quá giờ. Không có phí phạt hủy (chưa). Refund giảm phần PT + phí tương ứng. Hoàn nhiều lần dùng tính lũy kế để tránh lệch làm tròn. Tổng refund không vượt tổng đã thanh toán. Không tính trùng khi retry.

### 3.4 Money rules

- Mọi cột tiền: `BIGINT` (đồng VND).
- Làm tròn half-up ở đồng gần nhất (`(x + 5) / 10` cho integer xu phát sinh, hoặc `(long)Math.Round(x, MidpointRounding.AwayFromZero)` cho decimal đầu vào).
- Refund cộng dồn: `refunded_so_far + new_refund ≤ actual_paid_vnd`.
- Refund idempotent qua `idempotency_key`.
- Provider fee (nếu có) tách khỏi platform fee; trừ vào share PT nếu BR chốt sau.

### 3.5 Concurrency

- Unique constraint `(trainer_id, slot_start_utc)` khi appointment status ∈ {`held`,`awaiting_payment`,`paid`,`completed`}.
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
