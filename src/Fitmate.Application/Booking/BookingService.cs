using Fitmate.Application.Auth;
using Fitmate.Application.Common;
using Fitmate.Application.Finance;
using Fitmate.Application.Outbox;
using Fitmate.Application.Payments;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Finance;
using Fitmate.Domain.Payment;
using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Booking;

/// <summary>
/// Default <see cref="IBookingService"/> impl. BE-060 scope.
/// </summary>
/// <remarks>
/// <para><b>Out of scope (BE-160):</b> Confirm/Accept cần Appointment.HoldId FK + AppointmentStatus (DEC-026).
/// Service này chỉ implement create hold, cancel, complete, reschedule, list. Confirm/Accept sẽ bổ sung
/// ở BE-160 khi migrate AppointmentStatus.</para>
/// </remarks>
public class BookingService : IBookingService
{
    private readonly IAppointmentStore _appointments;
    private readonly IBookingHoldStore _holds;
    private readonly IServiceBookingLookup _services;
    private readonly IOutboxDispatcher _outbox;
    private readonly IAuthorizationGuard _authz;
    private readonly TimeSpan _holdTtl;
    private readonly IFinancialLedgerStore? _ledger;
    private readonly IPaymentStore? _payments;
    private readonly IGoogleMeetConnectionService? _meet;

    public BookingService(
        IAppointmentStore appointments,
        IBookingHoldStore holds,
        IServiceBookingLookup services,
        IOutboxDispatcher outbox,
        IAuthorizationGuard authz,
        TimeSpan? holdTtl = null,
        IFinancialLedgerStore? ledger = null,
        IPaymentStore? payments = null,
        IGoogleMeetConnectionService? meet = null)
    {
        _appointments = appointments;
        _holds = holds;
        _services = services;
        _outbox = outbox;
        _authz = authz;
        _holdTtl = holdTtl ?? TimeSpan.FromMinutes(10);
        _ledger = ledger;
        _payments = payments;
        _meet = meet;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _slotLocks = new();

    public async Task<(BookingHold Hold, Appointment Appointment)> CreateHoldAsync(
        Guid memberId, Guid ptUserId, Guid serviceId, DateTime slotStartUtc, CancellationToken ct)
    {
        // 1. Validate service.
        var svc = await _services.FindBookableAsync(serviceId, slotStartUtc, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Service {serviceId} not found or inactive.");
        if (svc.PtUserId != ptUserId)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Service does not belong to PT.",
                new Dictionary<string, string[]> { ["pt_id"] = new[] { "mismatch" } });
        if (memberId == ptUserId)
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, "PT cannot book their own service.");
        if (slotStartUtc.Kind != DateTimeKind.Utc || slotStartUtc < DateTime.UtcNow.AddMinutes(30))
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid,
                "Buổi tập phải bắt đầu sau ít nhất 30 phút và dùng thời gian UTC.",
                new Dictionary<string, string[]> { ["slot_start_utc"] = ["at least 30 minutes in the future, UTC"] });

        // 2. Concurrency & Overlap: Lock theo PT và kiểm tra interval overlap [S1, E1) và [S2, E2).
        var created = await _appointments.ExecuteWithScheduleLockAsync(ptUserId, async () =>
        {
            // Release/expire member's previous unpaid hold on this slot so they are not blocked by their own retry
            var prevHolds = await _appointments.FindActiveHeldByMemberAndSlotAsync(memberId, ptUserId, slotStartUtc, ct);
            foreach (var prev in prevHolds) prev.ExpireHold();
            if (prevHolds.Count > 0) await _appointments.SaveChangesAsync(ct);

            if (await _appointments.HasOverlappingSlotAsync(ptUserId, slotStartUtc, svc.DurationMinutes, null, ct))
                throw new ConflictException(ErrorCodes.Booking.SlotTaken,
                    "Khung giờ này vừa có người đặt hoặc đang được giữ chỗ. Vui lòng chọn khung giờ khác.");

            // 3. Tạo hold + Appointment(Held).
            var hold = BookingHold.Create(memberId, ptUserId, serviceId, slotStartUtc, _holdTtl);
            var appt = Appointment.CreateHold(
                memberId, ptUserId, serviceId,
                slotStartUtc, slotStartUtc, svc.DurationMinutes, svc.PriceVnd);

            await _holds.AddAsync(hold, ct);
            await _appointments.AddAsync(appt, ct);
            await _appointments.SaveChangesAsync(ct);
            await _holds.SaveChangesAsync(ct);

            if (svc.DeliveryMode != ServiceDeliveryMode.Online)
                _outbox.Enqueue("booking.held", new { holdId = hold.Id, appointmentId = appt.Id, memberId, ptUserId });
            return (Hold: hold, Appointment: appt);
        }, ct);

        if (svc.DeliveryMode == ServiceDeliveryMode.Online)
        {
            try
            {
                if (_meet is null)
                    throw new DomainException("GOOGLE_MEET_NOT_CONFIGURED", "Google Meet chưa được cấu hình.", 503);
                // Prepare the private, appointment-specific link before the member can pay.
                // Never call Google while a schedule/database transaction is open.
                await _meet.CreateForAppointmentAsync(ptUserId, created.Appointment.Id, ct);
                _outbox.Enqueue("booking.held", new { holdId = created.Hold.Id,
                    appointmentId = created.Appointment.Id, memberId, ptUserId });
                await _appointments.SaveChangesAsync(ct);
            }
            catch
            {
                // Request cancellation must not leave an unpaid slot held after Meet fails.
                await _appointments.ExecuteWithScheduleLockAsync(ptUserId, async () =>
                {
                    created.Hold.Release();
                    created.Appointment.Cancel("Không thể chuẩn bị liên kết Google Meet.");
                    await _appointments.SaveChangesAsync(CancellationToken.None);
                    await _holds.SaveChangesAsync(CancellationToken.None);
                    return true;
                }, CancellationToken.None);
                throw;
            }
        }
        return created;
    }

    public async Task<Appointment> CancelAsync(Guid actorUserId, Guid appointmentId, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Reason phải có ít nhất 5 ký tự.",
                new Dictionary<string, string[]> { ["reason"] = new[] { "min 5 chars" } });

        var appt = await _appointments.FindByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {appointmentId} not found.");
        if (appt.MemberId != actorUserId && appt.PtUserId != actorUserId)
            throw new ForbiddenException(ErrorCodes.Booking.NotOwner, "Not owner or PT.");

        if (await _appointments.IsPackageAppointmentAsync(appointmentId, ct))
            throw new ConflictException("PACKAGE_ACTION_REQUIRED",
                "Buổi này thuộc một gói PT; không thể hủy riêng qua luồng lịch lẻ.");

        if (appt.Status == AppointmentStatus.Cancelled)
            return appt;

        var isPaidOrConfirmed = appt.PaymentId.HasValue || appt.ActualPaidVnd > 0 || appt.Status is AppointmentStatus.Confirmed;
        if (isPaidOrConfirmed && appt.MemberId == actorUserId && DateTime.UtcNow > appt.SlotStartUtc.AddHours(-48))
            throw new ConflictException(
                ErrorCodes.Booking.CannotTransition,
                "Bạn chỉ có thể tự hủy lịch trước giờ bắt đầu ít nhất 48 giờ. Vui lòng liên hệ hỗ trợ nếu có tình huống khẩn cấp.");

        return await _appointments.ExecuteWithScheduleLockAsync(appt.PtUserId, async () =>
        {
            appt.Cancel(reason);
            await _appointments.SaveChangesAsync(ct);

            var activeHold = await _holds.FindByIdAsync(appt.Id, ct);
            if (activeHold != null && activeHold.Status == "active")
            {
                activeHold.Release();
                await _holds.SaveChangesAsync(ct);
            }

            if (_payments != null && appt.PaymentId.HasValue)
            {
                var payment = await _payments.FindByIdForUpdateAsync(appt.PaymentId.Value, ct);
                if (payment != null && payment.Status is PaymentStatus.Paid or PaymentStatus.PartialRefunded)
                {
                    var pendingRefunds = await _payments.GetPendingRefundsTotalAsync(payment.Id, ct);
                    var remainingRefund = payment.AmountVnd - payment.RefundedSoFarVnd - pendingRefunds;
                    if (remainingRefund > 0)
                    {
                        var refund = Refund.CreateSystemRefund(payment.Id, appt.Id, remainingRefund, $"Auto refund on cancellation: {reason}");
                        _payments.AddRefund(refund);
                        await _payments.SaveChangesAsync(ct);

                        _outbox.Enqueue("payment.refund_requested", new
                        {
                            refundId = refund.Id,
                            paymentId = payment.Id,
                            appointmentId = appt.Id,
                            amountVnd = remainingRefund,
                            reason
                        });
                    }
                }
            }

            _outbox.Enqueue("booking.cancelled", new { appointmentId, actorUserId, reason, memberId = appt.MemberId, ptUserId = appt.PtUserId });
            return appt;
        }, ct);
    }

    public async Task<Appointment> ConfirmAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct);
        var appt = await _appointments.FindByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {appointmentId} not found.");
        if (appt.PtUserId != ptUserId)
            throw new ForbiddenException(ErrorCodes.Booking.NotOwner, "Not owner PT.");

        return await _appointments.ExecuteWithScheduleLockAsync(ptUserId, async () =>
        {
            appt.ConfirmByPt();
            await _appointments.SaveChangesAsync(ct);
            _outbox.Enqueue("booking.confirmed", new { appointmentId, ptUserId, memberId = appt.MemberId });
            return appt;
        }, ct);
    }

    public async Task<Appointment> StartAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct);
        var appt = await _appointments.FindByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {appointmentId} not found.");
        if (appt.PtUserId != ptUserId)
            throw new ForbiddenException(ErrorCodes.Booking.NotOwner, "Not owner PT.");

        appt.StartByPt();
        await _appointments.SaveChangesAsync(ct);
        _outbox.Enqueue("booking.started", new { appointmentId, ptUserId, memberId = appt.MemberId });
        return appt;
    }

    public async Task<Appointment> CompleteAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct)
    {
        // BE-090: enforce role PT ở Application.
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct);
        var appt = await _appointments.FindByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {appointmentId} not found.");
        if (appt.PtUserId != ptUserId)
            throw new ForbiddenException(ErrorCodes.Booking.NotOwner, "Not owner PT.");

        return await _appointments.ExecuteWithScheduleLockAsync(ptUserId, async () =>
        {
            appt.CompleteByPt();
            await _appointments.SaveChangesAsync(ct);
            _outbox.Enqueue("booking.completion_submitted", new { appointmentId, ptUserId, memberId = appt.MemberId, deadlineUtc = appt.MemberConfirmationDeadlineUtc });
            return appt;
        }, ct);

    }

    public async Task<Appointment> ConfirmCompletionAsync(Guid memberId, Guid appointmentId, CancellationToken ct)
    {
        var appt = await _appointments.FindByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {appointmentId} not found.");
        if (appt.MemberId != memberId)
            throw new ForbiddenException(ErrorCodes.Booking.NotOwner, "Only the booking member can confirm completion.");

        return await _appointments.ExecuteWithScheduleLockAsync(appt.PtUserId, async () =>
        {
            appt.ConfirmCompletionByMember();
            await RecordCompletionLedgerAsync(appt, ct);
            await _appointments.SaveChangesAsync(ct);
            _outbox.Enqueue("booking.completed", new { appointmentId, ptUserId = appt.PtUserId, memberId });
            return appt;
        }, ct);
    }

    public async Task<Appointment> DisputeCompletionAsync(Guid memberId, Guid appointmentId, string reason, CancellationToken ct)
    {
        var appt = await _appointments.FindByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {appointmentId} not found.");
        if (appt.MemberId != memberId)
            throw new ForbiddenException(ErrorCodes.Booking.NotOwner, "Only the booking member can report a completion issue.");
        return await _appointments.ExecuteWithScheduleLockAsync(appt.PtUserId, async () =>
        {
            appt.DisputeCompletion(reason);
            await _appointments.SaveChangesAsync(ct);
            _outbox.Enqueue("booking.disputed", new { appointmentId, ptUserId = appt.PtUserId, memberId, reason = reason.Trim() });
            return appt;
        }, ct);
    }

    private async Task RecordCompletionLedgerAsync(Appointment appt, CancellationToken ct)
    {
        if (_ledger == null || appt.ActualPaidVnd <= 0 || await _ledger.HasEntriesForAppointmentAsync(appt.Id, ct)) return;
        var completedAt = appt.CompletedAtUtc ?? DateTime.UtcNow;
        var entries = new[]
        {
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.GrossBookingFee, appt.ActualPaidVnd, completedAt, appt.PtUserId.ToString(), appointmentId: appt.Id, paymentId: appt.PaymentId, sourceEventId: $"booking.completed:{appt.Id}"),
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.PlatformCommission, appt.PlatformFeeVnd, completedAt, appt.PtUserId.ToString(), appointmentId: appt.Id, paymentId: appt.PaymentId, sourceEventId: $"booking.completed:{appt.Id}"),
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.TrainerPayable, appt.TrainerShareVnd, completedAt, appt.PtUserId.ToString(), appointmentId: appt.Id, paymentId: appt.PaymentId, sourceEventId: $"booking.completed:{appt.Id}")
        };
        await _ledger.AddEntriesAsync(entries, ct);
        await _ledger.SaveChangesAsync(ct);
    }


    public Task<(IReadOnlyList<Appointment> Items, int Total)> ListByMemberAsync(Guid memberId, AppointmentStatus? status, int page, int pageSize, CancellationToken ct)
        => _appointments.ListByMemberPagedAsync(memberId, status, page, pageSize, ct);

    public Task<(IReadOnlyList<Appointment> Items, int Total)> ListByPtAsync(Guid ptUserId, AppointmentStatus? status, int page, int pageSize, CancellationToken ct)
        => _appointments.ListByPtPagedAsync(ptUserId, status, page, pageSize, ct);

    public Task<(IReadOnlyList<Appointment> Items, int Total)> ListAdminAsync(
        AppointmentStatus? status, Guid? ptUserId, Guid? memberId, int page, int pageSize, CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 25 : (pageSize > 100 ? 100 : pageSize);
        return _appointments.ListAdminPagedAsync(status, ptUserId, memberId, page, pageSize, ct);
    }


    public Task<Appointment?> FindByIdAsync(Guid appointmentId, CancellationToken ct)
        => _appointments.FindByIdAsync(appointmentId, ct);

    public async Task<int> ExpireOldHoldsAndPendingAsync(DateTime nowUtc, CancellationToken ct)
    {
        var expiredHolds = await _appointments.GetExpiredHeldAppointmentsAsync(nowUtc, 50, ct);
        var count = 0;
        foreach (var appt in expiredHolds)
        {
            await _appointments.ExecuteWithScheduleLockAsync(appt.PtUserId, async () =>
            {
                appt.ExpireHold();
                await _appointments.SaveChangesAsync(ct);
                _outbox.Enqueue("booking.expired", new { appointmentId = appt.Id, memberId = appt.MemberId, ptUserId = appt.PtUserId, slotStartUtc = appt.SlotStartUtc });
                return true;
            }, ct);
            count++;
        }

        var expiredPending = await _appointments.GetExpiredPendingAppointmentsAsync(nowUtc, 50, ct);
        foreach (var appt in expiredPending)
        {
            await _appointments.ExecuteWithScheduleLockAsync(appt.PtUserId, async () =>
            {
                appt.ExpireAcceptance();
                await _appointments.SaveChangesAsync(ct);
                _outbox.Enqueue("booking.acceptance_expired", new { appointmentId = appt.Id, memberId = appt.MemberId, ptUserId = appt.PtUserId });
                if (appt.PaymentId.HasValue && appt.ActualPaidVnd > 0)
                {
                    _outbox.Enqueue("finance.refund_required", new { appointmentId = appt.Id, paymentId = appt.PaymentId.Value, amountVnd = appt.ActualPaidVnd, reason = "PT acceptance deadline expired" });
                }
                return true;
            }, ct);
            count++;
        }

        var awaitingConfirmation = await _appointments.GetAwaitingConfirmationPastDeadlineAsync(nowUtc, 50, ct);
        foreach (var appt in awaitingConfirmation)
        {
            await _appointments.ExecuteWithScheduleLockAsync(appt.PtUserId, async () =>
            {
                appt.AutoConfirmCompletion(nowUtc);
                await RecordCompletionLedgerAsync(appt, ct);
                await _appointments.SaveChangesAsync(ct);
                _outbox.Enqueue("booking.completed", new
                {
                    appointmentId = appt.Id,
                    memberId = appt.MemberId,
                    ptUserId = appt.PtUserId,
                    confirmationSource = "deadline"
                });
                return true;
            }, ct);
            count++;
        }

        return count;
    }
}
