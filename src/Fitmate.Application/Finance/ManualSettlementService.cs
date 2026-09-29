using Fitmate.Application.Auth;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;

namespace Fitmate.Application.Finance;

public sealed record ApproveSettlementRequest(DateTime CustomerConfirmedAt, DateTime TrainerConfirmedAt,
    string SessionEvidenceReference, string PayeeEvidenceReference, bool NoOpenDisputeConfirmed);
public sealed record RecordTransferRequest(string BankReference, string EvidenceReference, DateTime TransferredAt);
public sealed record SettlementHoldRequest(string Reason);
public sealed record SettlementContext(Appointment Appointment, Payment Payment, ManualPtSettlement Settlement, bool HasPendingRefund, bool HasOpenDispute = false);
public sealed record SettlementPage(IReadOnlyList<ManualPtSettlement> Items, int Total, int Page, int PageSize);
public sealed record ConfirmManualRefundRequest(string BankReference, string EvidenceReference, DateTime TransferredAt);
public sealed record ManualRefundPage(IReadOnlyList<RefundView> Items, int Total, int Page, int PageSize);

public interface IManualSettlementStore
{
    Task<ManualPtSettlement> UpdateAsync(Guid appointmentId, Guid adminId, string action, Action<SettlementContext> update, CancellationToken ct);
    Task<SettlementPage> ListAsync(int page, int pageSize, CancellationToken ct);
    Task<RefundView> CreateRefundAsync(Guid adminId, CreateRefundRequest request, CancellationToken ct);
    Task<RefundView> ConfirmRefundAsync(Guid adminId, Guid refundId, ConfirmManualRefundRequest request, CancellationToken ct);
    Task<ManualRefundPage> ListRefundsAsync(int page, int pageSize, CancellationToken ct);
}

public interface IFinanceStepUp
{
    Task EnsureAsync(Guid adminId, CancellationToken ct);
}

public sealed class ManualSettlementService(IManualSettlementStore store, IAuthorizationGuard authz, IFinanceStepUp stepUp)
{
    public async Task<RefundView> CreateRefundAsync(Guid adminId, CreateRefundRequest request, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(adminId, "Admin", ct);
        await stepUp.EnsureAsync(adminId, ct);
        if (request.AmountVnd <= 0 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
            throw new DomainException("REFUND_INVALID", "A positive amount and reason (up to 500 characters) are required.", 400);
        return await store.CreateRefundAsync(adminId, request, ct);
    }
    public async Task<RefundView> ConfirmRefundAsync(Guid adminId, Guid refundId, ConfirmManualRefundRequest request, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(adminId, "Admin", ct);
        await stepUp.EnsureAsync(adminId, ct);
        if (string.IsNullOrWhiteSpace(request.BankReference) || request.BankReference.Length > 128 ||
            string.IsNullOrWhiteSpace(request.EvidenceReference) || request.EvidenceReference.Length > 200 ||
            request.TransferredAt.Kind != DateTimeKind.Utc || request.TransferredAt > DateTime.UtcNow)
            throw new DomainException("REFUND_EVIDENCE_REQUIRED", "A bank reference, private evidence reference and valid UTC transfer time are required.", 400);
        return await store.ConfirmRefundAsync(adminId, refundId, request, ct);
    }
    public async Task<ManualRefundPage> ListRefundsAsync(Guid adminId, int page, int pageSize, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(adminId, "Admin", ct);
        if (page < 1 || page > 100000 || pageSize < 1 || pageSize > 100)
            throw new DomainException("PAGINATION_INVALID", "Invalid pagination.", 400);
        return await store.ListRefundsAsync(page, pageSize, ct);
    }
    public async Task<SettlementPage> ListAsync(Guid adminId, int page, int pageSize, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(adminId, "Admin", ct);
        if (page < 1 || pageSize < 1 || pageSize > 100 || page > 100000)
            throw new DomainException("PAGINATION_INVALID", "Use page >= 1 and pageSize between 1 and 100.", 400);
        return await store.ListAsync(page, pageSize, ct);
    }

    public async Task<ManualPtSettlement> ApproveAsync(Guid adminId, Guid appointmentId, ApproveSettlementRequest request, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(adminId, "Admin", ct);
        await stepUp.EnsureAsync(adminId, ct);
        return await store.UpdateAsync(appointmentId, adminId, "finance.settlement_approved", context =>
        {
            var amount = EligibleAmount(context);
            context.Settlement.Approve(adminId, amount, request.CustomerConfirmedAt, request.TrainerConfirmedAt,
                context.Appointment.SlotStartUtc.AddMinutes(context.Appointment.DurationMinutes),
                request.SessionEvidenceReference, request.PayeeEvidenceReference, request.NoOpenDisputeConfirmed, DateTime.UtcNow);
        }, ct);
    }

    public async Task<ManualPtSettlement> HoldAsync(Guid adminId, Guid appointmentId, string reason, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(adminId, "Admin", ct);
        await stepUp.EnsureAsync(adminId, ct);
        return await store.UpdateAsync(appointmentId, adminId, "finance.settlement_held", context => context.Settlement.Hold(reason), ct);
    }

    public async Task<ManualPtSettlement> RecordTransferAsync(Guid adminId, Guid appointmentId, RecordTransferRequest request, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(adminId, "Admin", ct);
        await stepUp.EnsureAsync(adminId, ct);
        return await store.UpdateAsync(appointmentId, adminId, "finance.trainer_settled", context =>
        {
            // A duplicate submission after a later refund must still return the immutable receipt.
            var amount = context.Settlement.Status == "paid" ? context.Settlement.AmountVnd : EligibleAmount(context);
            context.Settlement.RecordTransfer(adminId, amount, request.BankReference, request.EvidenceReference, request.TransferredAt, DateTime.UtcNow);
        }, ct);
    }

    public static long EligibleAmount(SettlementContext context)
    {
        if (context.Appointment.Status != BookingStatus.Completed ||
            context.Appointment.SlotStartUtc.AddMinutes(context.Appointment.DurationMinutes) > DateTime.UtcNow ||
            context.Payment.Status is not (PaymentStatus.Paid or PaymentStatus.PartialRefunded) || context.HasPendingRefund || context.HasOpenDispute ||
            context.Appointment.PaymentId != context.Payment.Id || context.Payment.AmountVnd <= 0 ||
            context.Appointment.ActualPaidVnd != context.Payment.AmountVnd ||
            context.Appointment.PlatformFeeVnd + context.Appointment.TrainerShareVnd != context.Payment.AmountVnd)
            throw new ConflictException("SETTLEMENT_NOT_ELIGIBLE", "A completed, paid session without pending refunds is required.");
        // Cumulative fee refund uses the same rounding as the original fee snapshot.
        var refundedFee = (long)Math.Round(context.Appointment.PlatformFeeVnd * (decimal)context.Payment.RefundedSoFarVnd /
            context.Payment.AmountVnd, MidpointRounding.AwayFromZero);
        var amount = checked(context.Appointment.TrainerShareVnd - (context.Payment.RefundedSoFarVnd - refundedFee));
        if (amount <= 0) throw new ConflictException("SETTLEMENT_NOT_ELIGIBLE", "No positive trainer balance remains.");
        return amount;
    }
}
