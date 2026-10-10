using Fitmate.Application.Finance;
using Fitmate.Application.Outbox;
using Fitmate.Application.Payments;
using Fitmate.Domain.Common;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Identity;
using Fitmate.Domain.Finance;
using Fitmate.Domain.Payment;
using Fitmate.Domain.Support;
using Fitmate.Domain.Trainers;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Finance;

public sealed class EfManualSettlementStore(FitmateDbContext db, IPaymentStore payments, IOutboxDispatcher outbox) : IManualSettlementStore
{
    public async Task<BookingIncidentDecisionView> ResolveIncidentAsync(Guid adminId, Guid ticketId,
        BookingIncidentOutcome outcome, string reason, string evidenceReference, CancellationToken ct)
    {
        var link = await db.SupportTickets.AsNoTracking().Where(t => t.Id == ticketId)
            .Select(t => t.BookingId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("BOOKING_INCIDENT_NOT_FOUND", "Linked booking incident not found.");
        var paymentId = await db.Appointments.AsNoTracking().Where(a => a.Id == link)
            .Select(a => a.PaymentId).SingleOrDefaultAsync(ct)
            ?? throw new ConflictException("BOOKING_PAYMENT_REQUIRED", "A paid booking is required for this decision.");

        return await payments.ExecuteInTransactionAsync(async () =>
        {
            var payment = await payments.FindByIdForUpdateAsync(paymentId, ct)
                ?? throw new NotFoundException("PAYMENT_NOT_FOUND", "Payment not found.");
            var appointment = await payments.FindAppointmentForUpdateAsync(link, ct)
                ?? throw new NotFoundException("BOOKING_NOT_FOUND", "Booking not found.");
            var ticket = await db.SupportTickets.FromSqlInterpolated(
                    $"SELECT * FROM support_tickets WHERE id = {ticketId} FOR UPDATE")
                .SingleOrDefaultAsync(ct)
                ?? throw new NotFoundException("BOOKING_INCIDENT_NOT_FOUND", "Linked booking incident not found.");

            if (ticket.BookingId != appointment.Id || ticket.Category != "booking_dispute" ||
                appointment.PaymentId != payment.Id ||
                payment.Status is not (PaymentStatus.Paid or PaymentStatus.PartialRefunded) ||
                payment.Method != PaymentMethod.Payos)
                throw new ConflictException("BOOKING_INCIDENT_RECONCILIATION_REQUIRED",
                    "The incident, appointment and payOS payment do not match.");

            var now = DateTime.UtcNow;
            if (now < appointment.SlotStartUtc.AddMinutes(appointment.DurationMinutes))
                throw new ConflictException("BOOKING_SESSION_NOT_ENDED", "Attendance can be decided after the scheduled session ends.");

            var allocationVnd = appointment.ActualPaidVnd;
            if (payment.PackageId.HasValue)
            {
                var session = await db.SessionPackageSessions.SingleOrDefaultAsync(s =>
                    s.PackageId == payment.PackageId && s.AppointmentId == appointment.Id, ct);
                if (session is null || allocationVnd != session.PaidAllocationVnd ||
                    appointment.PlatformFeeVnd != session.PlatformFeeAllocationVnd ||
                    appointment.TrainerShareVnd != session.TrainerShareAllocationVnd)
                    throw new ConflictException("PACKAGE_RECONCILIATION_REQUIRED",
                        "The package session allocation does not match the paid booking.");
            }
            else if (payment.RequireAppointmentId() != appointment.Id || allocationVnd != payment.AmountVnd)
                throw new ConflictException("BOOKING_INCIDENT_RECONCILIATION_REQUIRED",
                    "The single-session amount does not match the payment.");

            if (allocationVnd <= 0 ||
                appointment.PlatformFeeVnd + appointment.TrainerShareVnd != allocationVnd)
                throw new ConflictException("BOOKING_INCIDENT_RECONCILIATION_REQUIRED",
                    "The booking financial snapshot is invalid.");

            Guid? refundId = null;
            PtProfile? penalizedPt = null;
            if (outcome == BookingIncidentOutcome.PtNoShow)
            {
                if (appointment.Status is not (AppointmentStatus.Confirmed or AppointmentStatus.InProgress or
                    AppointmentStatus.AwaitingMemberConfirmation or AppointmentStatus.Disputed))
                    throw new ConflictException("BOOKING_DECISION_NOT_ALLOWED", "PT no-show cannot resolve this booking state.");
                var settlement = await db.Set<ManualPtSettlement>()
                    .SingleOrDefaultAsync(s => s.AppointmentId == appointment.Id, ct);
                if (settlement?.Status == "paid")
                    throw new ConflictException("PAYOUT_ALREADY_TRANSFERRED",
                        "A paid-out booking requires manual finance reconciliation before refund.");
                var refundedForSession = await db.Refunds.Where(r => r.PaymentId == payment.Id &&
                        r.AppointmentId == appointment.Id && r.Status == RefundStatus.Succeeded)
                    .SumAsync(r => (long?)r.AmountVnd, ct) ?? 0;
                var pendingForSession = await db.Refunds.Where(r => r.PaymentId == payment.Id &&
                        r.AppointmentId == appointment.Id && r.Status == RefundStatus.Pending)
                    .SumAsync(r => (long?)r.AmountVnd, ct) ?? 0;
                if (refundedForSession != 0 || pendingForSession != 0 ||
                    allocationVnd > payment.AmountVnd - payment.RefundedSoFarVnd -
                    (await db.Refunds.Where(r => r.PaymentId == payment.Id && r.Status == RefundStatus.Pending)
                        .SumAsync(r => (long?)r.AmountVnd, ct) ?? 0))
                    throw new ConflictException("REFUND_RECONCILIATION_REQUIRED",
                        "An existing refund or payment balance needs manual review.");
                appointment.Cancel("Admin verified PT no-show; see incident ticket.");
                var ptProfile = await db.PtProfiles.FromSqlInterpolated(
                        $"SELECT * FROM pt_profiles WHERE \"UserId\" = {appointment.PtUserId} FOR UPDATE")
                    .SingleOrDefaultAsync(ct)
                    ?? throw new ConflictException("PT_PROFILE_REQUIRED", "PT profile needs reconciliation before a no-show penalty.");
                ptProfile.RecordVerifiedNoShow(now);
                penalizedPt = ptProfile;
                if (ptProfile.PermanentlyBanned)
                {
                    var user = await db.Users.SingleOrDefaultAsync(u => u.Id == appointment.PtUserId, ct)
                        ?? throw new ConflictException("PT_USER_REQUIRED", "PT account needs reconciliation.");
                    user.Status = UserStatus.Locked;
                    user.UpdatedAt = now;
                }
                var refund = Refund.Create(payment.Id, appointment.Id, allocationVnd,
                    $"PT no-show incident {ticket.Id}", adminId);
                db.Refunds.Add(refund);
                refundId = refund.Id;
                settlement?.Hold("PT no-show incident: refund pending.");
                outbox.Enqueue("payment.refund_requested", new
                {
                    refundId, paymentId = payment.Id, appointmentId = appointment.Id,
                    packageId = payment.PackageId, amountVnd = allocationVnd
                });
                outbox.Enqueue("booking.cancelled", new
                {
                    appointmentId = appointment.Id, actorUserId = adminId,
                    reason = "Admin verified PT no-show", memberId = appointment.MemberId,
                    ptUserId = appointment.PtUserId
                });
            }
            else
            {
                appointment.ResolveCompletedByAdmin(now);
                if (await db.FinancialLedgerEntries.AnyAsync(e => e.AppointmentId == appointment.Id &&
                    e.EntryType == FinancialLedgerEntryTypes.GrossBookingFee, ct))
                    throw new ConflictException("BOOKING_ALREADY_RECOGNIZED", "This session already has recognized revenue.");
                db.FinancialLedgerEntries.AddRange(new[]
                {
                    FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.GrossBookingFee, allocationVnd, now,
                        appointment.PtUserId.ToString(), appointmentId: appointment.Id,
                        paymentId: payment.Id, sourceEventId: $"booking.completed:{appointment.Id}"),
                    FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.PlatformCommission, appointment.PlatformFeeVnd, now,
                        appointment.PtUserId.ToString(), appointmentId: appointment.Id,
                        paymentId: payment.Id, sourceEventId: $"booking.completed:{appointment.Id}"),
                    FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.TrainerPayable, appointment.TrainerShareVnd, now,
                        appointment.PtUserId.ToString(), appointmentId: appointment.Id,
                        paymentId: payment.Id, sourceEventId: $"booking.completed:{appointment.Id}")
                });
                outbox.Enqueue("booking.completed", new
                {
                    appointmentId = appointment.Id, memberId = appointment.MemberId,
                    ptUserId = appointment.PtUserId, confirmationSource = "admin_incident_decision"
                });
            }

            ticket.ResolveBookingIncident(adminId, outcome, reason, evidenceReference, now);
            db.SupportTicketMessages.Add(ticket.Messages.Last());
            db.AuditLogs.Add(AuditLog.Record(adminId, "Admin", "booking.incident_resolved",
                "Appointment", appointment.Id,
                metadataJson: System.Text.Json.JsonSerializer.Serialize(new
                {
                    ticketId, outcome = outcome.ToString(), refundId, evidenceReference
                })));
            await db.SaveChangesAsync(ct);
            return new BookingIncidentDecisionView(ticketId, appointment.Id, outcome.ToString(), refundId,
                penalizedPt?.VerifiedNoShowStrikes, penalizedPt?.PenaltyFlags,
                penalizedPt?.SuspendedUntilUtc, penalizedPt?.PermanentlyBanned);
        }, ct);
    }

    public Task<RefundView> CreateRefundAsync(Guid adminId, CreateRefundRequest request, CancellationToken ct) =>
        payments.ExecuteInTransactionAsync(async () =>
        {
            var payment = await payments.FindByIdForUpdateAsync(request.PaymentId, ct)
                ?? throw new NotFoundException("PAYMENT_NOT_FOUND", "Payment not found.");
            if (payment.Method != PaymentMethod.Payos || payment.Status is not (PaymentStatus.Paid or PaymentStatus.PartialRefunded))
                throw new ConflictException("REFUND_NOT_ELIGIBLE", "Use the original provider workflow for this payment.");
            var appointmentId = payment.PackageId.HasValue
                ? request.AppointmentId ?? throw new DomainException("PACKAGE_APPOINTMENT_REQUIRED", "Choose a package session to refund.", 400)
                : payment.RequireAppointmentId();
            var pending = await db.Refunds.Where(r => r.PaymentId == payment.Id && r.Status == RefundStatus.Pending).ToListAsync(ct);
            var existing = pending.FirstOrDefault(r => r.AppointmentId == appointmentId &&
                r.AmountVnd == request.AmountVnd && r.Reason == request.Reason);
            if (existing != null) return View(existing);
            SessionPackageSession? packageSession = null;
            Appointment? packageAppointment = null;
            if (payment.PackageId.HasValue)
            {
                var packageStatus = await db.SessionPackages.Where(p => p.Id == payment.PackageId.Value)
                    .Select(p => (SessionPackageStatus?)p.Status).SingleOrDefaultAsync(ct);
                packageSession = await db.SessionPackageSessions.SingleOrDefaultAsync(s =>
                    s.PackageId == payment.PackageId.Value && s.AppointmentId == appointmentId, ct);
                packageAppointment = await payments.FindAppointmentForUpdateAsync(appointmentId, ct);
                if (packageStatus is not (SessionPackageStatus.Active or SessionPackageStatus.Stopped) || packageSession is null ||
                    packageAppointment?.PaymentId != payment.Id ||
                    packageAppointment.ActualPaidVnd != packageSession.PaidAllocationVnd ||
                    packageAppointment.PlatformFeeVnd != packageSession.PlatformFeeAllocationVnd ||
                    packageAppointment.TrainerShareVnd != packageSession.TrainerShareAllocationVnd ||
                    packageAppointment.Status is not (AppointmentStatus.Confirmed or AppointmentStatus.Completed) ||
                    (packageAppointment.Status == AppointmentStatus.Confirmed &&
                     (request.AmountVnd != packageSession.PaidAllocationVnd ||
                      packageAppointment.SlotStartUtc < DateTime.UtcNow.AddHours(48))))
                    throw new ConflictException("PACKAGE_REFUND_NOT_ELIGIBLE", "Only a full session at least 48 hours ahead or a reviewed completed session can be refunded here.");
            }
            if (request.AmountVnd > payment.AmountVnd - payment.RefundedSoFarVnd - pending.Sum(r => r.AmountVnd))
                throw new ConflictException("REFUND_CAP_EXCEEDED", "Refund intents exceed the collected amount.");
            if (packageSession is not null)
            {
                var succeededForSession = await db.Refunds.Where(r => r.PaymentId == payment.Id &&
                    r.AppointmentId == appointmentId && r.Status == RefundStatus.Succeeded)
                    .SumAsync(r => (long?)r.AmountVnd, ct) ?? 0L;
                var reservedForSession = pending.Where(r => r.AppointmentId == appointmentId).Sum(r => r.AmountVnd);
                if (request.AmountVnd > packageSession.PaidAllocationVnd - succeededForSession - reservedForSession)
                    throw new ConflictException("REFUND_CAP_EXCEEDED", "Refund intents exceed this package session's allocation.");
            }
            var refund = Refund.Create(payment.Id, appointmentId, request.AmountVnd, request.Reason, adminId);
            db.Refunds.Add(refund);
            if (packageAppointment?.Status == AppointmentStatus.Confirmed)
            {
                packageAppointment.Cancel("Package session refund requested by Admin.");
                outbox.Enqueue("booking.cancelled", new
                {
                    appointmentId, actorUserId = adminId, reason = request.Reason.Trim(),
                    memberId = packageAppointment.MemberId, ptUserId = packageAppointment.PtUserId
                });
            }
            var settlement = await db.Set<ManualPtSettlement>().SingleOrDefaultAsync(s => s.AppointmentId == appointmentId, ct);
            if (settlement != null && settlement.Status != "paid") settlement.Hold("Refund pending review.");
            db.AuditLogs.Add(AuditLog.Record(adminId, "Admin", "finance.manual_refund_requested", "Refund", refund.Id));
            if (packageSession is not null)
                outbox.Enqueue("payment.refund_requested", new
                {
                    refundId = refund.Id, paymentId = payment.Id, appointmentId,
                    packageId = payment.PackageId, amountVnd = refund.AmountVnd
                });
            await db.SaveChangesAsync(ct);
            return View(refund);
        }, ct);

    public async Task<RefundView> ConfirmRefundAsync(Guid adminId, Guid refundId, ConfirmManualRefundRequest request, CancellationToken ct)
    {
        var paymentId = await db.Refunds.Where(r => r.Id == refundId).Select(r => (Guid?)r.PaymentId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("REFUND_NOT_FOUND", "Refund not found.");
        return await payments.ExecuteInTransactionAsync(async () =>
        {
            var payment = await payments.FindByIdForUpdateAsync(paymentId, ct)
                ?? throw new NotFoundException("PAYMENT_NOT_FOUND", "Payment not found.");
            var refund = await db.Refunds.SingleAsync(r => r.Id == refundId, ct);
            if (payment.Method != PaymentMethod.Payos || !refund.ApprovedAtUtc.HasValue || request.TransferredAt < refund.ApprovedAtUtc.Value)
                throw new ConflictException("REFUND_NOT_ELIGIBLE", "Invalid manual refund confirmation.");
            var bankReference = request.BankReference.Trim();
            if (refund.Status == RefundStatus.Succeeded && refund.ProviderRefundRef == bankReference) return View(refund);
            var package = payment.PackageId.HasValue
                ? await db.SessionPackages.Include(p => p.Sessions)
                    .SingleOrDefaultAsync(p => p.Id == payment.PackageId.Value, ct)
                : null;
            var latePackage = package?.Status == SessionPackageStatus.LatePaymentRefundPending;
            var activePackage = package?.Status is SessionPackageStatus.Active or SessionPackageStatus.Stopped;
            var packageSession = package?.Sessions.SingleOrDefault(s => s.AppointmentId == refund.AppointmentId);
            Appointment? packageAppointment = null;
            long packageRefundedBefore = 0;
            if (payment.PackageId.HasValue)
            {
                if (package is null || package.PaymentId != payment.Id ||
                    (latePackage && (refund.AmountVnd != payment.AmountVnd ||
                        refund.AppointmentId != package.Sessions.OrderBy(s => s.Sequence).FirstOrDefault()?.AppointmentId)) ||
                    (!latePackage && !activePackage))
                    throw new ConflictException("PACKAGE_RECONCILIATION_REQUIRED", "Package refund requires manual reconciliation.");
                if (activePackage)
                {
                    packageAppointment = await payments.FindAppointmentForUpdateAsync(refund.AppointmentId, ct);
                    if (packageSession is null || packageAppointment?.PaymentId != payment.Id ||
                        packageAppointment.ActualPaidVnd != packageSession.PaidAllocationVnd ||
                        packageAppointment.PlatformFeeVnd != packageSession.PlatformFeeAllocationVnd ||
                        packageAppointment.TrainerShareVnd != packageSession.TrainerShareAllocationVnd ||
                        packageAppointment.Status is not (AppointmentStatus.Cancelled or AppointmentStatus.Completed))
                        throw new ConflictException("PACKAGE_RECONCILIATION_REQUIRED", "Package session refund does not match its allocation.");
                    packageRefundedBefore = await db.Refunds.Where(r => r.PaymentId == payment.Id &&
                        r.AppointmentId == refund.AppointmentId && r.Status == RefundStatus.Succeeded)
                        .SumAsync(r => (long?)r.AmountVnd, ct) ?? 0L;
                    if (refund.AmountVnd > packageSession.PaidAllocationVnd - packageRefundedBefore)
                        throw new ConflictException("REFUND_CAP_EXCEEDED", "Refund exceeds this package session's allocation.");
                }
            }
            var appointmentId = package is null ? payment.RequireAppointmentId() : refund.AppointmentId;
            if (refund.Status != RefundStatus.Pending)
                throw new ConflictException("REFUND_ALREADY_PROCESSED", "Refund is no longer pending.");
            await LockReferenceAsync(bankReference, ct);
            if (await db.Refunds.AnyAsync(r => r.Id != refundId && r.ProviderRefundRef == bankReference, ct) ||
                await db.Set<ManualPtSettlement>().AnyAsync(s => s.BankReference == bankReference, ct))
                throw new ConflictException("TRANSFER_REFERENCE_REUSED", "Transfer reference already used.");
            var refundedSoFarBefore = payment.RefundedSoFarVnd;
            payment.ApplyRefund(refund.AmountVnd);
            refund.MarkManualTransferSucceeded(bankReference, request.TransferredAt);

            if (!latePackage)
            {
                var appt = packageAppointment ?? await db.Appointments.FindAsync(new object[] { appointmentId }, ct);
                // A cancelled, never-completed package session has no recognized revenue to reverse.
                if (!activePackage || appt?.Status == AppointmentStatus.Completed)
                {
                    if (activePackage && !await db.FinancialLedgerEntries.AnyAsync(e =>
                            e.AppointmentId == appointmentId &&
                            e.EntryType == FinancialLedgerEntryTypes.GrossBookingFee, ct))
                        throw new ConflictException("PACKAGE_RECONCILIATION_REQUIRED", "Completed session has no recognized revenue entry.");
                    var allocationPaid = activePackage ? packageSession!.PaidAllocationVnd : payment.AmountVnd;
                    var platformFeeVnd = activePackage ? packageSession!.PlatformFeeAllocationVnd :
                        appt?.PlatformFeeVnd ?? (long)Math.Round(payment.AmountVnd * 0.10m, MidpointRounding.AwayFromZero);
                    var cumulativeBefore = activePackage ? packageRefundedBefore : refundedSoFarBefore;
                    var cumulativeAfter = checked(cumulativeBefore + refund.AmountVnd);
                    var cumFeeBefore = (long)Math.Round(platformFeeVnd * (decimal)cumulativeBefore / allocationPaid, MidpointRounding.AwayFromZero);
                    var cumFeeAfter = (long)Math.Round(platformFeeVnd * (decimal)cumulativeAfter / allocationPaid, MidpointRounding.AwayFromZero);
                    var refundFee = cumFeeAfter - cumFeeBefore;
                    var refundTrainer = refund.AmountVnd - refundFee;
                    var occurredAt = refund.SucceededAtUtc ?? request.TransferredAt;
                    db.FinancialLedgerEntries.AddRange(new[]
                    {
                        FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.RefundGrossReduction, -refund.AmountVnd, occurredAt, adminId.ToString(), appointmentId: appointmentId, paymentId: payment.Id, refundId: refund.Id, sourceEventId: $"refund.confirmed:{refund.Id}"),
                        FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.RefundPlatformFeeReturn, -refundFee, occurredAt, adminId.ToString(), appointmentId: appointmentId, paymentId: payment.Id, refundId: refund.Id, sourceEventId: $"refund.confirmed:{refund.Id}"),
                        FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.RefundTrainerDeduction, -refundTrainer, occurredAt, adminId.ToString(), appointmentId: appointmentId, paymentId: payment.Id, refundId: refund.Id, sourceEventId: $"refund.confirmed:{refund.Id}")
                    });
                }
            }
            db.AuditLogs.Add(AuditLog.Record(adminId, "Admin", "finance.manual_refund_confirmed", "Refund", refundId,
                metadataJson: System.Text.Json.JsonSerializer.Serialize(new { request.EvidenceReference, request.TransferredAt })));
            outbox.Enqueue("finance.refund_processed", new { refundId, paymentId, appointmentId, packageId = payment.PackageId, amountVnd = refund.AmountVnd });
            await db.SaveChangesAsync(ct);
            return View(refund);
        }, ct);
    }

    public async Task<RefundView> ApproveRefundAsync(Guid adminId, Guid refundId, CancellationToken ct)
    {
        var paymentId = await db.Refunds.Where(r => r.Id == refundId).Select(r => (Guid?)r.PaymentId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("REFUND_NOT_FOUND", "Refund not found.");
        return await payments.ExecuteInTransactionAsync(async () =>
        {
            var payment = await payments.FindByIdForUpdateAsync(paymentId, ct)
                ?? throw new NotFoundException("PAYMENT_NOT_FOUND", "Payment not found.");
            var refund = await db.Refunds.SingleAsync(r => r.Id == refundId, ct);
            if (payment.Method != PaymentMethod.Payos || refund.Status != RefundStatus.Pending)
                throw new ConflictException("REFUND_NOT_ELIGIBLE", "Only pending payOS refunds can be approved for manual transfer.");
            if (refund.ApprovedAtUtc.HasValue) return View(refund);
            refund.Approve(adminId);
            db.AuditLogs.Add(AuditLog.Record(adminId, "Admin", "finance.manual_refund_approved", "Refund", refundId));
            await db.SaveChangesAsync(ct);
            return View(refund);
        }, ct);
    }

    public async Task<ManualRefundPage> ListRefundsAsync(int page, int pageSize, CancellationToken ct)
    {
        var query = db.Refunds.AsNoTracking().Where(r => db.Payments.Any(p => p.Id == r.PaymentId && p.Method == PaymentMethod.Payos));
        var total = await query.CountAsync(ct);
        var now = DateTime.UtcNow;
        var items = await query
            .OrderBy(r => r.Status == RefundStatus.Pending && r.ApprovedAtUtc != null && r.ApprovedAtUtc.Value.AddHours(48) < now ? 0 : 1)
            .ThenBy(r => r.ApprovedAtUtc ?? DateTime.MaxValue)
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(items.Select(View).ToList(), total, page, pageSize);
    }
    private static RefundView View(Refund r) => new(r.Id, r.PaymentId, r.AppointmentId, r.AmountVnd,
        r.Reason, r.Status, r.CreatedAt, r.ApprovedAtUtc, r.TransferDueAtUtc,
        r.Status == RefundStatus.Pending && r.TransferDueAtUtc < DateTime.UtcNow);

    public async Task<ManualPtSettlement> UpdateAsync(Guid appointmentId, Guid adminId, string action, Action<SettlementContext> update, CancellationToken ct)
    {
        var paymentId = await db.Appointments.Where(p => p.Id == appointmentId).Select(p => p.PaymentId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("PAYMENT_NOT_FOUND", "Payment not found.");
        return await payments.ExecuteInTransactionAsync(async () =>
        {
            // Same payment lock as refund intent creation and verified payment settlement.
            var payment = await payments.FindByIdForUpdateAsync(paymentId, ct)
                ?? throw new NotFoundException("PAYMENT_NOT_FOUND", "Payment not found.");
            var appointment = await payments.FindAppointmentForUpdateAsync(appointmentId, ct)
                ?? throw new NotFoundException("BOOKING_NOT_FOUND", "Booking not found.");
            var packageSessionVerified = false;
            long? packageSessionRefundedVnd = null;
            if (payment.PackageId.HasValue)
            {
                var allocation = await db.SessionPackageSessions.SingleOrDefaultAsync(s =>
                    s.PackageId == payment.PackageId.Value && s.AppointmentId == appointmentId, ct);
                if (allocation is null || appointment.PaymentId != payment.Id ||
                    appointment.ActualPaidVnd != allocation.PaidAllocationVnd ||
                    appointment.PlatformFeeVnd != allocation.PlatformFeeAllocationVnd ||
                    appointment.TrainerShareVnd != allocation.TrainerShareAllocationVnd)
                    throw new ConflictException("PACKAGE_RECONCILIATION_REQUIRED", "Package payout allocation does not match the appointment.");
                packageSessionVerified = true;
                packageSessionRefundedVnd = await db.Refunds
                    .Where(r => r.PaymentId == payment.Id && r.AppointmentId == appointmentId &&
                        r.Status == RefundStatus.Succeeded)
                    .SumAsync(r => (long?)r.AmountVnd, ct) ?? 0L;
            }
            var settlement = await db.Set<ManualPtSettlement>().SingleOrDefaultAsync(s => s.AppointmentId == appointmentId, ct);
            if (settlement == null)
            {
                settlement = ManualPtSettlement.Create(appointmentId, appointment.PtUserId, paymentId);
                db.Add(settlement);
            }
            var wasPaid = settlement.Status == "paid";
            var pending = await db.Refunds.AnyAsync(r => r.PaymentId == paymentId && r.Status == RefundStatus.Pending, ct);
            var dispute = await db.SupportTickets.AnyAsync(t => t.BookingId == appointmentId &&
                t.Status != Fitmate.Domain.Support.SupportTicketStatus.Resolved && t.Status != Fitmate.Domain.Support.SupportTicketStatus.Closed, ct);
            update(new SettlementContext(appointment, payment, settlement, pending, dispute,
                packageSessionVerified, packageSessionRefundedVnd));
            if (wasPaid) return settlement; // Do not emit duplicate receipts/audits.
            if (settlement.BankReference != null)
            {
                await LockReferenceAsync(settlement.BankReference, ct);
                if (await db.Set<ManualPtSettlement>().AnyAsync(s => s.AppointmentId != appointmentId && s.BankReference == settlement.BankReference, ct) ||
                    await db.Refunds.AnyAsync(r => r.ProviderRefundRef == settlement.BankReference, ct))
                    throw new ConflictException("TRANSFER_REFERENCE_REUSED", "This bank transfer has already been allocated.");
            }
            db.AuditLogs.Add(AuditLog.Record(adminId, "Admin", action, "Appointment", appointmentId,
                metadataJson: System.Text.Json.JsonSerializer.Serialize(new
                {
                    settlement.Status, settlement.AmountVnd, settlement.CustomerConfirmedAt,
                    settlement.TrainerConfirmedAt, settlement.SessionEvidenceReference,
                    settlement.PayeeEvidenceReference, settlement.HoldReason, settlement.ApprovedAt,
                    settlement.TransferredAt, settlement.TransferEvidenceReference
                })));
            if (settlement.Status == "paid")
            {
                var payoutEntry = FinancialLedgerEntry.Create(
                    FinancialLedgerEntryTypes.TrainerPayout,
                    -settlement.AmountVnd,
                    settlement.TransferredAt ?? DateTime.UtcNow,
                    adminId.ToString(),
                    appointmentId: appointmentId,
                    paymentId: paymentId,
                    settlementId: settlement.AppointmentId,
                    sourceEventId: $"settlement.transferred:{appointmentId}");
                db.FinancialLedgerEntries.Add(payoutEntry);
                outbox.Enqueue("finance.trainer_settled", new { appointmentId, ptUserId = settlement.PtUserId, amountVnd = settlement.AmountVnd });
            }
            await db.SaveChangesAsync(ct);
            return settlement;
        }, ct);
    }

    public async Task<SettlementPage> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        var query = db.Set<ManualPtSettlement>().AsNoTracking();
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(s => s.ApprovedAt).ThenBy(s => s.AppointmentId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(items, count, page, pageSize);
    }

    private async Task LockReferenceAsync(string reference, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({reference}, 0))", ct);
    }
}
