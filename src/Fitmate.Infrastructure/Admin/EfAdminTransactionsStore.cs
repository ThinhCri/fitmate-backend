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
        var items = await rows.OrderByDescending(r => r.OccurredAtUtc).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new AdminTransaction(r.Id, r.Type, r.Status, r.AmountVnd,
                r.OccurredAtUtc, r.PaymentId, r.AppointmentId, r.PackageId, r.ProviderReference))
            .ToListAsync(ct);
        return new AdminTransactionPage(items, total, page, pageSize);
    }
}
