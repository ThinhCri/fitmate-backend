using Fitmate.Application.Booking;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Booking;

public sealed class EfSessionPackageReadStore(FitmateDbContext db) : ISessionPackageReadStore
{
    public async Task<SessionPackagePageView> ListAsync(Guid actorId, bool asPt,
        int page, int pageSize, CancellationToken ct)
    {
        var query = db.SessionPackages.AsNoTracking()
            .Where(p => asPt ? p.PtUserId == actorId : p.MemberId == actorId);
        var total = await query.CountAsync(ct);
        var packages = await query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Include(p => p.Sessions).Include(p => p.StopRequests)
            .AsSplitQuery().ToArrayAsync(ct);
        return new SessionPackagePageView(await BuildViewsAsync(packages, ct), total, page, pageSize);
    }

    public async Task<SessionPackageView?> FindForParticipantAsync(Guid packageId,
        Guid actorId, CancellationToken ct)
    {
        var package = await db.SessionPackages.AsNoTracking()
            .Where(p => p.Id == packageId &&
                (p.MemberId == actorId || p.PtUserId == actorId))
            .Include(p => p.Sessions).Include(p => p.StopRequests)
            .AsSplitQuery().SingleOrDefaultAsync(ct);
        if (package is null) return null;
        return (await BuildViewsAsync([package], ct))[0];
    }

    public async Task<SessionPackagePageView> ListForAdminAsync(SessionPackageStatus? status,
        Guid? memberId, Guid? ptUserId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.SessionPackages.AsNoTracking().AsQueryable();
        if (status.HasValue) query = query.Where(p => p.Status == status.Value);
        if (memberId.HasValue) query = query.Where(p => p.MemberId == memberId.Value);
        if (ptUserId.HasValue) query = query.Where(p => p.PtUserId == ptUserId.Value);
        var total = await query.CountAsync(ct);
        var packages = await query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Include(p => p.Sessions).Include(p => p.StopRequests)
            .AsSplitQuery().ToArrayAsync(ct);
        return new SessionPackagePageView(await BuildViewsAsync(packages, ct), total, page, pageSize);
    }

    public async Task<SessionPackageView?> FindForAdminAsync(Guid packageId, CancellationToken ct)
    {
        var package = await db.SessionPackages.AsNoTracking()
            .Where(p => p.Id == packageId)
            .Include(p => p.Sessions).Include(p => p.StopRequests)
            .AsSplitQuery().SingleOrDefaultAsync(ct);
        return package is null ? null : (await BuildViewsAsync([package], ct))[0];
    }

    private async Task<IReadOnlyList<SessionPackageView>> BuildViewsAsync(
        IReadOnlyList<SessionPackage> packages, CancellationToken ct)
    {
        if (packages.Count == 0) return [];
        var appointmentIds = packages.SelectMany(p => p.Sessions)
            .Select(s => s.AppointmentId).Distinct().ToArray();
        var paymentIds = packages.Where(p => p.PaymentId.HasValue)
            .Select(p => p.PaymentId!.Value).Distinct().ToArray();
        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => appointmentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);
        var payments = await db.Payments.AsNoTracking()
            .Where(p => paymentIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
        var views = new List<SessionPackageView>(packages.Count);
        foreach (var package in packages)
        {
            if (package.Sessions.Count != package.SessionCount ||
                package.Sessions.Sum(s => s.PaidAllocationVnd) != package.TotalPriceVnd ||
                package.Sessions.Any(s => !appointments.ContainsKey(s.AppointmentId)) ||
                (package.PaymentId.HasValue && !payments.ContainsKey(package.PaymentId.Value)))
                throw new ConflictException("PACKAGE_RECONCILIATION_REQUIRED",
                    "Dữ liệu gói tập chưa khớp, cần đối soát.");
            var sessions = package.Sessions.OrderBy(s => s.Sequence).Select(s =>
            {
                var appointment = appointments[s.AppointmentId];
                if (appointment.MemberId != package.MemberId ||
                    appointment.PtUserId != package.PtUserId ||
                    appointment.ServiceId != package.ServiceId)
                    throw new ConflictException("PACKAGE_RECONCILIATION_REQUIRED",
                        "Buổi tập không khớp gói, cần đối soát.");
                return new SessionPackageSessionView(s.AppointmentId, s.Sequence,
                    appointment.SlotStartUtc, appointment.DurationMinutes,
                    appointment.Status.ToString(), s.PaidAllocationVnd);
            }).ToArray();
            var activeStop = package.StopRequests
                .Where(r => r.Status != SessionPackageStopRequestStatus.Withdrawn)
                .OrderByDescending(r => r.RequestedAtUtc).FirstOrDefault();
            var stopView = activeStop is null ? null : new SessionPackageStopView(
                activeStop.Id, activeStop.Reason, activeStop.Status.ToString(),
                activeStop.ApprovalSource.ToString(), activeStop.RequestedAtUtc,
                activeStop.PtResponseDeadlineUtc, activeStop.RefundQuoteVnd,
                activeStop.FinalizedAtUtc);
            var payment = package.PaymentId.HasValue ? payments[package.PaymentId.Value] : null;
            if (payment is not null && (payment.PackageId != package.Id ||
                payment.AmountVnd != package.TotalPriceVnd))
                throw new ConflictException("PACKAGE_RECONCILIATION_REQUIRED",
                    "Giao dịch không khớp gói, cần đối soát.");
            views.Add(new SessionPackageView(package.Id, package.MemberId, package.PtUserId,
                package.ServiceId, package.Status.ToString(), package.SessionCount,
                package.TotalPriceVnd, package.HoldExpiresAtUtc,
                package.InitialScheduleDeadlineUtc, package.MakeUpDeadlineUtc,
                package.PaymentId, payment?.Status.ToString(),
                payment?.RefundedSoFarVnd ?? 0, stopView, sessions));
        }
        return views;
    }
}
