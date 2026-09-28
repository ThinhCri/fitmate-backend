using Fitmate.Application.Auth;
using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Trainers;

/// <summary>
/// Default <see cref="IAvailabilityService"/> impl.
/// </summary>
public class AvailabilityService : IAvailabilityService
{
    private readonly IAvailabilitySlotStore _store;
    private readonly IAuthorizationGuard _authz;

    public AvailabilityService(IAvailabilitySlotStore store, IAuthorizationGuard authz)
    {
        _store = store;
        _authz = authz;
    }

    public async Task<IReadOnlyList<AvailabilitySlot>> ReplaceMineAsync(Guid ptUserId, IReadOnlyList<SlotInput> slots, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct); // BE-090
        if (slots is null || slots.Count == 0)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "slots required", new Dictionary<string, string[]> { ["slots"] = new[] { "required" } });

        // Build slot entities + validate.
        var created = new List<AvailabilitySlot>();
        var seenStartsUtc = new HashSet<DateTime>();
        foreach (var s in slots)
        {
            if (s.DurationMinutes < 15 || s.DurationMinutes > 240)
                throw new ValidationException(
                    ErrorCodes.Validation.FieldInvalid,
                    "durationMinutes 15..240",
                    new Dictionary<string, string[]> { ["durationMinutes"] = new[] { "15..240" } });
            if (string.IsNullOrWhiteSpace(s.TrainerTimezone))
                throw new ValidationException(
                    ErrorCodes.Validation.FieldInvalid,
                    "trainerTimezone required",
                    new Dictionary<string, string[]> { ["trainerTimezone"] = new[] { "required" } });

            AvailabilitySlot entity;
            try
            {
                entity = AvailabilitySlot.Create(ptUserId, s.SlotStartLocal, s.TrainerTimezone, s.DurationMinutes);
            }
            catch (Exception ex) when (ex is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new ValidationException(
                    ErrorCodes.Validation.FieldInvalid,
                    "Invalid slot: " + ex.Message,
                    new Dictionary<string, string[]> { ["slot"] = new[] { "invalid" } });
            }

            // Reject duplicate UTC start within batch.
            if (!seenStartsUtc.Add(entity.SlotStartUtc))
                throw new ConflictException(
                    ErrorCodes.Booking.SlotTaken,
                    $"Duplicate slot start_utc in batch: {entity.SlotStartUtc:O}");

            created.Add(entity);
        }

        // Replace: xóa slot Open cũ (không đụng Held/Booked để bảo vệ booking đang diễn ra).
        await _store.DeleteByPtAndStatusAsync(ptUserId, AvailabilityStatus.Open, ct);
        foreach (var s in created)
            await _store.AddAsync(s, ct);
        await _store.SaveChangesAsync(ct);

        return created;
    }

    public Task<IReadOnlyList<AvailabilitySlot>> ListOpenForTrainerAsync(Guid ptUserId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        if (toUtc <= fromUtc)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "to must be after from",
                new Dictionary<string, string[]> { ["to"] = new[] { "> from" } });
        return _store.ListOpenByPtAsync(ptUserId, fromUtc, toUtc, ct);
    }

    public async Task<IReadOnlyList<AvailabilitySlot>> ListAuthoritativeForPtAsync(Guid ptUserId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct);
        if (toUtc <= fromUtc)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "to must be after from",
                new Dictionary<string, string[]> { ["to"] = new[] { "> from" } });
        return await _store.ListByPtAsync(ptUserId, new[] { AvailabilityStatus.Open, AvailabilityStatus.Held, AvailabilityStatus.Booked }, fromUtc, toUtc, ct);
    }

    public async Task<IReadOnlyList<AvailabilitySlot>> ReplaceScopedAsync(Guid ptUserId, DateTime fromUtc, DateTime toUtc, IReadOnlyList<SlotInput> slots, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct);
        if (toUtc <= fromUtc)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "to must be after from",
                new Dictionary<string, string[]> { ["to"] = new[] { "> from" } });

        slots ??= Array.Empty<SlotInput>();

        var profileTz = await _store.GetPtTimezoneAsync(ptUserId, ct);
        var defaultTz = !string.IsNullOrWhiteSpace(profileTz) ? profileTz : "Asia/Ho_Chi_Minh";

        var created = new List<AvailabilitySlot>();
        foreach (var s in slots)
        {
            if (s.DurationMinutes < 15 || s.DurationMinutes > 240)
                throw new ValidationException(
                    ErrorCodes.Validation.FieldInvalid,
                    "durationMinutes 15..240",
                    new Dictionary<string, string[]> { ["durationMinutes"] = new[] { "15..240" } });

            var tz = !string.IsNullOrWhiteSpace(s.TrainerTimezone) ? s.TrainerTimezone : defaultTz;

            AvailabilitySlot entity;
            try
            {
                entity = AvailabilitySlot.Create(ptUserId, s.SlotStartLocal, tz, s.DurationMinutes);
            }
            catch (Exception ex) when (ex is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new ValidationException(
                    ErrorCodes.Validation.FieldInvalid,
                    "Invalid slot: " + ex.Message,
                    new Dictionary<string, string[]> { ["slot"] = new[] { "invalid" } });
            }

            var startUtc = entity.SlotStartUtc;
            var endUtc = startUtc.AddMinutes(entity.DurationMinutes);

            if (startUtc < fromUtc || endUtc > toUtc)
            {
                throw new ValidationException(
                    ErrorCodes.Validation.FieldInvalid,
                    $"Slot [{startUtc:O}, {endUtc:O}) falls outside the scoped range [{fromUtc:O}, {toUtc:O})",
                    new Dictionary<string, string[]> { ["slots"] = new[] { "outside scoped range" } });
            }

            // Reject interval overlap within batch: startA < endB && startB < endA
            foreach (var existingBatchSlot in created)
            {
                var existingStart = existingBatchSlot.SlotStartUtc;
                var existingEnd = existingStart.AddMinutes(existingBatchSlot.DurationMinutes);

                if (startUtc < existingEnd && existingStart < endUtc)
                {
                    throw new ConflictException(
                        ErrorCodes.Booking.SlotTaken,
                        $"Interval overlap between slots in batch: {startUtc:O} and {existingStart:O}");
                }
            }

            created.Add(entity);
        }

        // Concurrency/conflict check against existing Held or Booked slots in this range
        var existingLocked = await _store.ListByPtAsync(
            ptUserId,
            new[] { AvailabilityStatus.Held, AvailabilityStatus.Booked },
            fromUtc,
            toUtc,
            ct);

        foreach (var s in created)
        {
            var startUtc = s.SlotStartUtc;
            var endUtc = startUtc.AddMinutes(s.DurationMinutes);

            foreach (var locked in existingLocked)
            {
                var lockedStart = locked.SlotStartUtc;
                var lockedEnd = lockedStart.AddMinutes(locked.DurationMinutes);

                if (startUtc < lockedEnd && lockedStart < endUtc)
                {
                    throw new ConflictException(
                        ErrorCodes.Booking.SlotTaken,
                        $"Slot [{startUtc:O}, {endUtc:O}) overlaps an existing {locked.Status} slot at {lockedStart:O}");
                }
            }
        }

        // Atomically replace only Open slots in [fromUtc, toUtc)
        await _store.ReplaceScopedAsync(ptUserId, fromUtc, toUtc, created, ct);

        return created;
    }
}
