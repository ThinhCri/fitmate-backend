using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Trainers;

/// <summary>
/// Một slot input từ client. Domain tính UTC từ local + IANA timezone.
/// </summary>
public sealed record SlotInput(DateTime SlotStartLocal, string TrainerTimezone, int DurationMinutes);

/// <summary>
/// Use case cho AvailabilitySlot. Theo OpenAPI:
/// <list type="bullet">
/// <item><c>PUT /me/availability</c> — PT thay thế toàn bộ slot (batch).</item>
/// <item><c>GET /trainers/{id}/availability</c> — public xem slot Open của PT.</item>
/// </list>
/// </summary>
public interface IAvailabilityService
{
    /// <summary>
    /// Replace toàn bộ slot của PT: xóa slot Open cũ (không đụng slot Held/Booked) + insert slot mới.
    /// Slot trùng start_utc trong batch → ConflictException.
    /// </summary>
    Task<IReadOnlyList<AvailabilitySlot>> ReplaceMineAsync(Guid ptUserId, IReadOnlyList<SlotInput> slots, CancellationToken ct);

    /// <summary>List slot Open của 1 PT trong khoảng [from, to) UTC. Public cho member xem.</summary>
    Task<IReadOnlyList<AvailabilitySlot>> ListOpenForTrainerAsync(Guid ptUserId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}
