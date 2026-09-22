using Fitmate.Application.Finance;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Finance;

public class EfTrainerRevenueStore : ITrainerRevenueStore
{
    private readonly FitmateDbContext _db;

    public EfTrainerRevenueStore(FitmateDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<TrainerBookingRevenueRow>> ListBookingsInRangeAsync(
        Guid ptUserId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var query = _db.Appointments
            .Where(a => a.PtUserId == ptUserId && a.CreatedAt >= fromUtc && a.CreatedAt < toUtc);

        var appts = await query
            .Select(a => new
            {
                a.Id,
                a.SlotStartUtc,
                a.CreatedAt,
                a.PriceListedVnd,
                a.ActualPaidVnd,
                a.PlatformFeeVnd,
                a.TrainerShareVnd,
                a.Status,
                RefundedVnd = _db.Payments
                    .Where(p => p.AppointmentId == a.Id && (p.Status == PaymentStatus.Refunded || p.Status == PaymentStatus.PartialRefunded))
                    .Sum(p => (long?)p.RefundedSoFarVnd) ?? 0L
            })
            .ToListAsync(ct);

        return appts.Select(a => new TrainerBookingRevenueRow(
            AppointmentId: a.Id,
            SlotStartUtc: a.SlotStartUtc,
            CreatedAt: a.CreatedAt,
            PriceListedVnd: a.PriceListedVnd,
            ActualPaidVnd: a.ActualPaidVnd,
            PlatformFeeVnd: a.PlatformFeeVnd,
            TrainerShareVnd: a.TrainerShareVnd,
            RefundedVnd: a.RefundedVnd,
            Status: a.Status)).ToList();
    }

    public async Task<(IReadOnlyList<TrainerBookingRevenueRow> Items, int Total)> ListHistoryPagedAsync(
        Guid ptUserId, int skip, int take, CancellationToken ct)
    {
        var query = _db.Appointments
            .Where(a => a.PtUserId == ptUserId && (a.Status == BookingStatus.Paid || a.Status == BookingStatus.Confirmed || a.Status == BookingStatus.InProgress || a.Status == BookingStatus.Completed || a.Status == BookingStatus.Refunded));

        var total = await query.CountAsync(ct);

        var appts = await query
            .OrderByDescending(a => a.SlotStartUtc)
            .Skip(skip)
            .Take(take)
            .Select(a => new
            {
                a.Id,
                a.SlotStartUtc,
                a.CreatedAt,
                a.PriceListedVnd,
                a.ActualPaidVnd,
                a.PlatformFeeVnd,
                a.TrainerShareVnd,
                a.Status,
                RefundedVnd = _db.Payments
                    .Where(p => p.AppointmentId == a.Id && (p.Status == PaymentStatus.Refunded || p.Status == PaymentStatus.PartialRefunded))
                    .Sum(p => (long?)p.RefundedSoFarVnd) ?? 0L
            })
            .ToListAsync(ct);

        var items = appts.Select(a => new TrainerBookingRevenueRow(
            AppointmentId: a.Id,
            SlotStartUtc: a.SlotStartUtc,
            CreatedAt: a.CreatedAt,
            PriceListedVnd: a.PriceListedVnd,
            ActualPaidVnd: a.ActualPaidVnd,
            PlatformFeeVnd: a.PlatformFeeVnd,
            TrainerShareVnd: a.TrainerShareVnd,
            RefundedVnd: a.RefundedVnd,
            Status: a.Status)).ToList();

        return (items, total);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}

