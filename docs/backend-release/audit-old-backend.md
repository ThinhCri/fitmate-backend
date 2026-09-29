# Backend cũ — Kiểm kê thực tế

> Baseline update 2026-09-22: tài liệu này giữ chi tiết/lịch sử; khi có mâu thuẫn, dùng [README](README.md), [domain rules](domain-rules.md), [tasks](tasks.md) và DEC-051. Không dùng giá/quota, trạng thái DONE, mock adapter hoặc route giả định trong tài liệu cũ làm bằng chứng production. Nguồn policy cập nhật: [release sources](policy-and-runtime-sources.md).

> **Nguồn**: `D:\Ky8\exe201\FitmateAI_BE\src\main\java\com\fitmate\backend\` (Spring Boot + JPA + Lombok). Đọc trực tiếp từ file. Cập nhật 2026-09-18.

## 1. Phạm vi thực tế

| Hạng mục | Giá trị thực tế |
|---|---|
| Số entity | **12** (User, UserProfile, PtProfile, Appointment, SocialPost, ChatMessage, Exercise, MealLog, MealSchedule, WeightHistory, WorkoutLog, WorkoutPlan) |
| Số controller | **2** (`AuthController` ở `/api/auth/{register,login,test}`, `UserController` ở `/api/user/profile/{userId}`) |
| Số service | **2** (`AuthService` 4.4KB, `UserService` 3.1KB) |
| Số repository | **3** (User, UserProfile, PtProfile) |
| Endpoint thực sự có | **3** (register, login, getUserProfile) + 1 health-check string |
| Auth | "JWT" giả = `"FITMATE-JWT-" + UUID.randomUUID()` — KHÔNG phải JWT thật, không verify được |
| Security | `SecurityConfig.permitAll()` cho mọi route, CSRF tắt, password BCrypt 11-round |
| CORS | `allowedOriginPatterns("*") + allowCredentials(true)` — KHÔNG an toàn cho browser |
| Validation | Không có Bean Validation, không có ProblemDetails |
| Payment | **0** endpoint, 0 entity |
| Certificate moderation | **0** — `PtProfile.isVerified=true` được set cứng khi đăng ký PT |
| Admin endpoints | **0** |
| Notification | **0** — dù có Notification trong OpenAPI |
| Workout session idempotency | **0** — không có `client_id`, có thể duplicate khi retry |
| Outbox / Idempotency | **0** |
| Audit log | **0** |
| Token refresh | **0** |
| Email verification | **0** |
| Password reset | **0** |
| Timezone | Dùng `LocalDateTime` (no zone) — sai nguyên tắc domain-rules |
| Soft delete | **0** — `delete` xóa cứng |
| Created at | Dùng `LocalDateTime.now()` — sai (phải UTC) |

## 2. Schema thực tế

### users
```sql
id BIGSERIAL PK
email VARCHAR UNIQUE NOT NULL
password VARCHAR NOT NULL  -- BCrypt
full_name VARCHAR NOT NULL
role VARCHAR NOT NULL       -- 'USER' | 'PT' | 'ADMIN'
avatar_url VARCHAR NULL
created_at TIMESTAMP        -- LocalDateTime.now(), không có TZ
```

### user_profiles
```sql
id BIGSERIAL PK
user_id BIGINT FK → users.id
height DOUBLE
weight DOUBLE
gender VARCHAR
birthday VARCHAR
fitness_goal TEXT? (columnDefinition not set)
selected_days TEXT          -- JSON string
allergies TEXT
weight_goal VARCHAR         -- 'lose'|'maintain'|'gain'
```

### pt_profiles
```sql
id BIGSERIAL PK
user_id BIGINT FK → users.id UNIQUE
specialty VARCHAR
experience_years VARCHAR    -- NOTE: VARCHAR, không phải int
price_per_session DOUBLE
certifications TEXT
rating DOUBLE
is_verified BOOLEAN         -- NOTE: tự set true lúc register PT
```

### appointments
```sql
id BIGSERIAL PK
user_id BIGINT FK → users.id
pt_id BIGINT FK → users.id
booking_date VARCHAR        -- '2026-06-18' (DATE as string)
booking_time VARCHAR        -- '09:00' (TIME as string)
status VARCHAR              -- 'PENDING'|'CONFIRMED'|'CANCELLED'|'COMPLETED'
note TEXT
created_at TIMESTAMP
```

### social_posts
```sql
id BIGSERIAL PK
user_id BIGINT FK
post_content TEXT NOT NULL
image_url VARCHAR
likes_count INT
comments_count INT
created_at TIMESTAMP
```

### chat_messages
```sql
id BIGSERIAL PK
sender_id BIGINT FK → users.id
receiver_id BIGINT NULL
is_ai_companion BOOLEAN
content TEXT NOT NULL
sent_at TIMESTAMP
```

### exercises
```sql
id BIGSERIAL PK
workout_plan_id BIGINT FK
name VARCHAR
reps VARCHAR
sets INT
calories_burn INT
image_url VARCHAR
```

### meal_logs
```sql
id BIGSERIAL PK
user_id BIGINT FK
food_name VARCHAR
image_url VARCHAR
calories INT
protein INT
carbs INT
fat INT
portion_size DOUBLE
logged_at TIMESTAMP
```

### meal_schedules
```sql
id BIGSERIAL PK
user_id BIGINT FK
day_of_week VARCHAR
meal_type VARCHAR
food_name VARCHAR
calories INT
protein INT
carbs INT
fat INT
```

### weight_history
```sql
id BIGSERIAL PK
user_id BIGINT FK
weight_kg DOUBLE NOT NULL
recorded_at TIMESTAMP
```

### workout_logs
```sql
id BIGSERIAL PK
user_id BIGINT FK
workout_plan_id BIGINT FK?
actual_calories_burned INT
completed_at TIMESTAMP
```

### workout_plans
```sql
id BIGSERIAL PK
user_id BIGINT FK
day_of_week VARCHAR
workout_name VARCHAR
duration_minutes INT
target_calories_burn INT
is_completed BOOLEAN
```

## 3. Vấn đề nghiêm trọng (evidence-based)

| ID | Vấn đề | Evidence |
|---|---|---|
| S-001 | **Secrets lộ** | `src/main/resources/application.properties` từng chứa Supabase password `[REDACTED]` trong Git history. Module Spring Boot legacy đã bị xóa khỏi current tree. |
| S-002 | **JWT giả** | `AuthService.java:81` và `:101` — `"FITMATE-JWT-" + UUID.randomUUID()`. Không có signature, không thể verify ở client khác. |
| S-003 | **permitAll** | `SecurityConfig.java:26` — `anyRequest().permitAll()`. Ai cũng gọi được mọi endpoint. |
| S-004 | **CORS credentials wildcard** | `CorsConfig.java:18-22` — `allowedOriginPatterns("*")` + `allowCredentials(true)`. RFC không cho phép kết hợp này. |
| S-005 | **`isVerified=true` tự động** | `AuthService.java:71` — PT mới đăng ký được auto-verified, không qua admin duyệt. |
| S-006 | **Không có certificate workflow** | `PtProfile.certifications` lưu text, không có entity Certificate riêng, không có status pending/approved/rejected. |
| S-007 | **Không có payment** | 0 entity, 0 endpoint, 0 service. |
| S-008 | **Không có admin** | 0 controller admin, 0 RBAC thật. |
| S-009 | **Không có refresh token / session** | Login trả về token stateless không có TTL, không có cách logout/revoke. |
| S-010 | **DateTime không có TZ** | Tất cả entity dùng `LocalDateTime`. Sẽ sai khi user ở nhiều timezone. |
| S-011 | **`experience_years` VARCHAR** | `PtProfile.java:25` — lưu số năm kinh nghiệm thành chuỗi. |
| S-012 | **No idempotency** | `AuthService.register` không có cơ chế chống duplicate khi user double-tap. |
| S-013 | **Soft delete không có** | Không có `deleted_at`. GDPR/quyền xóa tài khoản không khả thi. |
| S-014 | **`workout_logs` không lưu `client_id`** | Không thể retry-safe. Mất dữ liệu khi network retry. |
| S-015 | **Fitness goal/specialty dạng text/comma-separated** | `UserProfile.selectedDays` lưu JSON string, `PtProfile.certifications` lưu raw text. Không truy vấn được. |
| S-016 | **Không có audit log** | Không ai biết admin đã duyệt cert nào, lock user nào, refund booking nào. |

## 4. Mapping parity giữa BE cũ và OpenAPI mới

> Mục đích: biết cái gì có trong app/web mà BE cũ KHÔNG serve được.

| OpenAPI path | FE/Web cần | BE cũ có | Cần tạo mới |
|---|---|---|---|
| `POST /auth/register` | ✅ | ✅ (sơ khai) | Viết lại |
| `POST /auth/login` | ✅ | ✅ | Viết lại (JWT thật) |
| `POST /auth/refresh` | ✅ | ❌ | Mới |
| `POST /auth/logout` | ✅ | ❌ | Mới |
| `POST /auth/password/forgot` | ✅ | ❌ | Mới |
| `POST /auth/password/reset` | ✅ | ❌ | Mới |
| `GET/PATCH/DELETE /users/me` | ✅ | ❌ (chỉ có GET theo id) | Mới |
| `POST /users/me/password` | ✅ | ❌ | Mới |
| `POST /users/me/devices` | ✅ | ❌ | Mới |
| `GET/POST /trainers` | ✅ | ❌ | Mới |
| `GET /trainers/{id}` | ✅ | ❌ | Mới |
| `POST /me/certificates` | ✅ | ❌ (chỉ text) | Mới + image upload |
| `GET /me/certificates` | ✅ | ❌ | Mới |
| `GET /me/availability` | ✅ | ❌ | Mới |
| `POST /me/availability` | ✅ | ❌ | Mới |
| `GET /trainers/{id}/availability` | ✅ | ❌ | Mới |
| `GET /me/services` | ✅ | ❌ | Mới |
| `POST /me/services` | ✅ | ❌ | Mới |
| `POST /bookings/holds` | ✅ | ❌ | Mới |
| `POST /bookings` | ✅ | ❌ | Mới |
| `POST /bookings/{id}/cancel` | ✅ | ❌ | Mới |
| `POST /bookings/{id}/complete` | ✅ | ❌ | Mới |
| `GET /me/bookings` | ✅ | ❌ (chỉ có entity) | Mới |
| `GET /trainers/{id}/bookings` | ✅ | ❌ | Mới |
| `POST /bookings/{id}/payments` | ✅ | ❌ | Mới (BE-140 → BE-400) |
| `POST /payments/{id}/vnpay/ipn` | ✅ | ❌ | Mới |
| `GET /me/payments` | ✅ | ❌ | Mới |
| `GET /me/finance/summary` | ✅ | ❌ | Mới |
| `GET /trainers/{id}/finance/earnings` | ✅ | ❌ | Mới |
| `POST /me/workout/plans` | ✅ | ❌ | Mới |
| `POST /me/workout/sessions` | ✅ | ❌ | Mới (cần client_id) |
| `POST /me/weight-logs` | ✅ | ❌ | Mới |
| `POST /me/nutrition/scan` | ✅ | ❌ | Mới (AI stub) |
| `POST /me/nutrition/meals` | ✅ | ❌ | Mới |
| `GET /social/posts` | ✅ | ❌ (chỉ entity) | Mới |
| `POST /social/posts` | ✅ | ❌ | Mới |
| `POST /social/posts/{id}/comments` | ✅ | ❌ | Mới |
| `POST /social/posts/{id}/reactions` | ✅ | ❌ | Mới |
| `POST /chat/ai/messages` | ✅ | ❌ (chỉ entity đơn giản) | Mới (thread + AI) |
| `POST /chat/friends/{id}/messages` | ✅ | ❌ | Mới |
| `GET /me/notifications` | ✅ | ❌ | Mới |
| `POST /me/notifications/{id}/read` | ✅ | ❌ | Mới |
| `POST /reports` | ✅ | ❌ | Mới |
| `GET /admin/users` | ✅ | ❌ | Mới |
| `POST /admin/users/{id}/lock` | ✅ | ❌ | Mới |
| `GET /admin/certificates` | ✅ | ❌ | Mới |
| `POST /admin/certificates/{id}/approve|reject` | ✅ | ❌ | Mới |
| `GET /admin/bookings` | ✅ | ❌ | Mới |
| `POST /admin/bookings/{id}/refund` | ✅ | ❌ | Mới |
| `GET /admin/finance/overview` | ✅ | ❌ | Mới |
| `GET /admin/reports` | ✅ | ❌ | Mới |
| `POST /admin/reports/{id}/resolve|dismiss` | ✅ | ❌ | Mới |
| `GET /admin/audit` | ✅ | ❌ | Mới |

**Kết luận parity**: BE cũ cover **~3/69 endpoints** (~4%). Còn lại 97% là viết mới. Không phải migration theo nghĩa "chuyển dữ liệu" — mà là viết mới hoàn toàn.

## 5. Đối chiếu với mobile domain.ts

Mobile dùng `User.email/password (mock)` không map sang BE. Sau khi integrate:
- `User.password` KHÔNG gửi từ BE → xóa field này trong mobile mock.
- `User.profile` lưu phẳng → BE trả `User + UserProfile` riêng.
- `Appointment.status` mobile: `'pending'|'confirmed'|'completed'|'cancelled'|'late'|'rescheduled'` (6 state). BE OpenAPI mới chỉ có `Held|AwaitingPayment|Paid|Completed|Cancelled|Refunded|Expired` (7 state). Mapping:
  - `pending` → `AwaitingPayment`
  - `confirmed` → `Paid`
  - `completed` → `Completed`
  - `cancelled` → `Cancelled` hoặc `Expired`
  - `late` → **CHƯA CÓ trong OpenAPI** — cần bổ sung hoặc map sang state hiện có.
  - `rescheduled` → **CHƯA CÓ** — cần bổ sung hoặc tạo Appointment mới.
- `Appointment.timeSlot` mobile: string 'T2 18:00'. BE cần lưu `slotStartUtc + slotStartLocal + durationMinutes` rõ ràng.
- `WorkoutSession` mobile có `status: 'completed'|'endedEarly'|'skipped'`. BE OpenAPI có `Completed|EndedEarly|Skipped` ✅ match.
- `Post.visibility` mobile: `'public'|'friends'|'private'`. BE OpenAPI có `Public|Friends|Private` ✅.
- `Reaction.type` mobile: `'heart'|'flame'|'smile'`. BE OpenAPI có `Heart|Flame|Smile` ✅.
- Bốn meal type mobile `breakfast|lunch|dinner|snack` ↔ BE `MealSchedule.breakfast/lunch/dinner/snack` ✅.

**Cần quyết định sản phẩm**:
- **`late` và `rescheduled`** trong Appointment có nghĩa gì? Có giữ trong BE mới không?
  - `late`: thành viên đến trễ — có thể map sang `Completed` với `note`.
  - `rescheduled`: đổi lịch — có thể cancel appointment cũ + tạo mới.

## 6. Đối chiếu với web admin mockData.js

Web admin có những field MÀ MOBILE không có, ngược lại:
- `User.totalEarnings`, `availableBalance`, `fitCoins`, `currentPT`, `currentPackage`, `streak`, `caloriesBurned` → **admin-only metrics**.
- `Booking.serviceTitle` (string gộp) ↔ mobile chỉ có `ptEmail + timeSlot`. BE cần tách: `Booking → BookingItems` hoặc giữ `serviceTitle` là denormalized.
- `Booking.paymentMethod` (FitMate Pay / QR / MoMo / Napas) — BE OpenAPI mới chỉ có enum `Vnpay`. **Cần bổ sung** enum payment method hoặc ghi rõ web hiện dùng mock khác hệ thống thật.
- `Booking.location` (chuỗi địa chỉ) — BE chưa có. Có thể thêm hoặc là part of `Service`.
- `Booking.sessionSchedule` ('Thứ 2 - 4 - 6 lúc 17:30') — denormalized từ `Service.schedule`.
- `Booking.notes` mobile cũng có nhưng ngắn hơn.
- `ViolationReports.targetType` mobile có 'post'|'comment'|'user'|'certificate'. Web mock có 'post'|'user'|'pt'. **Lệch**: BE mới phải chốt danh sách targetType.
- `aiAnalyticsSummary` web có thống kê AI (Gemini tokens, cost). BE mới chỉ có `ai-contract.md` đề xuất. **Chưa có API cho admin xem AI stats**.
- `initialAdminSettings.systemInfo` web có `environment`, `region`, `version`. BE có `/health/live`/`/health/ready` chưa trả version + region.
- `initialAdminSettings.platformPolicy.platformFeeRate=10` (cố định). BE OpenAPI chưa có endpoint admin xem/sửa chính sách. Có cần cho MVP không?
- `User.specialties` (array) mobile chỉ lưu `spec: string[]`. ✅ match.
- `BookingStatus` web: `'in_progress'|'completed'|'pending'|'cancelled'`. BE OpenAPI: `Held|AwaitingPayment|Paid|Completed|Cancelled|Refunded|Expired`. Mapping:
  - `in_progress` → `Paid` (đang diễn ra)
  - `completed` → `Completed`
  - `pending` → `Held` hoặc `AwaitingPayment`
  - `cancelled` → `Cancelled` (đã refund thì `Refunded`)

**Cần quyết định sản phẩm**:
- `paymentMethod` enum mở rộng (VNPay + MoMo + Napas + QR) hay rút gọn chỉ VNPay?
- `late` và `rescheduled` appointment state — có cần trong BE mới?
- `targetType` report — `pt` hay `user`?
- Có cần admin endpoint quản lý `platformPolicy.platformFeeRate` hay cố định 10%?
- `targetType: 'user'|'post'|'comment'|'certificate'` — bốn hay ba?
- `Booking.location` — lưu ở Service hay Booking?

## 7. Kết luận kiểm kê

1. BE cũ **không đủ parity** với app/web hiện tại. Cần viết mới ~97% backend.
2. Schema cũ dùng `BIGSERIAL`, dấu thời gian không có TZ, lưu JSON-as-text — đều sai so với domain-rules mới.
3. Schema cũ KHÔNG có: Certificate, Service, AvailabilitySlot, BookingHold, Payment, Refund, OutboxMessage, IdempotencyRecord, Reaction, FriendRequest, Friend, Report, ChatThread, ChatMessage (thiếu thread), Review, Notification, Device, AuditLog.
4. JWT cũ là giả — không thể dùng cho FE thật. Phải viết lại hoàn toàn.
5. Không có data migration vì BE cũ gần như không phục vụ production (chỉ mock auth). Demo user sẽ tạo lại qua seed script.
6. Phải đối chiếu lại OpenAPI spec với mobile + web trước khi code tiếp. Một số field (late, rescheduled, location, paymentMethod) chưa có quyết định chốt.

## 8. Việc cần làm tiếp (theo thứ tự)

1. **Q-01**: Chốt `AppointmentStatus` enum giữa mobile 6-state ↔ BE 7-state ↔ web 4-state.
2. **Q-02**: Chốt `paymentMethod` enum (VNPay-only hay multi-provider).
3. **Q-03**: Chốt `Report.targetType` (3 hay 4 giá trị).
4. **Q-04**: Chốt `Booking.location` (lưu ở Service hay Booking).
5. **Q-05**: Chốt `platformPolicy.platformFeeRate` API (read-only hay cho phép sửa).
6. **Q-06**: Chốt admin metrics scope (AI tokens, region, version) — có cần cho MVP không?
7. Sau khi Q-01..Q-06 xong: cập nhật OpenAPI, sau đó mới code tiếp.
8. Sau cùng: viết migration script `data-model.md` ghi rõ BE cũ **không có data** cần migrate.
