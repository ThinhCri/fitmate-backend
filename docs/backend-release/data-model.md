# Data Model & ERD (đề xuất)

> Mỗi bounded context có schema riêng trong Postgres. Chỉ liệt kê các aggregate chính; chi tiết FK + index sẽ được implement khi viết EF Core entities.

## Schemas

| Schema | Bounded context | Aggregate roots chính |
|---|---|---|
| `identity` | users, profile, sessions | User, UserProfile, RefreshToken, AuditLog |
| `trainers` | PT, certificates, services, availability | PtProfile, Certificate, Service, AvailabilitySlot |
| `booking` | appointments, holds | Appointment, BookingHold, AppointmentAudit |
| `payment` | transactions, refunds | Payment, Refund, PaymentAttempt |
| `finance` | ledger (read projections) | LedgerEntry |
| `workout` | plans, exercises, sessions, weight | WorkoutPlan, Exercise, WorkoutSession, WeightHistory |
| `nutrition` | meals, schedule | MealLog, MealSchedule |
| `social` | posts, comments, reactions, friend | Post, Comment, Reaction, FriendRequest, Friend |
| `chat` | AI + friend chat | ChatThread, ChatMessage |
| `notifications` | system + reminder | Notification, Device |
| `moderation` | reports | Report |
| `admin` | admin-specific | AdminUser, AdminAudit |
| `outbox` | outbox + idempotency | OutboxMessage, IdempotencyRecord |

## ERD (rút gọn)

```
identity.users ──< identity.user_profile
identity.users ──< identity.refresh_tokens
identity.users ──< identity.audit_log (actor)

trainers.pt_profile (1-1) ── identity.users
trainers.certificates ──< trainers.pt_profile
trainers.services ──< trainers.pt_profile
trainers.availability_slots ──< trainers.pt_profile

booking.appointments ──< identity.users (member_id)
booking.appointments ──< identity.users (pt_id, qua trainers.pt_profile.user_id)
booking.booking_holds ──< trainers.pt_profile

payment.payments ──< booking.appointments
payment.refunds ──< payment.payments
finance.ledger_entries ──< payment.payments, payment.refunds

workout.workout_plans ──< identity.users
workout.exercises ──< workout.workout_plans
workout.workout_sessions ──< identity.users, workout.workout_plans
workout.weight_history ──< identity.users

nutrition.meal_logs ──< identity.users
nutrition.meal_schedules ──< identity.users

social.posts ──< identity.users
social.comments ──< social.posts
social.reactions ──< social.posts
social.friend_requests ──< identity.users (from, to)
social.friends ──< identity.users

chat.chat_threads ──< identity.users (member_id)
chat.chat_messages ──< chat.chat_threads

notifications.notifications ──< identity.users
notifications.devices ──< identity.users

moderation.reports (target polymorphic: post_id/comment_id/user_id)

outbox.outbox_messages
outbox.idempotency_records
```

## Ràng buộc chính (sẽ implement trong EF)

| Bảng | Ràng buộc |
|---|---|
| `identity.users.email` | UNIQUE |
| `identity.refresh_tokens.token_hash` | UNIQUE, FK → users |
| `trainers.certificates.(pt_id, status)` | Index cho query pending |
| `booking.appointments.(pt_user_id, slot_start_utc)` | Unique partial index khi status ∈ {held,awaiting_payment,paid,completed} |
| `booking.booking_holds.(pt_id, slot_start_utc)` | Unique partial index khi status='active' và expires_at > now |
| `payment.payments.appointment_id` | UNIQUE (1 payment per booking) |
| `payment.refunds.(payment_id)` | Index, sum ≤ actual_paid_vnd enforced trong app |
| `social.friends.(user_id, friend_id)` | UNIQUE, CHECK user_id ≠ friend_id |
| `social.friend_requests.(from_id, to_id)` | UNIQUE khi status='pending' |
| `social.reactions.(post_id, user_id)` | UNIQUE |
| `chat.chat_messages.thread_id, created_at` | Index cho pagination |
| `outbox.idempotency_records.key` | UNIQUE, TTL |
| `outbox.outbox_messages.status, next_attempt_at` | Index cho worker |

## Audit & soft delete

- `created_at`, `updated_at` trên mọi bảng.
- `deleted_at` (nullable) cho soft-delete: users, posts, comments, services.
- Hard delete chỉ cho: idempotency_records (sau TTL), refresh_tokens (sau revoke), outbox_messages (sau dispatch).

## PII / sensitive

| Cột | Bảng | Loại |
|---|---|---|
| `email` | users | PII |
| `phone` | user_profile | PII |
| `password_hash` | users | secret |
| `health_*` | user_profile, weight_history, meal_logs | health |
| `content` | chat_messages, posts, comments | private |
| `provider_payload` | payments | financial |
| `token_hash` | refresh_tokens | credential |

Tất cả cột trên đều redact khi log; mọi select ra response đều đi qua allowlist.

## Partition / archive

- `chat_messages`: partition theo `created_at` tháng (Postgres native partitioning) khi đạt ngưỡng.
- `audit_log`: partition theo `created_at` tháng.
- `outbox_messages`: archive sau khi status='done' quá 30 ngày.

## Retention (đề xuất — cần user chốt)

| Loại | Giữ |
|---|---|
| User profile + weight + meals | theo account lifecycle (giữ 30 ngày sau delete, sau đó anonymize) |
| Chat messages | 90 ngày sau user delete |
| Payment + refund | 5 năm (tuân thủ kế toán VN) |
| Audit log | 3 năm |
| Workout sessions | theo account lifecycle |
| Posts / comments | theo account lifecycle hoặc moderation |
| Idempotency records | 24 giờ |
| Refresh tokens (revoked) | 30 ngày |
