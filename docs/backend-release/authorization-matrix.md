# Authorization Matrix

> Bảng này là nguồn chuẩn cho mọi endpoint. Mỗi dòng là một action; cột là vai trò; giá trị là quyền. Authorization phải enforce ở **Application layer**, không chỉ ở Controller.

## Ký hiệu

- ✅ allow
- ❌ deny
- 👁️ read-only (admin aggregate, không truy cập raw)
- 🔒 object-level (chỉ chủ sở hữu)
- 🛡️ admin moderation exception (audit bắt buộc)
- ⚠️ cần điều kiện (ghi trong footnote)

## 1. Identity

| Action | public | member | pt | admin |
|---|---|---|---|---|
| Register | ✅ | ✅ | ✅ | ❌ (admin tạo qua API riêng) |
| Login | ✅ | ✅ | ✅ | ✅ (với MFA nếu có) |
| Refresh | ✅ | ✅ | ✅ | ✅ |
| Forgot/reset password | ✅ | ✅ | ✅ | ❌ (admin reset qua API riêng) |
| Change own password | — | 🔒 | 🔒 | 🔒 |
| Get own profile | — | 🔒 | 🔒 | 🔒 |
| Update own profile | — | 🔒 | 🔒 | 🔒 |
| Get any user | — | ❌ | ❌ | ✅ (allowlist fields) |
| Lock / unlock user | — | ❌ | ❌ | ✅ |
| List users | — | ❌ | ❌ | ✅ |
| Delete own account | — | 🔒 | 🔒 | 🔒 |
| Delete any user | — | ❌ | ❌ | ✅ (retention-aware) |

## 2. Trainer

| Action | public | member | pt | admin |
|---|---|---|---|---|
| List PT (marketplace) | — | ✅ (verified only) | ✅ (verified only) | ✅ |
| Get PT detail | — | ✅ (verified only) | ✅ (verified only) | ✅ |
| Submit own certificate | — | ❌ | 🔒 | ❌ |
| List own certificates | — | ❌ | 🔒 | ❌ |
| Approve certificate | — | ❌ | ❌ | ✅ |
| Reject certificate | — | ❌ | ❌ | ✅ |
| Set own availability | — | ❌ | 🔒 | ❌ |
| Get PT availability | — | ✅ | ✅ | ✅ |
| Create / update own service | — | ❌ | 🔒 | ❌ |
| List own services | — | ❌ | 🔒 | ❌ |
| Set PT verified flag | — | ❌ | ❌ | ✅ |

## 3. Booking

> Theo DEC-026: `AppointmentStatus` và `PaymentStatus` là 2 enum riêng. Authorization phải enforce cả 2 trên mỗi action.

| Action | public | member | pt | admin |
|---|---|---|---|---|
| Create hold (`POST /bookings/holds`) | — | 🔒 | ❌ | ❌ |
| Confirm booking (`POST /bookings/{id}/confirm`) — `Held → Pending` | — | 🔒 | ❌ | ❌ |
| PT accept pending (`POST /me/bookings/{id}/accept`) — `Pending → Confirmed` | — | ❌ | 🔒 (PT của booking) | ❌ |
| Member cancel (`POST /me/bookings/{id}/cancel`) — `* → Cancelled` | — | 🔒 | 🔒 (PT của booking) | ✅ |
| PT complete (`POST /me/bookings/{id}/complete`) — `InProgress → Completed` | — | ❌ | 🔒 (PT của booking) | ✅ |
| Admin force complete | — | ❌ | ❌ | ✅ (audit) |
| Reschedule (`POST /me/bookings/{id}/reschedule`) — tạo `BookingRescheduleEvent` | — | 🔒 | 🔒 (PT của booking) | ✅ |
| List own bookings (member view) | — | 🔒 | ❌ | ✅ |
| List own bookings (PT view) | — | ❌ | 🔒 | ✅ |
| List all bookings | — | ❌ | ❌ | ✅ |

**Không có action nào PATCH `appointment.status` tùy ý** — mỗi transition đi qua endpoint riêng với guard.

## 4. Payment

> Theo DEC-027: provider duy nhất là `Vnpay`. `Payment.method = 'vnpay'`.

| Action | public | member | pt | admin |
|---|---|---|---|---|
| Create payment (`POST /payments`) | — | 🔒 | ❌ | ❌ |
| List own payments | — | 🔒 | 🔒 (liên quan booking của mình) | ✅ |
| List all payments | — | ❌ | ❌ | ✅ |
| VNPay return URL | ✅ (signed, UX hint) | — | — | — |
| VNPay IPN webhook | ✅ (signed, source of truth) | — | — | — |
| Reconciliation job trigger | — | ❌ | ❌ | ✅ |
| Force refund (`POST /admin/bookings/{id}/refund`) | — | ❌ | ❌ | ✅ (audit) |
| **PATCH `payment_fee_rate` từ client** | — | ❌ (server-side only) | ❌ | ❌ |

## 5. Finance

| Action | public | member | pt | admin |
|---|---|---|---|---|
| Overview KPIs | — | ❌ | ❌ | ✅ |
| Ledger | — | ❌ | ❌ | ✅ |
| PT earnings | — | ❌ | 🔒 (self) | ✅ |
| My revenue (PT) | — | ❌ | 🔒 | ❌ |
| Revenue detail per booking | — | ❌ | 🔒 | ✅ |

## 6. Workout

| Action | public | member | pt | admin |
|---|---|---|---|---|
| Get own workout plans | — | 🔒 | 🔒 | ❌ |
| Create own workout plan | — | 🔒 | 🔒 | ❌ |
| Log workout session | — | 🔒 | 🔒 | ❌ |
| List own sessions | — | 🔒 | 🔒 | ❌ |
| Log weight | — | 🔒 | 🔒 | ❌ |
| Get weight history | — | 🔒 | 🔒 | ❌ |

## 7. Nutrition

| Action | public | member | pt | admin |
|---|---|---|---|---|
| Log meal | — | 🔒 | 🔒 | ❌ |
| Get own meal log | — | 🔒 | 🔒 | ❌ |
| Update meal | — | 🔒 | 🔒 | ❌ |
| Get/set meal schedule | — | 🔒 | 🔒 | ❌ |
| Scan meal (AI) | — | 🔒 | 🔒 | ❌ |

## 8. Social

| Action | public | member | pt | admin |
|---|---|---|---|---|
| Create post | — | 🔒 | 🔒 | ❌ |
| List posts (visibility filter) | — | ✅ | ✅ | ✅ |
| Get post detail | — | ✅ (visibility) | ✅ (visibility) | ✅ |
| Comment | — | 🔒 | 🔒 | ❌ |
| React | — | 🔒 | 🔒 | ❌ |
| Report content | — | 🔒 | 🔒 | 🔒 (self-report allowed) |
| Send friend request | — | 🔒 (block check 2 chiều) | 🔒 (block check 2 chiều) | ❌ |
| Accept/reject friend request | — | 🔒 (recipient only) | 🔒 (recipient only) | ❌ |
| Cancel friend request (sender) | — | 🔒 (sender only) | 🔒 (sender only) | ❌ |
| List friends | — | 🔒 | 🔒 | ❌ |
| Unfriend | — | 🔒 (either side) | 🔒 (either side) | ❌ |
| Get friend relation status | — | 🔒 | 🔒 | ❌ |
| Block / unblock user | — | 🔒 | 🔒 | ❌ |
| List blocked users | — | 🔒 | 🔒 | ❌ |
| Delete any post | — | ❌ | ❌ | ✅ (moderation) |

**Block note**: block chặn gửi/accept friend request + huỷ pending request + xoá friendship hiện tại. KHÔNG ảnh hưởng booking/finance. Xem `domain-rules.md §11.2`.

## 9. Chat

> BE-191 (friend-only, không AI). AI chat đang chờ provider (BE-430). Friend chat policy xem `domain-rules.md §12`.
>
> - Send friend message: chỉ friend (canonical pair) + không bị block 2 chiều.
> - Self-message: `400 FITMATE.CHAT.CANNOT_SELF_MESSAGE`.
> - Idempotency: `client_message_id` (DEC-043). Cùng key + khác payload → `409 idempotency_mismatch`.
> - Read marker monotonic: cũ hơn marker hiện tại → no-op 204 (không lùi).
> - Block / unfriend KHÔNG xoá conversation; chỉ chặn send (DEC-045).
> - Admin KHÔNG đọc nội dung chat mặc định; chỉ với moderation audit.

| Action | public | member | pt | admin |
|---|---|---|---|---|
| Send AI message | — | 🔒 | 🔒 | ❌ |
| List AI threads | — | 🔒 | 🔒 | ❌ |
| Get AI thread messages | — | 🔒 | 🔒 | 🛡️ (audit) |
| Send friend message (`POST /chat/friends/messages`) | — | 🔒 (friend only, no block) | 🔒 (friend only, no block) | ❌ |
| List conversations (`GET /chat/friends/conversations`) | — | 🔒 | 🔒 | ❌ |
| Get conversation (`GET /chat/friends/conversations/{id}`) | — | 🔒 (participant) | 🔒 (participant) | ❌ |
| List messages (`GET /chat/friends/conversations/{id}/messages`) | — | 🔒 (participant) | 🔒 (participant) | 🛡️ (audit) |
| Mark read (`POST /chat/friends/conversations/{id}/read`) | — | 🔒 (participant) | 🔒 (participant) | ❌ |
| Read any conversation | — | ❌ | ❌ | 🛡️ (audit) |

**Participant check**: `GET .../conversations/{id}` + `.../messages` + `.../read` đều yêu cầu caller là 1 trong 2 participant. Không phải participant → `404` (không leak existence).

**Idempotency**: cùng `(conversation_id, client_message_id)` + cùng payload → trả row cũ. Khác payload → `409 FITMATE.CHAT.IDEMPOTENCY_MISMATCH`. Không tự retry ở server — client dùng `client_message_id` cho retry semantics.

**Marker monotonic**: `last_read_message_id` không dùng để so sánh thứ tự (UUID không order-stable). So sánh `candidate_message.CreatedAt > current.LastReadAt`.

## 10. Notifications

> Device registration: token stored encrypted (DataProtection, DEC-040). Idempotent per (userId, tokenHash). Soft-revoke stops push delivery; in-app notifications still work when push is disabled.
> Producer wiring (FriendService.Accept + ModerationService.Warn → `IPushDeliveryDispatcher`) gắn `source_event_id` (UUID of FriendRequest / Report) + `source_type` ('friend_request_accepted' | 'moderation_warning') để dedup qua partial unique index `(user_id, source_event_id, source_type)`. Worker re-fetch notification theo (recipientId, source_event_id, source_type) trước khi fan-out push.

| Action | public | member | pt | admin |
|---|---|---|---|---|
| List own notifications (`GET /me/notifications`) | — | 🔒 | 🔒 | ❌ |
| Mark read (`POST /me/notifications/{id}/read`) | — | 🔒 | 🔒 | ❌ |
| Register device (`POST /users/me/devices`) | — | 🔒 | 🔒 | ❌ |
| Revoke device (`DELETE /users/me/devices/{id}`) | — | 🔒 (owner only — wrong owner → 404) | 🔒 (owner only) | ❌ |

**Device owner check**: `DELETE /users/me/devices/{id}` trả 404 nếu device không thuộc về user (không leak existence). Admin không có quyền revoke device của người khác.

**Producer dedup**: FriendService.Accept và ModerationService.Warn gọi `IPushDeliveryDispatcher.DispatchAsync` với `(source_event_id, source_type)`. DbContext unique partial index `(user_id, source_event_id, source_type) WHERE source_event_id IS NOT NULL` đảm bảo 1 notification per (recipient, source_event). Idempotent: chạy lại path không tạo duplicate row.

## 11. Moderation

> Theo DEC-028: `ReportTargetType ∈ {Post, Comment, User, Certificate}`. PT map sang User; targetUserRole trả về read-only.
> **Block user là action riêng** — report user không tự động khóa. Số report là tín hiệu ưu tiên, admin resolve từng case.

| Action | public | member | pt | admin |
|---|---|---|---|---|
| Create report (`POST /reports`) — visibility check target | — | 🔒 | 🔒 | 🔒 (self-report) |
| List reports (`GET /admin/reports`) | — | ❌ | ❌ | ✅ |
| Resolve report (`POST /admin/reports/{id}/resolve`) | — | ❌ | ❌ | ✅ (audit; có thể trigger lock/delete) |
| Dismiss report (`POST /admin/reports/{id}/dismiss`) | — | ❌ | ❌ | ✅ (audit) |
| Block user (`POST /admin/users/{id}/lock`) — **không tự động** | — | ❌ | ❌ | ✅ |
| Read audit log (`GET /admin/audit`) | — | ❌ | ❌ | ✅ |

## 12. Field-level permissions

| Field | Ai được đọc | Ghi chú |
|---|---|---|
| `password_hash` | Không ai qua API | Internal only. |
| `email` | Owner; admin (audit) | Không cho PT khác. |
| `phone` | Owner; admin | — |
| `health_data.*` | Owner; admin 👁️ (aggregate) | Admin không đọc raw. |
| `chat_messages.content` | Sender, receiver; admin 🛡️ | Admin mở qua moderation exception. |
| `payment.provider_payload` | Admin (audit) | Không trả qua API thường. |
| `certificate.image_url` | Public sau duyệt; admin | Signed URL TTL. |

## 13. Admin bootstrap & MFA

- Admin đầu tiên không có password mặc định. Tạo qua CLI `dotnet run -- create-admin --email ...` đọc password từ stdin hoặc env.
- Mọi login admin phải đi qua MFA (TOTP) trước khi cấp token (đề xuất; cần user chốt).
- Admin refresh token TTL 1 giờ; access 5 phút.
- Admin endpoint yêu cầu IP allowlist (đề xuất; cần user chốt).
