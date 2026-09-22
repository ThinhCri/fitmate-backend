using Fitmate.Domain.Common;

namespace Fitmate.Application.Finance;

public sealed record TrainerBookingRevenueRow(
    Guid AppointmentId,
    DateTime SlotStartUtc,
    DateTime CreatedAt,
    long PriceListedVnd,
    long ActualPaidVnd,
    long PlatformFeeVnd,
    long TrainerShareVnd,
    long RefundedVnd,
    BookingStatus Status);

public interface ITrainerRevenueStore
{
    Task<IReadOnlyList<TrainerBookingRevenueRow>> ListBookingsInRangeAsync(Guid ptUserId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    Task<(IReadOnlyList<TrainerBookingRevenueRow> Items, int Total)> ListHistoryPagedAsync(Guid ptUserId, int skip, int take, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
