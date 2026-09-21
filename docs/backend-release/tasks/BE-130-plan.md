# BE-130 — My Bookings (member + PT) — Plan

> **Goal:** Member + PT list appointments of their own with status filter + pagination.

**Status:** ALREADY IMPLEMENTED — task needs verification + test coverage gap fill + status update.

## 1. Existing state (verified 2026-09-21)

### Application layer (`src/Fitmate.Application/Booking/`)
- ✅ `IBookingService.ListByMemberAsync(Guid memberId, BookingStatus? status, int page, int pageSize, CancellationToken ct)`
- ✅ `IBookingService.ListByPtAsync(Guid ptUserId, BookingStatus? status, int page, int pageSize, CancellationToken ct)`
- ✅ `BookingService` impl returns `(IReadOnlyList<Appointment> Items, int Total)` tuple
- ✅ Store: `IAppointmentStore.ListByMemberPagedAsync` / `ListByPtPagedAsync`

### API layer (`src/Fitmate.Api/Booking/BookingsController.cs`)
- ✅ `GET /api/v1/me/bookings` — `ListMyBookings` — member scope, query: `status`, `page`, `pageSize`
- ✅ `GET /api/v1/me/trainer-bookings` — `ListMyTrainerBookings` — PT scope (`[Authorize(Roles = "PT")]`)
- ✅ `NormalizePage(page, pageSize)` clamps pageSize 1..100 (default 25)
- ✅ `ParseStatus(string? raw)` lenient enum parse

### OpenAPI (`docs/backend-release/openapi/fitmate-v1.yaml`)
- ✅ Both endpoints documented with `status`, `page`, `pageSize` query params
- ✅ Response: `{ items: [...], total, page, pageSize }`
- ✅ `AppointmentStatus` enum ref + `Appointment` schema

### Unit tests (`tests/Fitmate.UnitTests/BookingServiceTests.cs`)
- ✅ `ListByMember_Filter` (1 test) — covers member list happy path
- ❌ Missing: `ListByPt_Filter`, pagination edge cases, status filter

## 2. Gaps to close

| # | Gap | Severity | Plan |
|---|-----|----------|------|
| 1 | `ListByPt` filter test | Low | Add 1 test mirroring `ListByMember_Filter` |
| 2 | Status filter test | Low | Add 1 test verifying status filter narrows results |
| 3 | Pagination total vs items consistency | Low | Add 1 test verifying total >= items.length when pageSize < total |
| 4 | `tasks.md` status | Cosmetic | Mark BE-130 DONE with implementation note |

No code changes required for gap 1-3 — only test additions. No migration, no OpenAPI change.

## 3. Out of scope (deferred to BE-060 completion)

- Confirm/Accept endpoints (`POST /me/bookings/{id}/accept`) — requires `AppointmentStatus.Confirmed` enum migration per DEC-026, still open.
- Cursor pagination — current offset-based is sufficient for MVP per BE-060 PARTIAL_DONE scope.
- Admin list endpoint — listed in tasks.md as BE-130 dep but admin filter lives in BE-110.

## 4. Acceptance criteria

- All existing tests still PASS.
- 3 new tests PASS (ListByPt_Filter, StatusFilter_NarrowsResults, Pagination_TotalReflectsAllItems).
- `dotnet build` 0 errors, no new warnings.
- `tasks.md` BE-130 marked DONE.
- `handoff.md` updated with session 32 entry.

## 5. Commit phases

1. `test(be): add BookingService list tests for PT and pagination [BE-130]` — 3 new tests in `BookingServiceTests.cs`.
2. `docs(be): mark BE-130 done, add session 32 handoff [BE-130]` — tasks.md + handoff.md.

Single substantive commit (tests) + 1 doc commit. Total 2 commits.
