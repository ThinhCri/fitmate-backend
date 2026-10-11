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
    AppointmentStatus Status,
    long PaidOutVnd = 0,
    string SettlementStatus = "pending_review",
    DateTime? TransferredAtUtc = null,
    string? BankReference = null,
    string? PayeeBankName = null,
    string? PayeeAccountNumber = null,
    string? PayeeAccountName = null);

public interface ITrainerRevenueStore
{
    Task<IReadOnlyList<TrainerBookingRevenueRow>> ListBookingsInRangeAsync(Guid ptUserId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    Task<(IReadOnlyList<TrainerBookingRevenueRow> Items, int Total)> ListHistoryPagedAsync(Guid ptUserId, int skip, int take, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
