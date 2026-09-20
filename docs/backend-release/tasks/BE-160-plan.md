# BE-160 Workout + Weight — Plan rev 2

> Rev 2 sau khảo sát mã nguồn hiện có (Sep 2026).
> Mục tiêu: triển khai CRUD + idempotent session cho `/me/workout/*` và
> create + list paginated cho `/me/weight`, không phụ thuộc booking/AI.

## 0. Khảo sát — nền tảng hiện có

### Entity Domain (đã có sẵn — KHÔNG tạo lại)

| Entity | File | Trạng thái |
|---|---|---|
| `WorkoutPlan` | `src/Fitmate.Domain/Workout/WorkoutPlan.cs` | Đã có (Id, UserId, DayOfWeek, WorkoutName, DurationMinutes, TargetCaloriesBurn, IsCompleted). |
| `Exercise` | `src/Fitmate.Domain/Workout/Exercise.cs` | Đã có (Id, WorkoutPlanId, Name, Type enum, Sets, Reps?, DurationSec?, RestSec, RestAfterSec?, Calories, Notes?, MediaUrl?, MediaType?, MediaSource?, Tags). |
| `WorkoutSession` | `src/Fitmate.Domain/Workout/WorkoutSession.cs` | Đã có (Id, UserId, WorkoutPlanId?, ClientId, Date, Day, DurationMin, CaloriesBurned, Exercises, Status enum, CompletedSets?, PlannedSets?). |
| `WeightLog` | `src/Fitmate.Domain/Workout/WeightLog.cs` | Đã có (Id, UserId, WeightKg decimal, RecordedAt). |

### EF Configurations (đã có sẵn)

- `src/Fitmate.Infrastructure/Persistence/Configurations/WorkoutConfiguration.cs`
- Indexes đã có: `IX_workout_plans_(UserId, DayOfWeek)` (KHÔNG unique), `IX_workout_sessions_(UserId, ClientId)` UNIQUE, `IX_weight_logs_(UserId, RecordedAt)`.

### Migrations (đã có sẵn)

- `20260918160320_InitialSchema` đã tạo 4 bảng + indexes.
- **KHÔNG có FK constraint** `exercises.WorkoutPlanId → workout_plans.id` và `workout_sessions.WorkoutPlanId → workout_plans.id` (BE-160 sẽ thêm qua migration mới).

### OpenAPI (đã có sẵn — `docs/backend-release/openapi/fitmate-v1.yaml`)

- `GET /me/workout/plans`, `POST /me/workout/plans`
- `GET /me/workout/sessions`, `POST /me/workout/sessions` (header `Idempotency-Key`, body `client_id`)
- `GET /me/weight`, `POST /me/weight`
- **Thiếu**: `PATCH /me/workout/plans/{id}`, `DELETE /me/workout/plans/{id}`, `GET /me/workout/sessions/{id}` (chi tiết). Weight list trả array — cần đổi sang paginated object (đã chốt với user).

### Dependencies đã xong (BE-070 + BE-080 + BE-090)

- `IdempotencyMiddleware` (`src/Fitmate.Api/Idempotency/IdempotencyMiddleware.cs`) — replay theo header `Idempotency-Key` + payload hash.
- `IIdempotencyService` + `IIdempotencyStore` — lưu replay theo key.
- `IAuthorizationGuard` (`src/Fitmate.Application/Auth/AuthorizationGuard.cs`) — `EnsureOwnerAsync(resource, userId, ownerUserId)`, `EnsureRoleAsync`.
- `ProblemDetailsFactory` + `FitmateProblemDetails` + `ErrorCodes` (`src/Fitmate.Domain/Common/ErrorCodes.cs`).
- **Sai tham chiếu cũ:** ghi chú BE-060 nói "Confirm/Accept cần Appointment.HoldId FK + AppointmentStatus (DEC-026)... bổ sung ở BE-160 khi migrate AppointmentStatus" — KHÔNG đúng. Migrate AppointmentStatus thuộc **BE-130** (Booking flows). Plan này ghi rõ scope **KHÔNG chạm** booking.

---

## 1. Scope chốt (theo user duyệt Sep 21)

### 1.1 Endpoint BE-160 triển khai

| # | Method | Path | OpenAPI hiện có? | BE-160 |
|---|---|---|---|---|
| 1 | GET | `/api/v1/me/workout/plans` | Có | Implement (list plan của owner) |
| 2 | POST | `/api/v1/me/workout/plans` | Có | Implement (create plan + nested exercises) |
| 3 | PATCH | `/api/v1/me/workout/plans/{id}` | **Thiếu** | Implement + bổ sung OpenAPI |
| 4 | DELETE | `/api/v1/me/workout/plans/{id}` | **Thiếu** | Implement + bổ sung OpenAPI |
| 5 | GET | `/api/v1/me/workout/sessions` | Có | Implement (cursor pagination + filter `from`/`to`) |
| 6 | POST | `/api/v1/me/workout/sessions` | Có (header `Idempotency-Key` + body `client_id`) | Implement (idempotent theo cả 2) |
| 7 | GET | `/api/v1/me/weight` | Có (trả array) | Implement — **đổi** sang paginated `{items, nextCursor}` |
| 8 | POST | `/api/v1/me/weight` | Có | Implement |

**Không có Weight PATCH/DELETE/GET-by-id** trong scope BE-160 (user đã xác nhận "không mô tả Weight CRUD nếu thực tế scope chỉ create/list").

### 1.2 Snapshot session (yêu cầu user #2 cuối)

- Mỗi `WorkoutSession` lưu thêm **snapshot tối thiểu** tại thời điểm tập để sửa/xóa plan không phá lịch sử:
  - `SnapshotPlanName`, `SnapshotPlanDayOfWeek`, `SnapshotPlanDurationMinutes`, `SnapshotPlanTargetCaloriesBurn` (4 cột mới, nullable, copy từ plan tại thời điểm insert).
  - `ExerciseResults` (bảng con mới `workout_session_exercises`): lưu từng bài đã tập trong session với `ExerciseName`, `Type`, `Sets`, `Reps`/`DurationSec` (planned), `CompletedSets`, `CompletedReps`/`CompletedDurationSec` per set (kết quả thực tế).
- Migration mới thêm các cột + bảng này. **CẬP NHẬT entity + EF config đồng bộ.**

### 1.3 Idempotency — giữ cả 2 (yêu cầu user #4)

- **Header `Idempotency-Key`** (qua `IdempotencyMiddleware`): replay theo payload hash. Middleware đã có, không cần sửa.
- **Body `client_id`** (qua Application service + DB unique index): idempotent theo `(UserId, ClientId)`.
- Rule kết hợp:
  - Cùng `client_id` + cùng payload → trả row cũ (200).
  - Cùng `client_id` + payload khác → 409 `FITMATE.WORKOUT.SESSION_PAYLOAD_MISMATCH`.
  - Khác `client_id` + cùng `Idempotency-Key` → middleware reject 409 trước (existing rule).
  - DB unique `(UserId, ClientId)` đảm bảo atomic chống race (PostgreSQL transaction + `ON CONFLICT` hoặc `try/catch` unique violation → idempotent replay).
- **Không soft-delete session** (giữ cứng để retry cũ vẫn tìm thấy; hoặc nếu có soft-delete, đảm bảo `IX_workout_sessions_(UserId, ClientId)` vẫn áp dụng). BE-160 quyết định: **không soft-delete session** (chỉ append-only, đúng với bản chất lịch sử).

### 1.4 Weight log

- `WeightKg` `numeric(5,2)` (đã có schema). Range 20–400 kg ghi nhận là **giới hạn đầu vào của sản phẩm**, không phải khuyến nghị sức khỏe — ghi rõ trong OpenAPI description.
- `RecordedAt`: client gửi; nếu null → server dùng `UtcNow`. Cho phép nhập lịch sử quá 7 ngày, không có giới hạn "quá khứ". **Chỉ reject** nếu `RecordedAt > UtcNow + 5 phút` (khoảng dung sai clock skew). Ghi rõ tolerance trong OpenAPI.
- `CreatedAt`: server set khi nhận — đã có từ `AuditableEntity`.
- Pagination: cursor `(recordedAt desc, id desc)`, default 50, max 100.
- Rate-limit chống spam: do middleware/endpoint rate limit (BE-140/UA-019), KHÔNG cấm nhiều log/ngày.

### 1.5 Day-of-week và timezone (yêu cầu user #6 + #3)

- API: `day_of_week` chấp nhận string enum `mon`/`tue`/`wed`/`thu`/`fri`/`sat`/`sun`. Validation trong Application layer.
- DB lưu string (đã có `HasMaxLength(16)`) — giữ nguyên.
- **Session `Date` + `Day`** hiểu theo timezone của user (`UserProfile.Timezone`, đã có trong schema Identity). Application service chuyển `clientDate` + `userTimezone` → `Date` (DateOnly UTC) + `Day` (string). Nếu user chưa set timezone (default `Asia/Ho_Chi_Minh`) → dùng default. **KHÔNG dùng server timezone ngầm định.**
- `recorded_at` cho weight: lưu UTC (đã có `timestamp with time zone`). FE convert khi hiển thị.

### 1.6 Validation (yêu cầu user #3)

- `Exercise`:
  - `reps` bắt buộc + > 0 khi `type=reps`; `duration_sec` bắt buộc + > 0 khi `type=time`.
  - Trường không áp dụng (vd `reps` khi `type=time`) → null là hợp lệ, KHÔNG reject.
  - `sets` > 0; `rest_sec` >= 0; `rest_after_sec` >= 0 (nullable ok); `calories` >= 0; `name` 1–200; `tags` max 10 mỗi tag max 32 chars.
- `WorkoutPlan`:
  - `workout_name` 1–200; `duration_minutes` 1–600; `target_calories_burn` 0–100000; `day_of_week` ∈ enum.
  - `is_completed` field đã có trong entity nhưng **KHÔNG dùng làm nguồn trạng thái vĩnh viễn**. Service **bỏ qua** field này khi expose DTO (xem §2.2).
- `WorkoutSession`:
  - `client_id` 8–64 chars, required.
  - `exercise_results[]`: mỗi item chứa `set_results[]` có cấu trúc (`set_index`, `status`, `actual_reps`/`actual_duration_sec`).
  - Server **tính lại** `completed_sets` từ SetResults (count status=Completed, exclude bonus nếu contract cấm). KHÔNG tin tổng client gửi nếu mâu thuẫn với SetResults — log warning + dùng giá trị server-derived.
  - Default: bonus set **KHÔNG cho phép** (`set_index > planned_sets` → 400). OpenAPI note: extension BE-162 có thể mở lại.
  - `duration_min` (planned) 1–600; `calories_burned` (actual, client-estimated) 0–100000; `exercises` (count) >= 0.
  - Calories do client cung cấp là **ước tính** — OpenAPI description ghi rõ không phải số đo backend verify.
- `WeightLog`:
  - `weight_kg` 20–400 (range sản phẩm); `recorded_at` ≤ UtcNow + 5 phút.
- Plan CRUD: owner check qua `AuthorizationGuard.EnsureOwnerAsync("workout_plan", userId, plan.UserId)`.

### 1.7 Ngoài scope (yêu cầu user #7)

- Admin workout template (BE-161 sau nếu cần).
- AI tạo plan hoặc khuyến nghị (BE-170 có AI meal scan; BE-160 không phụ thuộc).
- Thư viện media/video — `MediaUrl`/`MediaType`/`MediaSource` chỉ echo client gửi, không validate nguồn, không auto-fill.
- Migrate AppointmentStatus (BE-130).
- Profile/auth changes (BE-080 đã DONE, BE-160 không chạm).
- Calendar nutrition (BE-170).
- Workout timer (BE-160 chỉ lưu kết quả cuối; FE player là việc FE).

---

## 2. Thiết kế chi tiết

### 2.1 Domain changes (ngoài entity đã có)

**Mở rộng `WorkoutSession`** (thêm field — đổi tên cho rõ ngữ nghĩa "planned vs actual"):

```csharp
// WorkoutSession.cs (bổ sung — đổi tên từ DurationMin/CaloriesBurned sang Planned/Actual)
public string? SnapshotPlanName { get; private set; }
public string? SnapshotPlanDayOfWeek { get; private set; }
public int? SnapshotPlanPlannedDurationMinutes { get; private set; }
public int? SnapshotPlanTargetCaloriesBurn { get; private set; }
```

> Lưu ý quan trọng: tên field gốc `DurationMin` (planned, kế hoạch tập) và `CaloriesBurned` (actual, kết quả thực tế) đã có sẵn. BE-160 giữ nguyên 2 field này — không đổi tên để tránh migration nặng. Snapshot chỉ thêm 4 cột mới từ plan.

**Entity mới `WorkoutSessionExercise`** (`src/Fitmate.Domain/Workout/WorkoutSessionExercise.cs`):

```csharp
public class WorkoutSessionExercise : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid WorkoutSessionId { get; private set; }
    // === Snapshot từ plan (planned) ===
    public string ExerciseName { get; private set; } = null!;
    public ExerciseType Type { get; private set; }
    public int OrderIndex { get; private set; }                  // vị trí trong plan
    public int PlannedSets { get; private set; }
    public int? PlannedReps { get; private set; }                // null khi type=time
    public int? PlannedDurationSec { get; private set; }         // null khi type=reps
    public int? PlannedRestSec { get; private set; }
    public int PlannedCalories { get; private set; }
    public List<string> Tags { get; private set; } = new();
    // === Actual: tính từ SetResults (per-set), KHÔNG lưu int đơn ===
    public int CompletedSets { get; private set; }               // derived, recompute từ SetResults
}
```

**Entity mới `WorkoutSessionExerciseSet`** (`src/Fitmate.Domain/Workout/WorkoutSessionExerciseSet.cs`) — kết quả từng hiệp có cấu trúc:

```csharp
public class WorkoutSessionExerciseSet : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid WorkoutSessionExerciseId { get; private set; }
    public int SetIndex { get; private set; }                    // 1-based, unique trong 1 exercise
    public WorkoutSetStatus Status { get; private set; }         // completed | partial | skipped
    public int? ActualReps { get; private set; }                 // bài reps; null nếu time hoặc unknown
    public int? ActualDurationSec { get; private set; }          // bài time; null nếu reps hoặc unknown
    public bool IsBonus { get; private set; }                    // hiệp bổ sung (setIndex > plannedSets)
}

public enum WorkoutSetStatus { Completed = 1, Partial = 2, Skipped = 3 }
```

**Quy tắc `WorkoutSessionExerciseSet`:**
- Bài `reps` dùng `ActualReps`; bài `time` dùng `ActualDurationSec`. KHÔNG dùng 0 thay cho chưa biết.
- `SetIndex` unique trong 1 `WorkoutSessionExercise`.
- `ActualReps`/`ActualDurationSec` phải `>= 0` khi không null.
- `CompletedSets` của `WorkoutSessionExercise` được **tính lại từ SetResults** (count status = Completed, exclude IsBonus nếu contract cấm bonus). KHÔNG tin tổng client gửi nếu mâu thuẫn với SetResults.
- `IsBonus = (SetIndex > PlannedSets)` — tính phía server khi insert.

**API contract cho per-set:**
- `exercise_results[].set_results[]` trong `WorkoutSessionInput`.
- OpenAPI: bắt buộc `set_index` (>=1), `status` (enum), 1 trong 2 `actual_reps`/`actual_duration_sec` theo `type`.
- Nếu `set_index > planned_sets` → đánh `is_bonus = true`. Nếu contract không cho phép bonus (mặc định KHÔNG cho), reject 400.

### 2.2 Application layer

**`IWorkoutService`** (`src/Fitmate.Application/Workout/IWorkoutService.cs`):

```csharp
public interface IWorkoutService
{
    Task<IReadOnlyList<WorkoutPlanDto>> ListPlansAsync(Guid userId, string? dayOfWeek, CancellationToken ct);
    Task<WorkoutPlanDto> CreatePlanAsync(Guid userId, CreateWorkoutPlanRequest req, CancellationToken ct);
    Task<WorkoutPlanDto> UpdatePlanAsync(Guid userId, Guid planId, UpdateWorkoutPlanRequest req, CancellationToken ct);
    Task DeletePlanAsync(Guid userId, Guid planId, CancellationToken ct);
    Task<WorkoutPlanDto?> GetPlanAsync(Guid userId, Guid planId, CancellationToken ct); // optional convenience

    Task<WorkoutSessionsPage> ListSessionsAsync(Guid userId, string? cursor, int limit, DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<WorkoutSessionDto> LogSessionAsync(Guid userId, LogWorkoutSessionRequest req, CancellationToken ct);
}

public interface IWeightService
{
    Task<WeightLogsPage> ListAsync(Guid userId, string? cursor, int limit, DateTime? from, DateTime? to, CancellationToken ct);
    Task<WeightLogDto> LogAsync(Guid userId, decimal weightKg, DateTime? recordedAt, CancellationToken ct);
}
```

**`IWorkoutStore` + `IWeightStore`** abstractions (Application/Infrastructure ports), giống pattern `IChatStore`.

**`IUserTimezoneLookup`** lookup `UserProfile.Timezone`. Nếu chưa có abstraction, tạo mới (`Application/Auth/IUserTimezoneLookup.cs`) với impl trong Infrastructure. User mặc định: `Asia/Ho_Chi_Minh`.

### 2.3 Validation

Dùng `ValidationException` với `ErrorCodes.Validation.*` cho field-level (đã có pattern). Custom rule riêng:

- `WorkoutDayOfWeekValidator` — parse + validate enum `mon..sun` (case-insensitive).
- `ExerciseValidator` — validate `reps`/`duration_sec` theo `type`.
- `SessionValidator` — validate `completed_sets <= planned_sets` cho status ≠ skipped.

### 2.4 Infrastructure

- `EfWorkoutStore` (`src/Fitmate.Infrastructure/Workout/EfWorkoutStore.cs`):
  - Method: `FindPlanAsync`, `AddPlanAsync` (cascade add exercises), `UpdatePlanAsync` (replace exercises), `SoftDeletePlanAsync` (giữ cứng session, **không** touch sessions).
  - `FindSessionByClientIdAsync`, `AddSessionAsync` (atomic), `ListSessionsAsync` (cursor).
- `EfWeightStore` (`src/Fitmate.Infrastructure/Workout/EfWeightStore.cs`).
- `EfUserTimezoneLookup` — query `asp_net_users.timezone`.

### 2.5 Migration mới

`20260921000000_WorkoutSnapshot` — add:

**Pre-check trước khi apply (theo chốt user #4):**
- Query count orphan exercise: `SELECT COUNT(*) FROM exercises e WHERE NOT EXISTS (SELECT 1 FROM workout_plans p WHERE p.id = e."WorkoutPlanId")`.
- Query count orphan session: `SELECT COUNT(*) FROM workout_sessions s WHERE s."WorkoutPlanId" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM workout_plans p WHERE p.id = s."WorkoutPlanId")`.
- Nếu count > 0 → DỪNG migration, báo user với số lượng. **KHÔNG tự xóa orphan hoặc bịa snapshot để pass.**
- Nếu count = 0 → apply. Báo log: `Pre-check: orphan_exercises=N, orphan_sessions=M, nullability_ok=Y, fk_constraints_pending=2`.

**Schema changes:**
- Columns vào `workout_sessions`: `snapshot_plan_name` (text nullable), `snapshot_plan_day_of_week` (varchar(16) nullable), `snapshot_plan_planned_duration_minutes` (int nullable), `snapshot_plan_target_calories_burn` (int nullable).
- New table `workout_session_exercises` (id, workout_session_id, exercise_name, type, order_index, planned_sets, planned_reps nullable, planned_duration_sec nullable, planned_rest_sec nullable, planned_calories, completed_sets, tags text[], audit fields).
- New table `workout_session_exercise_sets` (id, workout_session_exercise_id, set_index, status (int), actual_reps nullable, actual_duration_sec nullable, is_bonus bool, audit fields).
- Indexes:
  - `IX_workout_session_exercises_WorkoutSessionId`
  - `IX_workout_session_exercise_sets_WorkoutSessionExerciseId_SetIndex` UNIQUE
- FK constraints:
  - `FK_exercises_workout_plans_WorkoutPlanId` ON DELETE CASCADE (xóa plan xóa exercise của plan).
  - `FK_workout_sessions_workout_plans_WorkoutPlanId` ON DELETE SET NULL (xóa plan giữ session, set WorkoutPlanId=null; snapshot + exercises vẫn nguyên).
  - `FK_workout_session_exercises_workout_sessions_WorkoutSessionId` ON DELETE CASCADE (xóa session xóa exercise results).
  - `FK_workout_session_exercise_sets_workout_session_exercises_WorkoutSessionExerciseId` ON DELETE CASCADE.

**Up/Down đối xứng** — Down phải drop FK + indexes + tables + columns theo thứ tự ngược. Pre-check trên DB test riêng (Testcontainers hoặc compose branch), KHÔNG apply trên dev DB local đang dùng.

**Soft-delete plan (nếu sau này áp dụng):** FK SetNull chỉ trigger khi hard-delete. Nếu BE-160 quyết định soft-delete plan → giữ `DeletedAt` + filter trong query, không động vào FK. BE-160 hiện **chỉ dùng hard-delete** (theo plan ban đầu) — nếu sau này đổi soft, FK behavior không đổi.

### 2.6 API controllers

- `MeWorkoutController` (`src/Fitmate.Api/Workout/MeWorkoutController.cs`):
  - `[Authorize]` toàn controller.
  - 6 endpoints: list/create plan, PATCH/DELETE plan, list sessions, POST session.
- `MeWeightController` (`src/Fitmate.Api/Weight/MeWeightController.cs`):
  - `[Authorize]`.
  - 2 endpoints: list (cursor), POST.

**Mapping ownership:** controller lấy `UserId` qua `CurrentUserAccessor` (đã có), truyền xuống service.

### 2.7 OpenAPI updates

Bổ sung vào `docs/backend-release/openapi/fitmate-v1.yaml`:

- `PATCH /me/workout/plans/{id}` (request: `WorkoutPlanUpdate` — partial fields).
- `DELETE /me/workout/plans/{id}` (204).
- Schema `WorkoutPlanUpdate` (tất cả optional).
- Update `WorkoutPlanInput`: cho phép `day_of_week` enum (chỉ trong description).
- Update response cho `/me/weight` (GET) và `/me/workout/sessions` (GET): paginated object `{items, nextCursor}`.
- New schema `WorkoutSessionExercise` (response) + field vào `WorkoutSession`.
- New schema `WorkoutSessionInput` cho `exercise_results` array.
- New error codes trong OpenAPI examples (ghi trong description).

### 2.8 Idempotency wiring

- `POST /me/workout/sessions`: controller **không** đọc `Idempotency-Key` trực tiếp — middleware xử lý. Service vẫn check `client_id` ở Application layer.
- Khi cùng `Idempotency-Key` (header) + `client_id` (body): middleware replay → service không chạy. (Middleware đã handle.)
- Khi khác `Idempotency-Key` nhưng cùng `client_id`: middleware proceed → service phát hiện duplicate (DB unique) → idempotent replay (trả row cũ).
- Khi khác `Idempotency-Key` + khác `client_id` (cùng payload): middleware proceed → service insert mới.
- Race condition 2 requests đồng thời cùng `(user, client_id)`: 1 thắng (insert), 1 fail (unique violation) → service catch + replay (đọc row vừa insert và trả).

### 2.9 Tests

**Không dùng EF InMemory** (yêu cầu user #8). Test phân 2 loại:

#### Unit tests (`tests/Fitmate.UnitTests/`)

- `WorkoutServiceTests` (mock store + authz):
  - `ListPlansAsync_returnsOnlyOwnPlans`
  - `CreatePlanAsync_validatesExercises_type_reps_requires_reps`
  - `CreatePlanAsync_validatesExercises_type_time_requires_duration_sec`
  - `CreatePlanAsync_invalidDayOfWeek_throwsValidation`
  - `UpdatePlanAsync_ownerMismatch_throwsForbidden`
  - `DeletePlanAsync_doesNotTouchSessions` (verify service chỉ gọi `DeletePlan`, không đụng session store)
  - `LogSessionAsync_idempotentSameClientId_returnsExisting`
  - `LogSessionAsync_sameClientIdDifferentPayload_throwsConflict`
  - `LogSessionAsync_bonusSetRejected_throwsValidation`
  - `LogSessionAsync_completedSetsDerivedFromSetResults_notClientTotal`
  - `LogSessionAsync_usesUserTimezoneForDay_field`
  - `LogSessionAsync_skippedStatus_allowsEmptySetResults`
  - `ListSessionsAsync_cursorPagination`
- `WeightServiceTests`:
  - `LogAsync_range20to400_validatesBounds`
  - `LogAsync_recordedAtInFutureTooFar_throwsValidation`
  - `LogAsync_allowsHistoricalPastData`
  - `ListAsync_cursorPagination_descByRecordedAt`
- `WorkoutSessionExerciseSetTests` (domain):
  - `Build_completedSet_hasActualReps`
  - `Build_timeTypeSet_hasActualDurationSec`
  - `Build_setIndexAbovePlanned_marksIsBonus`
  - `Build_negativeActual_throwsValidation`

#### Integration tests (`tests/Fitmate.IntegrationTests/`, PostgreSQL riêng — Testcontainers)

- `WorkoutIntegrationTests`:
  - `CreatePlan_persistsAndCanBeRetrieved`
  - `UpdatePlan_replacesExerciseList_doesNotMutateExistingSessionResults`
  - `DeletePlan_withExistingSession_setsWorkoutPlanIdToNull_sessionIntact` (FK SetNull)
  - `DeletePlan_cascadesExercises` (FK Cascade)
  - `DeletePlan_sessionExercisesAndSetsRemainIntact`
  - `LogSession_idempotent_concurrentRequests_returnsSameRow` (2 parallel POST cùng `client_id` → 1 row)
  - `LogSession_idempotent_replayDifferentPayload_returns409`
  - `LogSession_bonusSet_returns400`
  - `LogSession_completedSetsComputedFromSetResults`
  - `WeightLog_pagination_cursorOrder`
  - `WeightLog_recordedAtTolerance5min`
  - `WeightLog_allowsHistoricalImportBackdatedOneYear`
- `MigrationPreCheckTests`:
  - `PreCheck_emptyDb_returnsZeroOrphans_pass`
  - `PreCheck_withOrphanExercise_failsBeforeMigration` (inject orphan → pre-check detect → migration abort)

**Báo số test thực tế sau khi viết xong** (theo yêu cầu user #8 cuối — không ấn định trước).

### 2.10 Out of scope chốt

- KHÔNG đụng booking, payment, AI, notification (BE-200 producer chưa wire — không tự ý wire).
- KHÔNG thêm media validation server-side (FE tự quản lý URL nguồn).
- KHÔNG thêm analytics/aggregation (BE-180+ sau).
- KHÔNG retry/replay queue cho plan update (PATCH idempotent qua planId, không qua client_id).

---

## 3. Plan triển khai (commit từng phần có ý nghĩa)

| # | Commit | Nội dung |
|---|---|---|
| 1 | `docs(be): add BE-160 Workout + Weight plan rev 3 [BE-160]` | File này + cập nhật `tasks.md` (status IN_PROGRESS). |
| 2 | `feat(be): add WorkoutSessionExercise + Set entities [BE-160]` | Domain entity mới (3 file) + EF Configuration. **Chưa migration.** |
| 3 | `feat(be): extend WorkoutSession with snapshot fields [BE-160]` | Domain `WorkoutPlan` + `WorkoutSession` thêm fields + EF config update. **Chưa migration.** |
| 4 | `feat(be): add workout snapshot migration + FK constraints [BE-160]` | Migration `20260921000000_WorkoutSnapshot` với pre-check helper trong comment. |
| 5 | `feat(be): add IWorkoutService + IWeightService + error codes [BE-160]` | Interface + DTOs + ErrorCodes.Workout namespace + IUserTimezoneLookup. |
| 6 | `feat(be): implement WorkoutService + WeightService + stores [BE-160]` | Service impl + Ef stores + user timezone lookup impl + DI wiring. |
| 7 | `feat(be): add MeWorkout + MeWeight controllers + DI wiring [BE-160]` | Controllers + Program.cs DI. |
| 8 | `docs(be): update OpenAPI for workout PATCH/DELETE + pagination [BE-160]` | OpenAPI spec updates + lint check. |
| 9 | `test(be): add workout + weight unit tests [BE-160]` | Unit tests (số lượng báo sau khi viết). |
| 10 | `test(be): add workout + weight integration tests (PostgreSQL) [BE-160]` | Integration tests (Testcontainers PG riêng). |
| 11 | `docs(be): mark BE-160 DONE in tasks.md + handoff.md [BE-160]` | Đóng task, báo số test thực tế + warning pre-existing. |

**Mỗi commit phải `dotnet build` PASS 0 warning mới** và `dotnet test` không regress. Sau mỗi commit, report diff + đề xuất message, chờ user duyệt trước khi `git commit`.

---

## 4. Tiêu chí DONE

- `dotnet build` PASS, 0 warning mới (warning cũ ngoài scope bỏ qua — ghi trong commit body nếu có).
- `dotnet test` tổng tăng theo số thực tế (sẽ báo).
- Migration apply OK trên `compose up postgres` local.
- OpenAPI lint (spectral) pass — dùng docker hoặc Node CLI nếu chưa có npm package.
- 8 endpoint hoạt động đúng (manual check qua `dotnet run` + curl, hoặc integration test).
- Ownership, validation, idempotency, pagination đều PASS integration test.
- Documented in `handoff.md` + `tasks.md`.

---

## 5. Rủi ro & giảm thiểu

| Rủi ro | Giảm thiểu |
|---|---|
| FK constraint mới fail trên data hiện có (orphan exercise/session) | Pre-check: query count orphan trước khi apply migration. Nếu > 0 → dừng, báo user. |
| Race idempotent session fail trên unique violation → UX tệ | Try/catch unique violation → reload + return row cũ (idempotent replay pattern, giống chat). |
| Timezone lookup thêm query DB | Cache theo request scope hoặc include vào JWT claim sau (BE-141 sau). BE-160 chấp nhận 1 query thêm. |
| OpenAPI lint false positive | Disable rule cụ thể trong `.spectral.yaml`; ghi rõ rule + lý do. |
| Migration lỡ chạm row cũ | Backup schema snapshot qua `pg_dump --schema-only` trước khi apply (runbook). |

---

## 6. Tài liệu tham chiếu

- `docs/backend-release/openapi/fitmate-v1.yaml` §workout + §weight.
- `docs/backend-release/decisions.md` (DEC-026 về status separation; DEC-029 về money policy — không trực tiếp nhưng pattern).
- `docs/backend-release/domain-rules.md` §12 (chat policy pattern tham khảo cho ownership/idempotency).
- `docs/backend-release/api-contract.md` §11 (idempotency contract).
- `docs/backend-release/authorization-matrix.md` (BE-160 chỉ owner; không cần update matrix nếu rule đã có ở mục "owner resources").
