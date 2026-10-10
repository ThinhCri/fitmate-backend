using Fitmate.Application.Booking;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Booking;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Booking;

/// <summary>Expires an unpaid package and all its reservations under one PT schedule lock.</summary>
public sealed class SessionPackageExpirationService(
    FitmateDbContext db, IAppointmentStore appointments, IOutboxDispatcher outbox)
{
    public async Task<int> ExpireUnpaidAsync(DateTime nowUtc, CancellationToken ct)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Expiration time must be UTC.", nameof(nowUtc));

        var candidates = await db.SessionPackages.AsNoTracking()
            .Where(p => (p.Status == SessionPackageStatus.Held ||
                         p.Status == SessionPackageStatus.OnlinePreparing) &&
                        p.PaymentId == null && p.HoldExpiresAtUtc <= nowUtc)
            .OrderBy(p => p.HoldExpiresAtUtc)
            .Select(p => new { p.Id, p.PtUserId })
            .Take(50)
            .ToArrayAsync(ct);
        var expired = 0;
        foreach (var candidate in candidates)
        {
            var changed = await appointments.ExecuteWithScheduleLockAsync(candidate.PtUserId, async () =>
            {
                var package = await db.SessionPackages.Include(p => p.Sessions)
                    .SingleOrDefaultAsync(p => p.Id == candidate.Id, ct);
                if (package is null || package.Status is not (SessionPackageStatus.Held or SessionPackageStatus.OnlinePreparing) ||
                    package.PaymentId.HasValue || package.HoldExpiresAtUtc > nowUtc)
                    return false;

                var appointmentIds = package.Sessions.Select(s => s.AppointmentId).ToArray();
                var holdIds = package.Sessions.Select(s => s.HoldId).ToArray();
                var heldAppointments = await db.Appointments
                    .Where(a => appointmentIds.Contains(a.Id)).ToListAsync(ct);
                var heldHolds = await db.BookingHolds
                    .Where(h => holdIds.Contains(h.Id)).ToListAsync(ct);
                if (heldAppointments.Count != package.SessionCount ||
                    heldHolds.Count != package.SessionCount)
                    throw new InvalidOperationException("Package reservations are incomplete; manual reconciliation required.");

                package.Expire(nowUtc);
                foreach (var appointment in heldAppointments) appointment.ExpireHold();
                foreach (var hold in heldHolds) hold.Release();
                outbox.Enqueue("booking.package_expired", new
                {
                    packageId = package.Id,
                    memberId = package.MemberId,
                    ptUserId = package.PtUserId,
                    appointmentIds
                });
                return true;
            }, ct);
            if (changed) expired++;
        }
        return expired;
    }
}
