# Feature → API → Test Matrix

> Bảng liệt kê toàn bộ nghiệp vụ cần có trên BE, ánh xạ sang endpoint, test case, và trạng thái hiện tại.

## Quy ước trạng thái

- **Đã đúng**: endpoint có trong OpenAPI, đã implement, test pass.
- **Khác contract**: đã có endpoint nhưng input/output khác FE cần → ghi rõ delta.
- **Thiếu quyền / nghiệp vụ**: có endpoint nhưng authorization hoặc state transition chưa đúng.
- **Chưa có**: nghiệp vụ FE cần, BE chưa có.
- **Phụ thuộc dịch vụ ngoài**: cần provider (payment, email, AI) — sẽ làm ở giai đoạn 2.
- **Cần quyết định sản phẩm**: nghiệp vụ mơ hồ, cần user chốt.

## Vai trò

- `member` — hội viên
- `pt` — huấn luyện viên (có thêm `isVerified`)
- `admin` — quản trị viên nền tảng
- `public` — không cần auth
- `owner` — object-level (member chỉ đọc data của mình)

---

## 1. Identity (Auth & User)

| FID | Client | Vai trò | Màn hình / Luồng | File nguồn (FE/Web) | Input | Output | Endpoint | Quyền | BR / State | BE hiện | Task | Test | Blocker |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| FP-ID-01 | Cả hai | public | Login | `mobile/app/(auth)/login.tsx`, `web/src/components/Login.jsx` | email, password | access_token, refresh_token, user | POST `/api/v1/auth/login` | public | bcrypt verify, không lộ user existence | Scaffold cũ có /api/auth/login trả UUID token (không JWT) | BE-101 | success, wrong pwd, locked, unknown email, rate limit | — |
| FP-ID-02 | Cả hai | public | Register | `mobile/app/(auth)/register.tsx` | email, password, profile, optional PT data | user + tokens | POST `/api/v1/auth/register` | public | email unique, pwd ≥ 8, role=USER hoặc PT (PT gửi kèm cert → pending) | Có /api/auth/register | BE-101 | duplicate email, weak pwd, invalid email, PT with missing cert | Email verification (chưa có) |
| FP-ID-03 | Cả hai | public | Refresh token | app client | refresh_token | access_token, refresh_token mới | POST `/api/v1/auth/refresh` | public | rotate refresh token, revoke cũ | Chưa có | BE-102 | expired, revoked, replay | — |
| FP-ID-04 | Cả hai | owner | Logout | app client | access_token | 204 | POST `/api/v1/auth/logout` | owner | revoke session hiện tại | Chưa có | BE-102 | already revoked | — |
| FP-ID-05 | App | owner | Get my profile | `mobile/app/(tabs)/dashboard.tsx`, profile | — | user profile | GET `/api/v1/users/me` | owner | id = current user | Có /api/user/profile/{id} (FE dùng id tự gửi → ownership chưa enforce) | BE-103 | invalid token | — |
| FP-ID-06 | App | owner | Update my profile | profile/edit | partial profile | user profile | PATCH `/api/v1/users/me` | owner | chỉ member/PT tự sửa, không tự sửa role | Chưa có | BE-103 | forbidden role, invalid birthday | — |
| FP-ID-07 | App | public | Forgot password | auth | email | 202 | POST `/api/v1/auth/password/forgot` | public | rate limit, không lộ email có tồn tại | Chưa có | BE-104 | unknown email, rate limit | Email provider |
| FP-ID-08 | App | public | Reset password | auth | token, new_password | 204 | POST `/api/v1/auth/password/reset` | public | token dùng 1 lần, expire 30 phút | Chưa có | BE-104 | expired token, weak pwd | Email provider |
| FP-ID-09 | App | owner | Change password | settings | current, new | 204 | POST `/api/v1/users/me/password` | owner | yêu cầu current pwd | Chưa có | BE-104 | wrong current | — |
| FP-ID-10 | Admin | admin | Lock / unlock user | `UserManagement.jsx` | user_id, reason, lock=true/false | user | POST `/api/v1/admin/users/{id}/lock` | admin | audit, revoke session nếu lock | Chưa có | BE-110 | unknown user, already locked | — |
| FP-ID-11 | Owner | owner | Delete my account | settings | password confirmation | 204 | DELETE `/api/v1/users/me` | owner | xóa theo retention policy; audit; không xóa lịch sử tài chính | Chưa có | BE-105 | wrong pwd | Retention policy (chưa có) |
| FP-ID-12 | Admin | admin | List users | `UserManagement.jsx` | filter role/status, search, page | page<users> | GET `/api/v1/admin/users` | admin | allowlist fields; sort/page server | Chưa có | BE-110 | page size, invalid filter | — |

---

## 2. Trainer (PT)

| FID | Client | Vai trò | Màn hình / Luồng | File nguồn | Input | Output | Endpoint | Quyền | BR / State | BE hiện | Task | Test | Blocker |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| FP-TR-01 | App | member | List PT (marketplace) | `mobile/app/(tabs)/marketplace.tsx` | filter specialty, price, rating, page | page<pt> | GET `/api/v1/trainers` | member | chỉ trả verified=true | Chưa có | BE-120 | unverified PT not listed, sort | — |
| FP-TR-02 | App | member | Get PT detail | marketplace | id | pt profile + services | GET `/api/v1/trainers/{id}` | member | chỉ verified | Chưa có | BE-120 | unknown id | — |
| FP-TR-03 | App | pt | Submit certificate | profile, certificates | image, title, issued_by, code, issue_date | certificate | POST `/api/v1/me/certificates` | pt | status=pending; admin review | Có entity PtProfile.isVerified=true ngay khi register PT → không đúng BR | BE-121 | invalid image, missing field | Upload |
| FP-TR-04 | App | pt | List my certificates | certificates | — | cert[] | GET `/api/v1/me/certificates` | pt owner | — | Chưa có | BE-121 | — | — |
| FP-TR-05 | Admin | admin | List pending certificates | `TrainerManagement.jsx`, `CertificateModeration.jsx` | status=pending, page | page<cert> | GET `/api/v1/admin/certificates?status=pending` | admin | sort by submitted_at | Chưa có | BE-122 | page size | — |
| FP-TR-06 | Admin | admin | Approve certificate | `TrainerManagement.jsx` | id | cert + pt profile updated | POST `/api/v1/admin/certificates/{id}/approve` | admin | set verified=true, verified_at, audit | Chưa có | BE-122 | already approved | — |
| FP-TR-07 | Admin | admin | Reject certificate | `TrainerManagement.jsx` | id, reason | cert | POST `/api/v1/admin/certificates/{id}/reject` | admin | reason bắt buộc; audit | Chưa có | BE-122 | missing reason | — |
| FP-TR-08 | App | pt | Set my availability | appointments | slot[] | 204 | PUT `/api/v1/me/availability` | pt owner | validate không trùng | Chưa có | BE-123 | overlapping | — |
| FP-TR-09 | App | member | List PT availability | marketplace | pt_id, date range | slot[] | GET `/api/v1/trainers/{id}/availability` | member | chỉ slot future, status=open | Chưa có | BE-123 | past slot | — |
| FP-TR-10 | App | pt | List my services | services | — | service[] | GET `/api/v1/me/services` | pt owner | — | Chưa có | BE-124 | — | — |
| FP-TR-11 | App | pt | Create service | services | name, price_vnd, duration_min | service | POST `/api/v1/me/services` | pt owner | price ≥ min, ≤ max | Chưa có | BE-124 | invalid price | — |
| FP-TR-12 | App | pt | Update service | services | id, partial | service | PATCH `/api/v1/me/services/{id}` | pt owner | không sửa nếu có booking pending | Chưa có | BE-124 | has pending booking | — |

---

## 3. Booking

| FID | Client | Vai trò | Màn hình / Luồng | File nguồn | Input | Output | Endpoint | Quyền | BR / State | BE hiện | Task | Test | Blocker |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| FP-BK-01 | App | member | Hold slot | marketplace | pt_id, service_id, slot_start | hold{id, expires_at} | POST `/api/v1/bookings/holds` | member | TTL 5 phút, unique (pt_id, slot_start) khi hold active | Chưa có | BE-130 | duplicate slot | — |
| FP-BK-02 | App | member | Confirm booking (after payment) | app booking | hold_id, payment_id | appointment | POST `/api/v1/bookings/confirm` | member owner | `AppointmentStatus` Held/Pending → Pending; chỉ confirm khi `PaymentStatus = Paid` | Chưa có | BE-130 | hold expired, `PaymentStatus != Paid` | Payment |
| FP-BK-03 | App | member | List my bookings | `mobile/app/(tabs)/dashboard.tsx` (recent) | filter status, page | page<appointment> | GET `/api/v1/me/bookings` | member owner | — | Chưa có | BE-131 | status filter | — |
| FP-BK-04 | App | pt | List my bookings | `mobile/app/(modals)/appointments.tsx` | filter status, page | page<appointment> | GET `/api/v1/me/trainer-bookings` | pt owner | — | Chưa có | BE-131 | — | — |
| FP-BK-05 | App | member | Cancel booking (trước hoàn thành) | appointment | id, reason | appointment | POST `/api/v1/me/bookings/{id}/cancel` | member owner | `AppointmentStatus` → Cancelled; refund policy xem `domain-rules.md` §3 | Chưa có | BE-132 | already completed | — |
| FP-BK-06 | App | pt | Accept pending booking | appointment | id | appointment | POST `/api/v1/me/bookings/{id}/accept` | pt owner | Pending → Confirmed | Chưa có | BE-133 | không phải PT của booking | — |
| FP-BK-07 | App | pt | Confirm completion | appointment | id | appointment | POST `/api/v1/me/bookings/{id}/complete` | pt owner | InProgress → Completed; cần `PaymentStatus = Paid` | Chưa có | BE-133 | not PT của booking, payment chưa paid | — |
| FP-BK-08 | Admin | admin | List bookings | `BookingManagement.jsx` | filter status, pt, page | page<appointment> | GET `/api/v1/admin/bookings` | admin | server filter/sort theo `AppointmentStatus` mới + `PaymentStatus` | Chưa có | BE-134 | page size | — |
| FP-BK-09 | Admin | admin | Force refund | `BookingManagement.jsx` | id, amount, reason | refund + ledger | POST `/api/v1/admin/bookings/{id}/refund` | admin | audit; partial/full; ledger adjust; `RefundStatus` enum | Chưa có | BE-134 | already refunded | Payment |
| FP-BK-10 | App | member | Reschedule booking | appointment | id, new_slot_start | appointment | POST `/api/v1/me/bookings/{id}/reschedule` | member owner | tạo `BookingRescheduleEvent`; transaction check slot mới; release cũ an toàn | Chưa có | BE-135 | too late | — |
| FP-BK-11 | App | member | Set late attendance flag | appointment | id, late=true/false | appointment | POST `/api/v1/me/bookings/{id}/late` | member owner | flag `is_late_attendee`; server KHÔNG tự set quá giờ | Chưa có | BE-135a | — | — |

---

## 4. Payment

| FID | Client | Vai trò | Màn hình / Luồng | File nguồn | Input | Output | Endpoint | Quyền | BR / State | BE hiện | Task | Test | Blocker |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| FP-PAY-01 | App | member | Create payment intent | booking | hold_id, method=`vnpay` | payment{id, redirect_url} | POST `/api/v1/payments` | member owner | method whitelist (chỉ `vnpay` ở release đầu — DEC-027); idempotency | Chưa có | BE-140 | method != vnpay → 400 PROVIDER_NOT_SUPPORTED | VNPay |
| FP-PAY-02 | Public (VNPay) | public | Return URL | — | query | redirect | GET `/api/v1/payments/vnpay/return` | public | verify signature; status | Chưa có | BE-141 | bad signature | VNPay |
| FP-PAY-03 | Public (VNPay webhook) | public | IPN | — | body | 200 | POST `/api/v1/payments/vnpay/ipn` | public | verify HMAC; idempotent; chỉ nhận 1 lần | Chưa có | BE-141 | duplicate, bad sig | VNPay |
| FP-PAY-04 | Member | owner | Get my payment | — | id | payment | GET `/api/v1/me/payments/{id}` | owner | — | Chưa có | BE-142 | unknown id | — |
| FP-PAY-05 | Admin | admin | List payments | finance | filter status, page | page<payment> | GET `/api/v1/admin/payments` | admin | — | Chưa có | BE-143 | page size | — |
| FP-PAY-06 | Admin | admin | Trigger reconciliation job | finance | date range | job_id | POST `/api/v1/admin/payments/reconcile` | admin | — | Chưa có | BE-143 | — | VNPay |

---

## 5. Finance

| FID | Client | Vai trò | Màn hình / Luồng | File nguồn | Input | Output | Endpoint | Quyền | BR / State | BE hiện | Task | Test | Blocker |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| FP-FN-01 | Admin | admin | Overview KPIs | `Overview.jsx` | time range (7d/30d/all) | KPIs + chart | GET `/api/v1/admin/finance/overview` | admin | GMV (paid), Platform fee 10% (paid complete + paid not yet complete?), trainer share, refunds; theo `domain-rules.md` §3 | Chưa có | BE-150 | empty, all refunded | — |
| FP-FN-02 | Admin | admin | Finance ledger | `FinanceManagement.jsx` | filter status, page | page<ledger> | GET `/api/v1/admin/finance/ledger` | admin | sort by date desc | Chưa có | BE-150 | page size | — |
| FP-FN-03 | Admin | admin | PT earnings | `FinancialReports.jsx` | pt_id, date range | sum, list | GET `/api/v1/admin/finance/pt/{id}/earnings` | admin | — | Chưa có | BE-150 | unknown pt | — |
| FP-FN-04 | App | pt | My revenue | `mobile/app/(modals)/revenue/` | time range | KPIs + history | GET `/api/v1/me/revenue` | pt owner | chỉ booking với `PaymentStatus = Paid` + `AppointmentStatus = Completed` mới ghi nhận; refund là điều chỉnh | Chưa có | BE-151 | empty | — |
| FP-FN-05 | App | pt | My revenue detail (per booking) | revenue | booking_id | detail | GET `/api/v1/me/revenue/{bookingId}` | pt owner | breakdown fee/pt/refund | Chưa có | BE-151 | unknown booking | — |
| FP-FN-06 | Admin | admin | Read platform policy (read-only) | `Settings.jsx` | — | policy | GET `/api/v1/admin/platform-policy` | admin | DEC-029: read-only. Trả `platform_fee_rate_bps` + `policy_version` + `effective_from` + `editable=false` | Chưa có | BE-150 | — | — |

---

## 6. Workout

| FID | Client | Vai trò | Màn hình / Luồng | File nguồn | Input | Output | Endpoint | Quyền | BR / State | BE hiện | Task | Test | Blocker |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| FP-WK-01 | App | member | Get my workout plans | workout | week | plans[] | GET `/api/v1/me/workout/plans` | member owner | — | Có entity WorkoutPlan | BE-160 | — | — |
| FP-WK-02 | App | member | Create workout plan | workout | plan + exercises | plan | POST `/api/v1/me/workout/plans` | member owner | — | Chưa có | BE-160 | invalid exercises | — |
| FP-WK-03 | App | member | Log workout session | workout session | plan_id, completed sets, duration, calories | session | POST `/api/v1/me/workout/sessions` | member owner | idempotent qua client-generated id | Có WorkoutLog (entity cũ chưa có session result semantics) | BE-161 | duplicate id | — |
| FP-WK-04 | App | member | List workout sessions | workout | date range | session[] | GET `/api/v1/me/workout/sessions` | member owner | — | Chưa có | BE-161 | — | — |
| FP-WK-05 | App | member | Log weight | profile | weight_kg, recorded_at | weight | POST `/api/v1/me/weight` | member owner | — | Có WeightHistory | BE-162 | invalid value | — |
| FP-WK-06 | App | member | Get weight history | profile | date range | weight[] | GET `/api/v1/me/weight` | member owner | — | Có WeightHistory | BE-162 | — | — |

---

## 7. Nutrition

| FID | Client | Vai trò | Màn hình / Luồng | File nguồn | Input | Output | Endpoint | Quyền | BR / State | BE hiện | Task | Test | Blocker |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| FP-NT-01 | App | member | Log meal | nutrition | food_name, calories, macros, image?, source (manual/dataset/ai_estimate) | meal | POST `/api/v1/me/nutrition/meals` | member owner | flag source rõ; AI estimate có confidence | Có MealLog | BE-170 | invalid macros | Upload (image) |
| FP-NT-02 | App | member | Get meal log | nutrition | date range | meal[] | GET `/api/v1/me/nutrition/meals` | member owner | — | Có MealLog | BE-170 | — | — |
| FP-NT-03 | App | member | Update meal | nutrition | id, partial, confirmed (nếu là AI estimate) | meal | PATCH `/api/v1/me/nutrition/meals/{id}` | member owner | — | Chưa có | BE-170 | — | — |
| FP-NT-04 | App | member | Get/set meal schedule | nutrition | — | schedule | GET/PUT `/api/v1/me/nutrition/schedule` | member owner | — | Có MealSchedule | BE-171 | — | — |
| FP-NT-05 | App | member | Scan meal (AI estimate) | nutrition | image | estimate{calories, macros, confidence, items} | POST `/api/v1/me/nutrition/scan` | member owner | không lưu kết quả; user xác nhận mới log | Chưa có | BE-172 | — | AI |

---

## 8. Social

| FID | Client | Vai trò | Màn hình / Luồng | File nguồn | Input | Output | Endpoint | Quyền | BR / State | BE hiện | Task | Test | Blocker |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| FP-SC-01 | App | owner | Create post | social | content, image?, link?, tags, visibility | post | POST `/api/v1/social/posts` | owner | visibility whitelist | Có SocialPost | BE-180 | invalid visibility | Upload |
| FP-SC-02 | App | public | List posts | social | filter visibility, page | page<post> | GET `/api/v1/social/posts` | member | public posts + friends; privacy enforced | Chưa có | BE-180 | privacy | — |
| FP-SC-03 | App | owner | Get post detail | social | id | post + comments | GET `/api/v1/social/posts/{id}` | member | privacy | Chưa có | BE-180 | unknown id | — |
| FP-SC-04 | App | owner | Comment | social | post_id, text | comment | POST `/api/v1/social/posts/{id}/comments` | member | — | Chưa có | BE-181 | unknown post | — |
| FP-SC-05 | App | owner | React | social | post_id, type | 204 | POST `/api/v1/social/posts/{id}/reactions` | member | one reaction per user per post | Chưa có | BE-181 | duplicate | — |
| FP-SC-06 | App | owner | Report post/comment/user/certificate | social | target_id, target_type∈{post,comment,user,certificate}, reason | report | POST `/api/v1/social/reports` | member | DEC-028: 4 value, PT map sang user; reporter visibility-check; reason `minLength=5, maxLength=500`; idempotent (cùng reporter+target+status=Open → 409); không auto-lock user | Chưa có | BE-182 | — | — |
| FP-SC-07 | App | owner | Send friend request | social | to_user_id | friend_request | POST `/api/v1/social/friends/requests` | member | idempotent; block check 2 chiều; canonical pair; outbox event | Đã có (BE-181) | BE-181 | self, blocked, already friend, cross | — |
| FP-SC-08 | App | owner | Accept / reject / cancel friend request | social | id, action∈{accept,reject,cancel} | friend | POST `/api/v1/social/friends/requests/{id}` | member owner (recipient for accept/reject, sender for cancel) | atomic + block re-check; KHÔNG notify sender on reject | Đã có (BE-181) | BE-181 | not owner, already processed, concurrent block | — |
| FP-SC-09 | App | owner | List friends | social | cursor, limit | page<friend> | GET `/api/v1/social/friends` | member owner | cursor pagination; canonical pair query | Đã có (BE-181) | BE-181 | — | — |
| FP-SC-09a | App | owner | Unfriend | social | user_id | 204 | DELETE `/api/v1/social/friends/{user_id}` | member owner | idempotent; outbox `friendship.removed` (no notification) | Đã có (BE-181) | BE-181 | — | — |
| FP-SC-09b | App | owner | Friend relation status | social | user_id | relation_view | GET `/api/v1/social/friends/status/{user_id}` | member owner | `none\|outgoing_pending\|incoming_pending\|friends\|blocked_either` | Đã có (BE-181) | BE-181 | — | — |
| FP-SC-09c | App | owner | Block user | social | user_id | 204 | POST `/api/v1/social/blocks` | member owner | cancel pending + remove friendship; outbox `user.blocked` | Đã có (BE-181) | BE-181 | self, unknown, already blocked | — |
| FP-SC-09d | App | owner | Unblock user | social | user_id | 204 | DELETE `/api/v1/social/blocks/{user_id}` | member owner | KHÔNG restore friendship; outbox `user.unblocked` | Đã có (BE-181) | BE-181 | not blocked | — |
| FP-SC-09e | App | owner | List blocked users | social | cursor, limit | page<blocked> | GET `/api/v1/social/blocks` | member owner | cursor pagination | Đã có (BE-181) | BE-181 | — | — |

---

## 9. Chat

| FID | Client | Vai trò | Màn hình / Luồng | File nguồn | Input | Output | Endpoint | Quyền | BR / State | BE hiện | Task | Test | Blocker |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| FP-CH-01 | App | member | Send AI message | chat | text | ai_reply | POST `/api/v1/chat/ai/messages` | member owner | rate limit; không lưu payload nhạy cảm | Có ChatMessage.isAiCompanion | BE-190 | AI unavailable | AI |
| FP-CH-02 | App | member | List AI chat threads | chat | — | thread[] | GET `/api/v1/chat/ai/threads` | member owner | — | Chưa có | BE-190 | — | — |
| FP-CH-03 | App | member | List AI chat messages | chat | thread_id | message[] | GET `/api/v1/chat/ai/threads/{id}/messages` | member owner | pagination | Chưa có | BE-190 | — | — |
| FP-CH-04 | App | member | Send friend message | chat | peer_user_id, text, client_message_id? | message | POST `/api/v1/chat/friends/messages` | member owner | chỉ friend; không block 2 chiều; idempotent qua client_message_id | Có FriendConversation + ChatMessage.ConversationId | BE-191 | not friend, blocked, idempotency_mismatch | — |
| FP-CH-05 | App | member | List conversations | chat | — | conversation[] | GET `/api/v1/chat/friends/conversations` | member owner | cursor pagination; unread_count | Có ChatThreadRead + ConversationDto | BE-191 | — | — |
| FP-CH-06 | App | member | List friend messages | chat | conversation_id, cursor?, limit? | message[] | GET `/api/v1/chat/friends/conversations/{id}/messages` | participant only | cursor pagination | Có ChatMessage.ConversationId | BE-191 | not participant | — |
| FP-CH-07 | App | member | Mark friend messages read | chat | conversation_id, last_read_message_id | — | POST `/api/v1/chat/friends/conversations/{id}/read` | participant only | monotonic; no-op nếu cũ hơn marker | Có ChatThreadRead | BE-191 | message not in conversation | — |

---

## 10. Notifications

| FID | Client | Vai trò | Màn hình / Luồng | File nguồn | Input | Output | Endpoint | Quyền | BR / State | BE hiện | Task | Test | Blocker |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| FP-NF-01 | App | owner | List my notifications | notifications | filter read, page | page<notification> | GET `/api/v1/me/notifications` | owner | — | Có entity Notification (chưa có schema) | BE-200 | — | — |
| FP-NF-02 | App | owner | Mark read | notifications | id | 204 | POST `/api/v1/me/notifications/{id}/read` | owner | — | Chưa có | BE-200 | — | — |
| FP-NF-03 | App | owner | Push register | — | device_token, platform | 204 | POST `/api/v1/me/devices` | owner | device token hashed | Chưa có | BE-201 | duplicate | Push provider |

---

## 11. Moderation (admin)

| FID | Client | Vai trò | Màn hình / Luồng | File nguồn | Input | Output | Endpoint | Quyền | BR / State | BE hiện | Task | Test | Blocker |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| FP-MD-01 | Admin | admin | List reports | `ViolationReports.jsx`, `PostModeration.jsx`, `CertificateModeration.jsx` | status, page | page<report> | GET `/api/v1/admin/reports` | admin | — | Chưa có | BE-210 | — | — |
| FP-MD-02 | Admin | admin | Resolve report | `ViolationReports.jsx` | id, action (warn/lock/delete), note | report | POST `/api/v1/admin/reports/{id}/resolve` | admin | audit; trigger side effect (lock/delete) | Chưa có | BE-210 | — | — |
| FP-MD-03 | Admin | admin | Dismiss report | `ViolationReports.jsx` | id, note | report | POST `/api/v1/admin/reports/{id}/dismiss` | admin | audit | Chưa có | BE-210 | — | — |
| FP-MD-04 | Admin | admin | List audit log | admin | actor, action, date range, page | page<audit> | GET `/api/v1/admin/audit` | admin | — | Chưa có | BE-211 | — | — |

---

## 12. Admin Dashboard / Analytics

| FID | Client | Vai trò | Màn hình / Luồng | File nguồn | Input | Output | Endpoint | Quyền | BR / State | BE hiện | Task | Test | Blocker |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| FP-AD-01 | Admin | admin | Members count | `Overview.jsx` | time range | count | GET `/api/v1/admin/metrics/members` | admin | — | Chưa có | BE-220 | — | — |
| FP-AD-02 | Admin | admin | Active PTs count | `Overview.jsx` | time range | count | GET `/api/v1/admin/metrics/active-pts` | admin | isVerified=true | Chưa có | BE-220 | — | — |
| FP-AD-03 | Admin | admin | Bookings in period | `Overview.jsx` | range | count + chart | GET `/api/v1/admin/metrics/bookings` | admin | server aggregation | Chưa có | BE-220 | — | — |
| FP-AD-04 | Admin | admin | Platform fee in period | `Overview.jsx` | range | amount + chart | GET `/api/v1/admin/metrics/platform-fee` | admin | theo `domain-rules.md` §3 | Chưa có | BE-220 | — | — |
| FP-AD-05 | Admin | admin | AI token analytics | `AiTokenAnalytics.jsx` | range | usage, cost | GET `/api/v1/admin/metrics/ai-tokens` | admin | — | Chưa có | BE-220 | — | AI |
| FP-AD-06 | Admin | admin | Action required list | `Overview.jsx` | — | list | GET `/api/v1/admin/action-required` | admin | pending cert, pending reports, pending payments | Chưa có | BE-221 | — | — |
| FP-AD-07 | Admin | admin | Premium / VIP management | `PremiumManagement.jsx` | — | list | GET `/api/v1/admin/premium/users` | admin | — | Chưa có | BE-222 | — | Cần quyết định sản phẩm |

---

## Trạng thái tổng hợp (sẽ cập nhật sau khi có BE)

| Trạng thái | Đếm |
|---|---|
| Đã đúng | 0 |
| Khác contract | 1 (FP-TR-03: PT verify ngay khi register) |
| Thiếu quyền / nghiệp vụ | 1 (FP-ID-05: ownership chưa enforce) |
| Chưa có | ~50 |
| Phụ thuộc dịch vụ ngoài | ~10 (payment, AI, email, push, upload) |
| Cần quyết định sản phẩm | ~5 |

## Gap analysis từ khảo sát 2026-09-18

### Phát hiện từ audit-old-backend.md (BE Spring Boot cũ)

| Vấn đề | Ảnh hưởng đến feature matrix |
|---|---|
| BE cũ có 3 endpoint, OpenAPI mới có 69 endpoint. **~97% là viết mới**. | Status của hầu hết features giữ nguyên "Chưa có". |
| BE cũ tự động set `isVerified=true` khi PT đăng ký (không qua admin). | FP-TR-03 đã ghi "Khác contract" — giữ nguyên. |
| BE cũ KHÔNG có refresh token, session, audit. | FP-ID-03, FP-ID-04, FP-MD-04 vẫn "Chưa có". |
| BE cũ có `LocalDateTime` không có timezone. | Tất cả features có date/time ở mobile sẽ sai nếu reuse. Phải convert UTC. |
| BE cũ không có idempotency. | FP-BK-01 (hold slot), FP-PAY-01 (payment intent) — retry không an toàn. |

### Phát hiện từ mobile domain.ts

- `Appointment.status` mobile 6 ↔ web 4 ↔ BE 7 đã chốt ở DEC-026 (tách `AppointmentStatus` + `PaymentStatus` + `RefundStatus`). Mapping chi tiết: `api-contract.md §2.1`.
- `Appointment.timeSlot` mobile là string, cần tách thành `slotStartUtc` + `durationMinutes` ở BE.
- `WorkoutSession.status` mobile có `completed|endedEarly|skipped` ↔ BE ✅ match.
- `Cần chốt`: UA-018 (AppointmentStatus enum), UA-019 (payment method), UA-020 (report target type).
- **UA-018, UA-019, UA-020 đã chốt**: xem `decisions.md DEC-026/027/028` + `api-contract.md §2.1`. Matrix cập nhật với enum mới + transition matrix.

### Phát hiện từ web mockData.js

- `Booking.paymentMethod` (web) có 4 method (`FitMate Pay / QR / MoMo / Napas`) ↔ BE OpenAPI chỉ hỗ trợ `vnpay` ở release đầu (DEC-027). FE cần rút gọn chip về 1 ("Thanh toán qua VNPay"). Xem TODO `out-of-scope`.
- `Booking.location` (web) có sẵn ↔ BE chưa có field.
- `Booking.sessionSchedule` (web) là denormalized từ Service.schedule.
- `aiAnalyticsSummary` (web) có thống kê AI tokens, cost ↔ FP-AD-05 chưa rõ scope.
- `platformPolicy.platformFeeRate=10` (web, cố định) → BE thêm `GET /admin/platform-policy` (read-only, FP-FN-06 theo DEC-029). Không có POST để sửa ở release đầu.
- `ViolationReports.targetType` web: `post|user|pt` ↔ mobile: `post|comment|user|certificate` ↔ BE canonical 4 value (DEC-028); PT map sang user.
- `UserStatus` web: `active|suspended` ↔ BE OpenAPI: `Active|Suspended|PendingDeletion`. ✅ match.

### Endpoint cần thêm (chưa có trong matrix)

Sau audit, phát hiện FE sẽ cần thêm:
- `POST /api/v1/auth/email/verify` (email verification) — chưa list.
- `POST /api/v1/auth/email/resend` — chưa list.
- `GET /api/v1/me/profile/extended` (medical conditions + allergies đầy đủ).
- `GET /api/v1/trainers/{id}/reviews` (FE có `Review` entity).
- `POST /api/v1/trainers/{id}/reviews` (chỉ member đã book mới được review).
- `GET /api/v1/admin/users/{id}` (admin xem chi tiết user + lịch sử).
- `GET /api/v1/admin/users/{id}/audit-log` (audit riêng cho user).
- `GET /api/v1/admin/system/info` (admin xem version + region — web có `systemInfo`).
- `GET /api/v1/admin/platform-policy` (web có `platformPolicy`).

Sẽ thêm vào matrix ở phase tiếp theo.
