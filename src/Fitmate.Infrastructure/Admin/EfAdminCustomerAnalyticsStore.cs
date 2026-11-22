using Fitmate.Application.Admin;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Admin;

public sealed class EfAdminCustomerAnalyticsStore(FitmateDbContext db) : IAdminCustomerAnalyticsStore
{
    private sealed class UserAiAggregate
    {
        public Guid UserId { get; set; }
        public int Count { get; set; }
        public long TotalVnd { get; set; }
        public DateTime MaxDate { get; set; }
    }

    private sealed class UserPtAggregate
    {
        public Guid UserId { get; set; }
        public int Count { get; set; }
        public long TotalVnd { get; set; }
        public DateTime MaxDate { get; set; }
    }

    public async Task<CustomerAnalyticsPage> GetAsync(CustomerAnalyticsQuery query, CancellationToken ct)
    {
        var totalUsers = await db.Users.CountAsync(u => u.DeletedAt == null, ct);

        DateTime? fromUtc = query.Range switch
        {
            "7d" => DateTime.UtcNow.AddDays(-7),
            "30d" => DateTime.UtcNow.AddDays(-30),
            "90d" => DateTime.UtcNow.AddDays(-90),
            _ => null
        };

        // 1. AI Purchases - count all orders including refunded ones as normal paid orders per user requirement
        var aiOrdersQuery = from order in db.AiPlayOrders.AsNoTracking()
                            join sub in db.AiSubscriptions.AsNoTracking() on order.SubscriptionId equals sub.Id
                            where (order.State == "PROCESSED" || order.State == "PARTIALLY_REFUNDED" || order.State == "REFUNDED" || order.State == "PENDING_REFUND" || order.State == "SUBSCRIPTION_STATE_ACTIVE" || order.State == "SUBSCRIPTION_STATE_REVOKED")
                            select new { sub.UserId, TotalVnd = order.TotalVnd > 0 ? order.TotalVnd : 35000L, order.PurchasedAtUtc };

        if (fromUtc.HasValue)
        {
            aiOrdersQuery = aiOrdersQuery.Where(o => o.PurchasedAtUtc >= fromUtc.Value);
        }

        var aiRaw = await aiOrdersQuery.ToListAsync(ct);
        if (aiRaw.Count == 0)
        {
            var fallbackSubs = await db.AiSubscriptions.AsNoTracking()
                .Where(s => !fromUtc.HasValue || (s.VerifiedAtUtc > DateTime.MinValue ? s.VerifiedAtUtc : s.CreatedAtUtc) >= fromUtc.Value)
                .Select(s => new {
                    s.UserId,
                    TotalVnd = 35000L,
                    PurchasedAtUtc = s.VerifiedAtUtc > DateTime.MinValue ? s.VerifiedAtUtc : s.CreatedAtUtc
                }).ToListAsync(ct);
            aiRaw = fallbackSubs;
        }
        var aiByUser = aiRaw
            .GroupBy(x => x.UserId)
            .Select(g => new UserAiAggregate
            {
                UserId = g.Key,
                Count = g.Count(),
                TotalVnd = g.Sum(x => x.TotalVnd),
                MaxDate = g.Max(x => x.PurchasedAtUtc)
            })
            .ToDictionary(x => x.UserId);

        // 2. PT Purchases (Payments + direct Appointments + SessionPackages)
        var ptPaymentsQuery = db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Paid || p.CompletedAt != null);

        if (fromUtc.HasValue)
        {
            ptPaymentsQuery = ptPaymentsQuery.Where(p => (p.CompletedAt ?? p.CreatedAt) >= fromUtc.Value);
        }

        var ptPayments = await ptPaymentsQuery
            .Select(p => new
            {
                p.Id,
                p.AppointmentId,
                p.PackageId,
                p.AmountVnd,
                PaidAt = p.CompletedAt ?? p.CreatedAt
            })
            .ToListAsync(ct);

        var paymentIds = ptPayments.Select(p => p.Id).ToHashSet();
        var apptByPaymentId = await db.Appointments.AsNoTracking()
            .Where(a => a.PaymentId != null && paymentIds.Contains(a.PaymentId.Value))
            .Select(a => new { PaymentId = a.PaymentId!.Value, a.MemberId })
            .ToDictionaryAsync(a => a.PaymentId, a => a.MemberId, ct);

        var pkgByPaymentId = await db.SessionPackages.AsNoTracking()
            .Where(p => p.PaymentId != null && paymentIds.Contains(p.PaymentId.Value))
            .Select(p => new { PaymentId = p.PaymentId!.Value, p.MemberId })
            .ToDictionaryAsync(p => p.PaymentId, p => p.MemberId, ct);

        var apptIds = ptPayments.Where(p => p.AppointmentId != null).Select(p => p.AppointmentId!.Value).Distinct().ToList();
        var apptMembers = apptIds.Count > 0
            ? await db.Appointments.AsNoTracking()
                .Where(a => apptIds.Contains(a.Id))
                .Select(a => new { a.Id, a.MemberId })
                .ToDictionaryAsync(a => a.Id, a => a.MemberId, ct)
            : new Dictionary<Guid, Guid>();

        var pkgIds = ptPayments.Where(p => p.PackageId != null).Select(p => p.PackageId!.Value).Distinct().ToList();
        var pkgMembers = pkgIds.Count > 0
            ? await db.SessionPackages.AsNoTracking()
                .Where(pkg => pkgIds.Contains(pkg.Id))
                .Select(pkg => new { pkg.Id, pkg.MemberId })
                .ToDictionaryAsync(pkg => pkg.Id, pkg => pkg.MemberId, ct)
            : new Dictionary<Guid, Guid>();

        var ptPaidRows = new List<(Guid UserId, long AmountVnd, DateTime PaidAt)>();
        var countedApptIds = new HashSet<Guid>();
        var countedPkgIds = new HashSet<Guid>();

        foreach (var p in ptPayments)
        {
            if (p.AppointmentId.HasValue && apptMembers.TryGetValue(p.AppointmentId.Value, out var mId))
            {
                ptPaidRows.Add((mId, p.AmountVnd, p.PaidAt));
                countedApptIds.Add(p.AppointmentId.Value);
            }
            else if (apptByPaymentId.TryGetValue(p.Id, out var apptMemberId))
            {
                ptPaidRows.Add((apptMemberId, p.AmountVnd, p.PaidAt));
            }
            else if (p.PackageId.HasValue && pkgMembers.TryGetValue(p.PackageId.Value, out var pkgMId))
            {
                ptPaidRows.Add((pkgMId, p.AmountVnd, p.PaidAt));
                countedPkgIds.Add(p.PackageId.Value);
            }
            else if (pkgByPaymentId.TryGetValue(p.Id, out var pkgMemberId))
            {
                ptPaidRows.Add((pkgMemberId, p.AmountVnd, p.PaidAt));
            }
        }

        // Also include confirmed/completed appointments directly if they weren't caught via payments
        var directApptsQuery = db.Appointments.AsNoTracking()
            .Where(a => a.Status != AppointmentStatus.Held &&
                        a.Status != AppointmentStatus.Cancelled &&
                        a.Status != AppointmentStatus.Expired &&
                        (a.ActualPaidVnd > 0 || a.PriceListedVnd > 0));

        if (fromUtc.HasValue)
        {
            directApptsQuery = directApptsQuery.Where(a => (a.CompletedAtUtc ?? a.ConfirmedAtUtc ?? a.CreatedAt) >= fromUtc.Value);
        }

        var directAppts = await directApptsQuery
            .Select(a => new
            {
                a.Id,
                a.MemberId,
                Amount = a.ActualPaidVnd > 0 ? a.ActualPaidVnd : a.PriceListedVnd,
                Date = a.CompletedAtUtc ?? a.ConfirmedAtUtc ?? a.CreatedAt
            })
            .ToListAsync(ct);

        foreach (var a in directAppts)
        {
            if (!countedApptIds.Contains(a.Id))
            {
                ptPaidRows.Add((a.MemberId, a.Amount, a.Date));
            }
        }

        // Also include active/stopped session packages directly if they weren't caught via payments
        var directPkgsQuery = db.SessionPackages.AsNoTracking()
            .Where(p => p.Status != SessionPackageStatus.Held &&
                        p.Status != SessionPackageStatus.PaymentPending &&
                        p.TotalPriceVnd > 0);

        if (fromUtc.HasValue)
        {
            directPkgsQuery = directPkgsQuery.Where(p => p.CreatedAt >= fromUtc.Value);
        }

        var directPkgs = await directPkgsQuery
            .Select(p => new
            {
                p.Id,
                p.MemberId,
                Amount = p.TotalPriceVnd,
                Date = p.CreatedAt
            })
            .ToListAsync(ct);

        foreach (var p in directPkgs)
        {
            if (!countedPkgIds.Contains(p.Id))
            {
                ptPaidRows.Add((p.MemberId, p.Amount, p.Date));
            }
        }

        var ptByUser = ptPaidRows
            .GroupBy(x => x.UserId)
            .Select(g => new UserPtAggregate
            {
                UserId = g.Key,
                Count = g.Count(),
                TotalVnd = g.Sum(x => x.AmountVnd),
                MaxDate = g.Max(x => x.PaidAt)
            })
            .ToDictionary(x => x.UserId);

        // 3. Unique paying users & Cohort metrics
        var allPayingUserIds = aiByUser.Keys.Union(ptByUser.Keys).ToHashSet();
        var totalPayingUsers = allPayingUserIds.Count;
        var conversionRate = totalUsers > 0
            ? Math.Round((decimal)totalPayingUsers / totalUsers * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var aiBuyersCount = aiByUser.Count;
        var ptClientsCount = ptByUser.Count;
        var dualCustomersCount = allPayingUserIds.Count(id => aiByUser.ContainsKey(id) && ptByUser.ContainsKey(id));
        var aiOnlyCount = allPayingUserIds.Count(id => aiByUser.ContainsKey(id) && !ptByUser.ContainsKey(id));
        var ptOnlyCount = allPayingUserIds.Count(id => !aiByUser.ContainsKey(id) && ptByUser.ContainsKey(id));
        var freeUsersCount = Math.Max(0, totalUsers - totalPayingUsers);

        var totalAiRevenue = aiRaw.Sum(x => x.TotalVnd);
        var totalPtRevenue = ptPaidRows.Sum(x => x.AmountVnd);
        var totalCombinedRevenue = totalAiRevenue + totalPtRevenue;
        var arppu = totalPayingUsers > 0 ? totalCombinedRevenue / totalPayingUsers : 0L;
        var totalAiOrders = aiRaw.Count;
        var totalPtBookings = ptPaidRows.Count;

        var summary = new CustomerAnalyticsSummary(
            totalUsers,
            totalPayingUsers,
            conversionRate,
            aiBuyersCount,
            ptClientsCount,
            dualCustomersCount,
            aiOnlyCount,
            ptOnlyCount,
            freeUsersCount,
            totalAiRevenue,
            totalPtRevenue,
            totalCombinedRevenue,
            arppu,
            totalAiOrders,
            totalPtBookings
        );

        // 4. Detailed Customer List
        if (totalPayingUsers == 0)
        {
            return new CustomerAnalyticsPage(summary, [], 0, query.Page, query.PageSize);
        }

        var usersDict = await db.Users.AsNoTracking()
            .Where(u => allPayingUserIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                FullName = u.FullName ?? u.UserName ?? "Hội viên",
                Email = u.Email ?? "—",
                u.PhoneNumber,
                u.CreatedAt
            })
            .ToDictionaryAsync(u => u.Id, ct);

        var details = new List<CustomerPayingDetail>();
        foreach (var uid in allPayingUserIds)
        {
            var hasAi = aiByUser.TryGetValue(uid, out var ai);
            var hasPt = ptByUser.TryGetValue(uid, out var pt);

            var aiCount = hasAi ? ai!.Count : 0;
            var aiSpent = hasAi ? ai!.TotalVnd : 0L;
            var ptCount = hasPt ? pt!.Count : 0;
            var ptSpent = hasPt ? pt!.TotalVnd : 0L;

            DateTime? lastTx = null;
            if (hasAi && hasPt)
            {
                lastTx = ai!.MaxDate > pt!.MaxDate ? ai.MaxDate : pt.MaxDate;
            }
            else if (hasAi)
            {
                lastTx = ai!.MaxDate;
            }
            else if (hasPt)
            {
                lastTx = pt!.MaxDate;
            }

            var segment = (hasAi && hasPt) ? "DUAL" : (hasAi ? "AI_ONLY" : "PT_ONLY");

            usersDict.TryGetValue(uid, out var uInfo);

            details.Add(new CustomerPayingDetail(
                uid,
                uInfo?.FullName ?? "Hội viên",
                uInfo?.Email ?? "—",
                uInfo?.PhoneNumber,
                uInfo?.CreatedAt ?? DateTime.UtcNow,
                ptCount,
                aiCount,
                ptSpent,
                aiSpent,
                ptSpent + aiSpent,
                segment,
                lastTx
            ));
        }

        // Apply filters
        var filtered = details.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.Segment) && query.Segment.ToLowerInvariant() != "all")
        {
            var targetSeg = query.Segment.ToUpperInvariant();
            filtered = filtered.Where(x => x.Segment == targetSeg);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            filtered = filtered.Where(x =>
                x.FullName.ToLowerInvariant().Contains(term) ||
                x.Email.ToLowerInvariant().Contains(term) ||
                (x.PhoneNumber != null && x.PhoneNumber.ToLowerInvariant().Contains(term)));
        }

        // Apply sort
        filtered = (query.Sort?.ToLowerInvariant()) switch
        {
            "spent_asc" => filtered.OrderBy(x => x.TotalSpentVnd),
            "pt_desc" => filtered.OrderByDescending(x => x.PtBookingCount).ThenByDescending(x => x.TotalSpentVnd),
            "ai_desc" => filtered.OrderByDescending(x => x.AiPurchaseCount).ThenByDescending(x => x.TotalSpentVnd),
            "recent" => filtered.OrderByDescending(x => x.LastTransactionAtUtc ?? DateTime.MinValue),
            "oldest" => filtered.OrderBy(x => x.RegisteredAtUtc),
            _ => filtered.OrderByDescending(x => x.TotalSpentVnd).ThenByDescending(x => x.LastTransactionAtUtc)
        };

        var allFilteredList = filtered.ToList();
        var totalFiltered = allFilteredList.Count;
        var pagedItems = allFilteredList
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return new CustomerAnalyticsPage(summary, pagedItems, totalFiltered, query.Page, query.PageSize);
    }
}
