using Fitmate.Application.Finance;
using Fitmate.Application.Outbox;
using Fitmate.Application.Payments;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Domain.Finance;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Finance;

public sealed class EfManualSettlementStore(FitmateDbContext db, IPaymentStore payments, IOutboxDispatcher outbox) : IManualSettlementStore
{
    public Task<RefundView> CreateRefundAsync(Guid adminId, CreateRefundRequest request, CancellationToken ct) =>
        payments.ExecuteInTransactionAsync(async () =>
        {
            var payment = await payments.FindByIdForUpdateAsync(request.PaymentId, ct)
                ?? throw new NotFoundException("PAYMENT_NOT_FOUND", "Payment not found.");
            if (payment.Method != PaymentMethod.Payos || payment.Status is not (PaymentStatus.Paid or PaymentStatus.PartialRefunded))
                throw new ConflictException("REFUND_NOT_ELIGIBLE", "Use the original provider workflow for this payment.");
            var pending = await db.Refunds.Where(r => r.PaymentId == payment.Id && r.Status == RefundStatus.Pending).ToListAsync(ct);
            var existing = pending.FirstOrDefault(r => r.AmountVnd == request.AmountVnd && r.Reason == request.Reason);
            if (existing != null) return View(existing);
            if (request.AmountVnd > payment.AmountVnd - payment.RefundedSoFarVnd - pending.Sum(r => r.AmountVnd))
                throw new ConflictException("REFUND_CAP_EXCEEDED", "Refund intents exceed the collected amount.");
            var refund = Refund.Create(payment.Id, payment.AppointmentId, request.AmountVnd, request.Reason, adminId);
            db.Refunds.Add(refund);
            var settlement = await db.Set<ManualPtSettlement>().SingleOrDefaultAsync(s => s.AppointmentId == payment.AppointmentId, ct);
            if (settlement != null && settlement.Status != "paid") settlement.Hold("Refund pending review.");
            db.AuditLogs.Add(AuditLog.Record(adminId, "Admin", "finance.manual_refund_requested", "Refund", refund.Id));
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
            if (refund.Status != RefundStatus.Pending)
                throw new ConflictException("REFUND_ALREADY_PROCESSED", "Refund is no longer pending.");
            await LockReferenceAsync(bankReference, ct);
            if (await db.Refunds.AnyAsync(r => r.Id != refundId && r.ProviderRefundRef == bankReference, ct) ||
                await db.Set<ManualPtSettlement>().AnyAsync(s => s.BankReference == bankReference, ct))
                throw new ConflictException("TRANSFER_REFERENCE_REUSED", "Transfer reference already used.");
            var refundedSoFarBefore = payment.RefundedSoFarVnd;
            payment.ApplyRefund(refund.AmountVnd);
            refund.MarkManualTransferSucceeded(bankReference, request.TransferredAt);

            var appt = await db.Appointments.FindAsync(new object[] { payment.AppointmentId }, ct);
            var platformFeeVnd = appt?.PlatformFeeVnd ?? (long)Math.Round(payment.AmountVnd * 0.10m, MidpointRounding.AwayFromZero);
            var refundedSoFarAfter = payment.RefundedSoFarVnd;
            var cumFeeBefore = payment.AmountVnd > 0 ? (long)Math.Round(platformFeeVnd * (decimal)refundedSoFarBefore / payment.AmountVnd, MidpointRounding.AwayFromZero) : 0L;
            var cumFeeAfter = payment.AmountVnd > 0 ? (long)Math.Round(platformFeeVnd * (decimal)refundedSoFarAfter / payment.AmountVnd, MidpointRounding.AwayFromZero) : 0L;
            var refundFee = cumFeeAfter - cumFeeBefore;
            var refundTrainer = refund.AmountVnd - refundFee;
            var occurredAt = refund.SucceededAtUtc ?? request.TransferredAt;
            var adjEntries = new[]
            {
                FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.RefundGrossReduction, -refund.AmountVnd, occurredAt, adminId.ToString(), appointmentId: payment.AppointmentId, paymentId: payment.Id, refundId: refund.Id, sourceEventId: $"refund.confirmed:{refund.Id}"),
                FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.RefundPlatformFeeReturn, -refundFee, occurredAt, adminId.ToString(), appointmentId: payment.AppointmentId, paymentId: payment.Id, refundId: refund.Id, sourceEventId: $"refund.confirmed:{refund.Id}"),
                FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.RefundTrainerDeduction, -refundTrainer, occurredAt, adminId.ToString(), appointmentId: payment.AppointmentId, paymentId: payment.Id, refundId: refund.Id, sourceEventId: $"refund.confirmed:{refund.Id}")
            };
            db.FinancialLedgerEntries.AddRange(adjEntries);
            db.AuditLogs.Add(AuditLog.Record(adminId, "Admin", "finance.manual_refund_confirmed", "Refund", refundId,
                metadataJson: System.Text.Json.JsonSerializer.Serialize(new { request.EvidenceReference, request.TransferredAt })));
            outbox.Enqueue("finance.refund_processed", new { refundId, paymentId, appointmentId = payment.AppointmentId, amountVnd = refund.AmountVnd });
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
            update(new SettlementContext(appointment, payment, settlement, pending, dispute));
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
