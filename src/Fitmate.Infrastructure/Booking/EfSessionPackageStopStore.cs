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

    public Task<bool> IsAssignedPtAsync(Guid packageId, Guid ptUserId, CancellationToken ct) =>
        db.SessionPackages.AnyAsync(p => p.Id == packageId && p.PtUserId == ptUserId, ct);

    public async Task<IReadOnlyList<Guid>> ListTimedOutPackageIdsAsync(DateTime nowUtc,
        int limit, CancellationToken ct) => await db.SessionPackageStopRequests.AsNoTracking()
        .Where(r => r.Status == SessionPackageStopRequestStatus.Pending &&
                    r.PtResponseDeadlineUtc <= nowUtc &&
                    db.SessionPackages.Any(p => p.Id == r.PackageId &&
                        p.Status == SessionPackageStatus.StopRequested))
        .OrderBy(r => r.PtResponseDeadlineUtc).ThenBy(r => r.Id)
        .Select(r => r.PackageId).Take(limit).ToArrayAsync(ct);

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
            .Include(p => p.StopRequests).ThenInclude(r => r.Sessions)
            .SingleAsync(p => p.Id == packageId && p.MemberId == memberId, ct);
        var currentRequest = package.StopRequests.SingleOrDefault(r =>
            r.Status != SessionPackageStopRequestStatus.Withdrawn);

        if (package.Status == SessionPackageStatus.StopRequested && currentRequest is not null &&
            currentRequest.Reason == reason)
            return currentRequest;
        if (package.Status != SessionPackageStatus.Active || currentRequest is not null ||
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

    public Task<SessionPackageStopRequest> AcceptByPtAsync(Guid packageId, Guid ptUserId,
        CancellationToken ct) => payments.ExecuteInTransactionAsync(async () =>
    {
        var package = await LoadForDecisionAsync(packageId, ct);
        if (package is null || package.PtUserId != ptUserId)
            throw new NotFoundException(ErrorCodes.General.NotFound, "Không tìm thấy gói tập được phân công.");
        var request = CurrentRequest(package);
        if (package.Status != SessionPackageStatus.StopRequested || request is null)
            throw new ConflictException("PACKAGE_STOP_NOT_PENDING", "Gói tập không có yêu cầu dừng đang chờ.");
        if (request.Status == SessionPackageStopRequestStatus.Approved) return request;
        var nowUtc = DateTime.UtcNow;
        var timedOut = nowUtc >= request.PtResponseDeadlineUtc;
        if (timedOut)
            request.ApproveAfterTimeout(nowUtc);
        else
            request.ApproveByPt(nowUtc);
        db.AuditLogs.Add(AuditLog.Record(timedOut ? Refund.SystemUserId : ptUserId,
            timedOut ? "System" : "PT",
            timedOut ? "booking.package_stop_timeout_approved" : "booking.package_stop_approved",
            "SessionPackageStopRequest", request.Id));
        EnqueueApproved(package, request);
        await db.SaveChangesAsync(ct);
        return request;
    }, ct);

    public Task<SessionPackageStopRequest> WithdrawAsync(Guid packageId, Guid memberId,
        Guid requestId, CancellationToken ct) => payments.ExecuteInTransactionAsync(async () =>
    {
        var package = await LoadForDecisionAsync(packageId, ct);
        if (package is null || package.MemberId != memberId)
            throw new NotFoundException(ErrorCodes.General.NotFound, "Không tìm thấy yêu cầu dừng gói.");
        var request = package.StopRequests.SingleOrDefault(r => r.Id == requestId)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, "Không tìm thấy yêu cầu dừng gói.");
        if (request.Status == SessionPackageStopRequestStatus.Withdrawn) return request;
        if (request.Status != SessionPackageStopRequestStatus.Pending ||
            package.Status != SessionPackageStatus.StopRequested ||
            DateTime.UtcNow >= request.PtResponseDeadlineUtc)
            throw new ConflictException("PACKAGE_STOP_WITHDRAWAL_CLOSED",
                "Yêu cầu đã được duyệt hoặc hết thời gian rút.");
        package.WithdrawStopRequest(requestId, DateTime.UtcNow);
        db.AuditLogs.Add(AuditLog.Record(memberId, "Member", "booking.package_stop_withdrawn",
            "SessionPackageStopRequest", request.Id));
        outbox.Enqueue("booking.package_stop_withdrawn", new
        {
            packageId, stopRequestId = request.Id, memberId, ptUserId = package.PtUserId
        });
        await db.SaveChangesAsync(ct);
        return request;
    }, ct);

    public Task<bool> ApproveTimedOutAsync(Guid packageId, DateTime nowUtc,
        CancellationToken ct) => payments.ExecuteInTransactionAsync(async () =>
    {
        var package = await LoadForDecisionAsync(packageId, ct);
        if (package is null || package.Status != SessionPackageStatus.StopRequested)
            return false;
        var request = CurrentRequest(package);
        if (request?.Status != SessionPackageStopRequestStatus.Pending ||
            nowUtc < request.PtResponseDeadlineUtc)
            return false;
        request.ApproveAfterTimeout(nowUtc);
        db.AuditLogs.Add(AuditLog.Record(Refund.SystemUserId, "System",
            "booking.package_stop_timeout_approved", "SessionPackageStopRequest", request.Id));
        EnqueueApproved(package, request);
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    private async Task<SessionPackage?> LoadForDecisionAsync(Guid packageId, CancellationToken ct)
    {
        var paymentId = await db.SessionPackages.Where(p => p.Id == packageId)
            .Select(p => p.PaymentId).SingleOrDefaultAsync(ct);
        if (!paymentId.HasValue) return null;
        _ = await payments.FindByIdForUpdateAsync(paymentId.Value, ct)
            ?? throw new ConflictException("PACKAGE_PAYMENT_MISSING", "Không tìm thấy giao dịch của gói tập.");
        return await db.SessionPackages.Include(p => p.StopRequests)
            .SingleOrDefaultAsync(p => p.Id == packageId, ct);
    }

    private static SessionPackageStopRequest? CurrentRequest(SessionPackage package) =>
        package.StopRequests.SingleOrDefault(r => r.Status != SessionPackageStopRequestStatus.Withdrawn);

    private void EnqueueApproved(SessionPackage package, SessionPackageStopRequest request) =>
        outbox.Enqueue("booking.package_stop_approved", new
        {
            packageId = package.Id, stopRequestId = request.Id, memberId = package.MemberId,
            ptUserId = package.PtUserId, approvalSource = request.ApprovalSource.ToString(),
            refundQuoteVnd = request.RefundQuoteVnd
        });
}
