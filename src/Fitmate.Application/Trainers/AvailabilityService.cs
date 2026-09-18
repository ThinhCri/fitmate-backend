using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Trainers;

/// <summary>
/// Default <see cref="IAvailabilityService"/> impl.
/// </summary>
public class AvailabilityService : IAvailabilityService
{
    private readonly IAvailabilitySlotStore _store;

    public AvailabilityService(IAvailabilitySlotStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<AvailabilitySlot>> ReplaceMineAsync(Guid ptUserId, IReadOnlyList<SlotInput> slots, CancellationToken ct)
    {
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
}
