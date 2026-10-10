using Fitmate.Application.Booking;
using Fitmate.Application.Outbox;
using Fitmate.Application.Payments;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Booking;

public sealed class EfSessionPackageStopStore(
    FitmateDbContext db, IPaymentStore payments, IOutboxDispatcher outbox) : ISessionPackageStopStore
{
    public Task<bool> IsOwnedByAsync(Guid packageId, Guid memberId, CancellationToken ct) =>
        db.SessionPackages.AnyAsync(p => p.Id == packageId && p.MemberId == memberId, ct);

    public Task<SessionPackageStopRequest> RequestAsync(Guid packageId, Guid memberId,
        string reason, CancellationToken ct) => payments.ExecuteInTransactionAsync(async () =>
    {
        // Finance mutations lock this same payment first. Keep the lock order so
        // a refund cannot be created between eligibility checks and the snapshot.
        var paymentId = await db.SessionPackages
            .Where(p => p.Id == packageId && p.MemberId == memberId)
            .Select(p => p.PaymentId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, "Không tìm thấy gói tập đã thanh toán.");
        var payment = await payments.FindByIdForUpdateAsync(paymentId, ct)
            ?? throw new ConflictException("PACKAGE_PAYMENT_MISSING", "Không tìm thấy giao dịch của gói tập.");
        var package = await db.SessionPackages.Include(p => p.Sessions)
            .Include(p => p.StopRequest).ThenInclude(r => r!.Sessions)
            .SingleAsync(p => p.Id == packageId && p.MemberId == memberId, ct);

        if (package.Status == SessionPackageStatus.StopRequested && package.StopRequest is not null &&
            package.StopRequest.Reason == reason)
            return package.StopRequest;
        if (package.Status != SessionPackageStatus.Active || package.StopRequest is not null ||
            payment.PackageId != packageId || payment.Id != package.PaymentId ||
            payment.AmountVnd != package.TotalPriceVnd ||
            payment.Status is not (PaymentStatus.Paid or PaymentStatus.PartialRefunded))
            throw new ConflictException("PACKAGE_STOP_NOT_ELIGIBLE", "Gói tập không ở trạng thái có thể yêu cầu dừng.");

        var appointments = new List<Appointment>(package.Sessions.Count);
        foreach (var session in package.Sessions.OrderBy(s => s.AppointmentId))
        {
            var appointment = await payments.FindAppointmentForUpdateAsync(session.AppointmentId, ct)
                ?? throw new ConflictException("PACKAGE_SESSION_MISSING", "Không tìm thấy buổi tập trong gói.");
            if (appointment.MemberId != memberId || appointment.PtUserId != package.PtUserId ||
                appointment.PaymentId != payment.Id ||
                appointment.ActualPaidVnd != session.PaidAllocationVnd ||
                appointment.PlatformFeeVnd != session.PlatformFeeAllocationVnd ||
                appointment.TrainerShareVnd != session.TrainerShareAllocationVnd)
                throw new ConflictException("PACKAGE_RECONCILIATION_REQUIRED", "Dữ liệu buổi tập và giao dịch không khớp.");
            appointments.Add(appointment);
        }

        var nowUtc = DateTime.UtcNow;
        var eligibleIds = appointments.Where(a => a.Status == AppointmentStatus.Confirmed &&
            a.SlotStartUtc >= nowUtc.AddHours(48)).Select(a => a.Id).ToArray();
        if (await db.Refunds.AnyAsync(r => r.PaymentId == payment.Id &&
                eligibleIds.Contains(r.AppointmentId) &&
                (r.Status == RefundStatus.Pending || r.Status == RefundStatus.Succeeded), ct) ||
            await db.Set<ManualPtSettlement>().AnyAsync(s =>
                eligibleIds.Contains(s.AppointmentId) && s.Status == "paid", ct))
            throw new ConflictException("PACKAGE_STOP_FINANCE_REVIEW_REQUIRED",
                "Một buổi đủ điều kiện đang được hoàn hoặc đã chi PT; cần Admin đối soát trước.");

        var snapshot = appointments.Select(a => (a.Id, a.Status, a.SlotStartUtc)).ToArray();
        var request = package.RequestStop(reason, snapshot, nowUtc);
        db.AuditLogs.Add(AuditLog.Record(memberId, "Member", "booking.package_stop_requested",
            "SessionPackage", packageId));
        outbox.Enqueue("booking.package_stop_requested", new
        {
            packageId, stopRequestId = request.Id, memberId, ptUserId = package.PtUserId,
            responseDeadlineUtc = request.PtResponseDeadlineUtc, refundQuoteVnd = request.RefundQuoteVnd
        });
        await db.SaveChangesAsync(ct);
        return request;
    }, ct);
}
