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
                PaidOutVnd = _db.Set<ManualPtSettlement>().Where(s => s.AppointmentId == a.Id && s.Status == "paid").Select(s => (long?)s.AmountVnd).FirstOrDefault() ?? 0L,
                SettlementStatus = _db.Set<ManualPtSettlement>().Where(s => s.AppointmentId == a.Id).Select(s => s.Status).FirstOrDefault() ?? "pending_review",
                TransferredAtUtc = _db.Set<ManualPtSettlement>().Where(s => s.AppointmentId == a.Id).Select(s => s.TransferredAt).FirstOrDefault(),
                BankReference = _db.Set<ManualPtSettlement>().Where(s => s.AppointmentId == a.Id).Select(s => s.BankReference).FirstOrDefault(),
                PayeeBankName = _db.Set<ManualPtSettlement>().Where(s => s.AppointmentId == a.Id).Select(s => s.PayeeBankName).FirstOrDefault(),
                PayeeAccountNumber = _db.Set<ManualPtSettlement>().Where(s => s.AppointmentId == a.Id).Select(s => s.PayeeAccountNumber).FirstOrDefault(),
                PayeeAccountName = _db.Set<ManualPtSettlement>().Where(s => s.AppointmentId == a.Id).Select(s => s.PayeeAccountName).FirstOrDefault(),
                RefundedVnd = a.ActualPaidVnd > 0
                    ? _db.Refunds.Where(r => r.AppointmentId == a.Id && r.PaymentId == a.PaymentId &&
                        r.Status == RefundStatus.Succeeded).Sum(r => (long?)r.AmountVnd) ?? 0L
                    : 0L
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
            Status: a.Status, PaidOutVnd: a.PaidOutVnd, SettlementStatus: a.SettlementStatus,
            TransferredAtUtc: a.TransferredAtUtc, BankReference: a.BankReference,
            PayeeBankName: a.PayeeBankName, PayeeAccountNumber: a.PayeeAccountNumber,
            PayeeAccountName: a.PayeeAccountName)).ToList();
    }

    public async Task<(IReadOnlyList<TrainerBookingRevenueRow> Items, int Total)> ListHistoryPagedAsync(
        Guid ptUserId, int skip, int take, CancellationToken ct)
    {
        var query = _db.Appointments
            .Where(a => a.PtUserId == ptUserId && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.InProgress || a.Status == AppointmentStatus.AwaitingMemberConfirmation || a.Status == AppointmentStatus.Disputed || a.Status == AppointmentStatus.Completed || a.Status == AppointmentStatus.Cancelled));

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
                PaidOutVnd = _db.Set<ManualPtSettlement>().Where(s => s.AppointmentId == a.Id && s.Status == "paid").Select(s => (long?)s.AmountVnd).FirstOrDefault() ?? 0L,
                SettlementStatus = _db.Set<ManualPtSettlement>().Where(s => s.AppointmentId == a.Id).Select(s => s.Status).FirstOrDefault() ?? "pending_review",
                TransferredAtUtc = _db.Set<ManualPtSettlement>().Where(s => s.AppointmentId == a.Id).Select(s => s.TransferredAt).FirstOrDefault(),
                BankReference = _db.Set<ManualPtSettlement>().Where(s => s.AppointmentId == a.Id).Select(s => s.BankReference).FirstOrDefault(),
                PayeeBankName = _db.Set<ManualPtSettlement>().Where(s => s.AppointmentId == a.Id).Select(s => s.PayeeBankName).FirstOrDefault(),
                PayeeAccountNumber = _db.Set<ManualPtSettlement>().Where(s => s.AppointmentId == a.Id).Select(s => s.PayeeAccountNumber).FirstOrDefault(),
                PayeeAccountName = _db.Set<ManualPtSettlement>().Where(s => s.AppointmentId == a.Id).Select(s => s.PayeeAccountName).FirstOrDefault(),
                RefundedVnd = a.ActualPaidVnd > 0
                    ? _db.Refunds.Where(r => r.AppointmentId == a.Id && r.PaymentId == a.PaymentId &&
                        r.Status == RefundStatus.Succeeded).Sum(r => (long?)r.AmountVnd) ?? 0L
                    : 0L
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
            Status: a.Status, PaidOutVnd: a.PaidOutVnd, SettlementStatus: a.SettlementStatus,
            TransferredAtUtc: a.TransferredAtUtc, BankReference: a.BankReference,
            PayeeBankName: a.PayeeBankName, PayeeAccountNumber: a.PayeeAccountNumber,
            PayeeAccountName: a.PayeeAccountName)).ToList();

        return (items, total);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}

