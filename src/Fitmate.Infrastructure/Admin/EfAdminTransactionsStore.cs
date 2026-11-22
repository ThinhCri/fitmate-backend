using Fitmate.Application.Admin;
using Fitmate.Domain.Common;
using Fitmate.Domain.Finance;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Admin;

public sealed class EfAdminTransactionsStore(FitmateDbContext db) : IAdminTransactionsStore
{
    public async Task<AdminTransactionPage> GetAsync(string? type, string? status, Guid? referenceId,
        DateTime? fromUtc, DateTime? toUtc, int page, int pageSize, CancellationToken ct)
    {
        var payments = db.Payments.AsNoTracking().Select(p => new {
            p.Id, Type = "payment",
            Status = p.Status == PaymentStatus.Paid ? "paid" :
            p.Status == PaymentStatus.Failed ? "failed" :
            p.Status == PaymentStatus.Refunded ? "refunded" :
            p.Status == PaymentStatus.PartialRefunded ? "partial_refunded" : "pending",
            p.AmountVnd, OccurredAtUtc = p.CreatedAt, PaymentId = (Guid?)p.Id,
            p.AppointmentId, p.PackageId, ProviderReference = p.ProviderTxnRef
        });
        var refunds = db.Refunds.AsNoTracking().Select(r => new {
            r.Id, Type = "refund",
            Status = r.Status == RefundStatus.Succeeded ? "succeeded" :
            r.Status == RefundStatus.Failed ? "failed" : "pending",
            r.AmountVnd, OccurredAtUtc = r.CreatedAt, PaymentId = (Guid?)r.PaymentId,
            AppointmentId = (Guid?)r.AppointmentId, PackageId = (Guid?)null,
            ProviderReference = r.ProviderRefundRef
        });
        var payouts = db.FinancialLedgerEntries.AsNoTracking()
            .Where(e => e.EntryType == FinancialLedgerEntryTypes.TrainerPayout)
            .Select(e => new {
                e.Id, Type = "payout", Status = "paid", AmountVnd = -e.AmountVnd,
                e.OccurredAtUtc, e.PaymentId, e.AppointmentId,
                PackageId = (Guid?)null, ProviderReference = (string?)null
            });

        var rows = type switch
        {
            "payment" => payments,
            "refund" => refunds,
            "payout" => payouts,
            _ => payments.Concat(refunds).Concat(payouts)
        };

        if (status != null) rows = rows.Where(r => r.Status == status);
        if (referenceId.HasValue)
            rows = rows.Where(r => r.Id == referenceId || r.PaymentId == referenceId ||
                r.AppointmentId == referenceId || r.PackageId == referenceId);
        if (fromUtc.HasValue) rows = rows.Where(r => r.OccurredAtUtc >= fromUtc.Value);
        if (toUtc.HasValue) rows = rows.Where(r => r.OccurredAtUtc < toUtc.Value);

        var total = await rows.CountAsync(ct);
        var pagedRows = await rows.OrderByDescending(r => r.OccurredAtUtc).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        var apptIds = pagedRows.Where(r => r.AppointmentId != null).Select(r => r.AppointmentId!.Value).Distinct().ToList();
        var appts = apptIds.Count > 0
            ? await db.Appointments.AsNoTracking().Where(a => apptIds.Contains(a.Id)).Select(a => new { a.Id, a.MemberId, a.PtUserId }).ToDictionaryAsync(a => a.Id, ct)
            : new();

        var pkgIds = pagedRows.Where(r => r.PackageId != null).Select(r => r.PackageId!.Value).Distinct().ToList();
        var pkgs = pkgIds.Count > 0
            ? await db.SessionPackages.AsNoTracking().Where(p => pkgIds.Contains(p.Id)).Select(p => new { p.Id, p.MemberId, p.PtUserId }).ToDictionaryAsync(p => p.Id, ct)
            : new();

        var items = pagedRows.Select(r => {
            Guid? memberId = null;
            Guid? ptUserId = null;
            if (r.AppointmentId.HasValue && appts.TryGetValue(r.AppointmentId.Value, out var a))
            {
                memberId = a.MemberId;
                ptUserId = a.PtUserId;
            }
            else if (r.PackageId.HasValue && pkgs.TryGetValue(r.PackageId.Value, out var pkg))
            {
                memberId = pkg.MemberId;
                ptUserId = pkg.PtUserId;
            }
            return new AdminTransaction(r.Id, r.Type, r.Status, r.AmountVnd,
                r.OccurredAtUtc, r.PaymentId, r.AppointmentId, r.PackageId, r.ProviderReference,
                memberId, ptUserId);
        }).ToList();

        return new AdminTransactionPage(items, total, page, pageSize);
    }
}
