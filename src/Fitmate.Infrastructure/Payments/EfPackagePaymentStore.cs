using Fitmate.Application.Payments;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Payments;

public sealed class EfPackagePaymentStore(FitmateDbContext db) : IPackagePaymentStore
{
    public Task<SessionPackage?> FindWithSessionsAsync(Guid packageId, CancellationToken ct) =>
        db.SessionPackages.Include(p => p.Sessions)
            .SingleOrDefaultAsync(p => p.Id == packageId, ct);

    public Task<Payment?> FindPaymentAsync(Guid packageId, CancellationToken ct) =>
        db.Payments.SingleOrDefaultAsync(p => p.PackageId == packageId, ct);

    public async Task<bool> AllReservationsActiveAsync(SessionPackage package, DateTime nowUtc, CancellationToken ct)
    {
        var appointmentIds = package.Sessions.Select(s => s.AppointmentId).ToArray();
        var holdIds = package.Sessions.Select(s => s.HoldId).ToArray();
        if (appointmentIds.Length != package.SessionCount || holdIds.Distinct().Count() != package.SessionCount)
            return false;

        var heldAppointments = await db.Appointments.CountAsync(a =>
            appointmentIds.Contains(a.Id) && a.MemberId == package.MemberId &&
            a.PtUserId == package.PtUserId && a.ServiceId == package.ServiceId &&
            a.Status == AppointmentStatus.Held &&
            a.HoldExpiresAtUtc > nowUtc, ct);
        var activeHolds = await db.BookingHolds.CountAsync(h =>
            holdIds.Contains(h.Id) && h.MemberId == package.MemberId &&
            h.PtUserId == package.PtUserId && h.ServiceId == package.ServiceId &&
            h.Status == "active" && h.ExpiresAt > nowUtc, ct);
        return heldAppointments == package.SessionCount && activeHolds == package.SessionCount;
    }
}
