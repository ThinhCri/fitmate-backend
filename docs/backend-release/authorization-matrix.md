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
| Send friend request | — | 🔒 | 🔒 | ❌ |
| Accept/reject friend request | — | 🔒 | 🔒 | ❌ |
| List friends | — | 🔒 | 🔒 | ❌ |
| Delete any post | — | ❌ | ❌ | ✅ (moderation) |

## 9. Chat

| Action | public | member | pt | admin |
|---|---|---|---|---|
| Send AI message | — | 🔒 | 🔒 | ❌ |
| List AI threads | — | 🔒 | 🔒 | ❌ |
| Get AI thread messages | — | 🔒 | 🔒 | 🛡️ (audit) |
| Send friend message | — | 🔒 (friend only) | 🔒 (friend only) | ❌ |
| List conversations | — | 🔒 | 🔒 | ❌ |
| Read any conversation | — | ❌ | ❌ | 🛡️ (audit) |

## 10. Notifications

| Action | public | member | pt | admin |
|---|---|---|---|---|
| List own notifications | — | 🔒 | 🔒 | ❌ |
| Mark read | — | 🔒 | 🔒 | ❌ |
| Push register device | — | 🔒 | 🔒 | ❌ |

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
