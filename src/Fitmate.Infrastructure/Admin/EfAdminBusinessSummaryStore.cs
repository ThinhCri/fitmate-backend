using Fitmate.Application.Admin;
using Fitmate.Domain.Common;
using Fitmate.Domain.Finance;
using Fitmate.Domain.Trainers;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Admin;

public sealed class EfAdminBusinessSummaryStore(FitmateDbContext db) : IAdminBusinessSummaryStore
{
    public async Task<BusinessSummary> GetAsync(string range, DateTime fromUtc, DateTime toUtc,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var totalUsers = await db.Users.CountAsync(u => u.DeletedAt == null, ct);
        var newUsers = await db.Users.CountAsync(u => u.DeletedAt == null &&
            u.CreatedAt >= fromUtc && u.CreatedAt < toUtc, ct);
        var members = await (from user in db.Users
            join userRole in db.UserRoles on user.Id equals userRole.UserId
            join role in db.Roles on userRole.RoleId equals role.Id
            where user.DeletedAt == null && role.NormalizedName == "MEMBER"
            select user.Id).Distinct().CountAsync(ct);
        var verifiedPts = await db.PtProfiles.CountAsync(p =>
            p.VerificationStatus == PtVerificationStatus.Approved &&
            !p.PermanentlyBanned && (p.SuspendedUntilUtc == null || p.SuspendedUntilUtc <= now), ct);
        var pendingPts = await db.PtProfiles.CountAsync(p =>
            p.VerificationStatus == PtVerificationStatus.PendingReview, ct);

        var payments = db.Payments.AsNoTracking();
        var paidTransactions = await payments.CountAsync(p => p.CompletedAt >= fromUtc &&
            p.CompletedAt < toUtc, ct);
        var failedTransactions = await payments.CountAsync(p => p.Status == PaymentStatus.Failed &&
            p.UpdatedAt >= fromUtc && p.UpdatedAt < toUtc, ct);
        var pendingTransactions = await payments.CountAsync(p => p.Status == PaymentStatus.Pending, ct);
        var collected = await payments.Where(p => p.CompletedAt >= fromUtc &&
            p.CompletedAt < toUtc).SumAsync(p => (long?)p.AmountVnd, ct) ?? 0L;
        var refunded = await db.Refunds.Where(r => r.Status == RefundStatus.Succeeded &&
            r.SucceededAtUtc >= fromUtc && r.SucceededAtUtc < toUtc)
            .SumAsync(r => (long?)r.AmountVnd, ct) ?? 0L;

        var ledger = db.FinancialLedgerEntries.AsNoTracking();
        var periodLedger = ledger.Where(e => e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtc);
        var fee = await periodLedger.Where(e => e.EntryType == FinancialLedgerEntryTypes.PlatformCommission ||
            e.EntryType == FinancialLedgerEntryTypes.RefundPlatformFeeReturn)
            .SumAsync(e => (long?)e.AmountVnd, ct) ?? 0L;
        var share = await periodLedger.Where(e => e.EntryType == FinancialLedgerEntryTypes.TrainerPayable ||
            e.EntryType == FinancialLedgerEntryTypes.RefundTrainerDeduction)
            .SumAsync(e => (long?)e.AmountVnd, ct) ?? 0L;
        var paidOut = -(await periodLedger.Where(e => e.EntryType == FinancialLedgerEntryTypes.TrainerPayout)
            .SumAsync(e => (long?)e.AmountVnd, ct) ?? 0L);
        var allTimePayable = await ledger.Where(e => e.EntryType == FinancialLedgerEntryTypes.TrainerPayable ||
            e.EntryType == FinancialLedgerEntryTypes.RefundTrainerDeduction)
            .SumAsync(e => (long?)e.AmountVnd, ct) ?? 0L;
        var allTimePayoutEntries = await ledger.Where(e => e.EntryType == FinancialLedgerEntryTypes.TrainerPayout)
            .SumAsync(e => (long?)e.AmountVnd, ct) ?? 0L;

        var reviews = db.Reviews.Where(r => r.CreatedAt >= fromUtc && r.CreatedAt < toUtc);
        var reviewCount = await reviews.CountAsync(ct);
        var average = await reviews.AverageAsync(r => (double?)r.Rating, ct) ?? 0d;
        return new BusinessSummary(range, fromUtc, toUtc, totalUsers, newUsers, members,
            verifiedPts, pendingPts, paidTransactions, failedTransactions, pendingTransactions,
            collected, refunded, fee, share, paidOut, allTimePayable + allTimePayoutEntries,
            reviewCount, Math.Round((decimal)average, 2, MidpointRounding.AwayFromZero));
    }
}
