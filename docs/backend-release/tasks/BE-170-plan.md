# BE-170 — Nutrition (log, schedule) + AI stub — Plan

> **Goal:** Member logs meals (manual + scan via AI stub), gets/sets weekly meal schedule.

**Status:** PARTIAL — domain entities, EF config, OpenAPI schemas, and 4 endpoint definitions already exist. Application layer (service, controller, AI stub) needs implementation.

## 1. Existing state (verified 2026-09-21)

### Domain (`src/Fitmate.Domain/Nutrition/`)
- ✅ `MealLog` entity — full CRUD via `Create`/`Update` methods
- ✅ `MealSchedule` entity — `Create`/`Update` methods
- ✅ `MealSource` enum (`manual | dataset | ai_estimate`) in `Enums.cs`
- ✅ EF Configuration: `NutritionConfiguration.cs` (MealLog + MealSchedule)

### Infrastructure (`src/Fitmate.Infrastructure/`)
- ✅ `FitmateDbContext.MealLogs` + `MealSchedules` DbSets
- ✅ Initial migration already includes both tables

### OpenAPI (`docs/backend-release/openapi/fitmate-v1.yaml`)
- ✅ `GET /me/nutrition/meals` — list with `from`/`to` date filter
- ✅ `POST /me/nutrition/meals` — log meal
- ✅ `PATCH /me/nutrition/meals/{id}` — update / confirm
- ✅ `GET /me/nutrition/schedule` — get schedule
- ✅ `PUT /me/nutrition/schedule` — set schedule
- ✅ `POST /me/nutrition/scan` — AI estimate (returns 503 when provider unavailable)
- ✅ Schemas: `MealLog`, `MealLogInput`, `MealLogUpdate`, `MealSchedule`, `NutritionScanResult`

### Not yet implemented
- ❌ `INutritionService` abstraction
- ❌ `NutritionDtos` — request/response records
- ❌ `NutritionErrorCodes`
- ❌ `NutritionService` — implementation
- ❌ `INutritionStore` + `EfNutritionStore` — persistence
- ❌ `NutritionController` — 4 endpoints
- ❌ `IAiAdapter` + `AiNutritionAdapter` + `AiNutritionAdapterOptions` (AI stub per DEC-016/DEC-033)
- ❌ Unit tests

## 2. Scope

### In scope

1. **Meal log CRUD** — list (date range), create (manual), update (PATCH → confirm)
2. **Meal schedule** — get / upsert (replace whole schedule)
3. **AI scan stub** — `POST /me/nutrition/scan` → returns 503 `FITMATE.AI.UNAVAILABLE` always (per DEC-016: stub adapter fail-fast until real provider wired)
4. **Ownership enforcement** — all endpoints scoped to authenticated user

### Out of scope (deferred)

- Real AI provider integration — `IAiAdapter` stub only, 503 always
- Image upload/storage — `image_url` accepted in request but not persisted/stored in this task
- Nutrition analytics / daily summary
- Admin endpoints

## 3. Design decisions

### Service interface

Single `INutritionService` for all operations (not separate MealLog/MealSchedule services — they are tightly coupled and scoped).

### Meal log list pagination

Simple offset pagination: `page` (default 1) + `pageSize` (default 25, max 100). No cursor pagination needed for MVP.

### AI stub behavior

Per DEC-033: adapter throws `AiUnavailableException("FITMATE.AI.UNAVAILABLE")` immediately — no HTTP call, no fake response. Controller catches → returns 503.

### Error codes

```csharp
public static class NutritionErrorCodes
{
    public const string MealNotFound = "FITMATE.NUTRITION.MEAL_NOT_FOUND";
    public const string NotOwner = "FITMATE.NUTRITION.NOT_OWNER";
    public const string ScheduleNotFound = "FITMATE.NUTRITION.SCHEDULE_NOT_FOUND";
}
```

AI error codes reuse `ErrorCodes.AI.Unavailable` from Domain.

## 4. Acceptance criteria

- `dotnet build` 0 errors, no new warnings.
- `NutritionService` unit tests ≥ 5 cases: list empty, list with items, list date filter, create meal, update meal (confirm), update not owner → 404 semantics, get schedule (create-if-none), upsert schedule.
- `NutritionScan` returns 503.
- All 4 endpoints wired in `NutritionController` with DI registration.
- OpenAPI unchanged (already complete).

## 5. Commit phases

1. **`feat(be): add Nutrition abstractions, DTOs and error codes [BE-170]`** — `INutritionService`, `NutritionDtos`, `NutritionErrorCodes`.
2. **`feat(be): add NutritionService, EF store and AI stub [BE-170]`** — `NutritionService` + `INutritionStore` + `EfNutritionStore` + `IAiAdapter` + `AiNutritionStubAdapter` + `NutritionController` + DI.
3. **`test(be): add NutritionServiceTests with 6 cases [BE-170]`** — 6 unit tests.
4. **`docs(be): mark BE-170 done, add session 33 handoff [BE-170]`** — tasks.md + handoff.md.

Total: 4 commits.
