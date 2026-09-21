using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;

namespace Fitmate.Application.Payments;

public class PaymentService : IPaymentService
{
    private readonly IPaymentStore _store;
    private readonly IPaymentProvider _provider;

    public PaymentService(IPaymentStore store, IPaymentProvider provider)
    {
        _store = store;
        _provider = provider;
    }

    public async Task<PaymentView> CreateAsync(
        Guid memberId, CreatePaymentRequest request, CancellationToken ct)
    {
        if (memberId == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");

        // 1. Validate hold + get appointment.
        var hold = await _store.FindHoldAsync(request.HoldId, ct)
            ?? throw new NotFoundException(PaymentErrorCodes.HoldNotFound, $"Hold '{request.HoldId}' not found.");

        if (hold.MemberId != memberId)
            throw new ForbiddenException(PaymentErrorCodes.NotOwner, "You do not own this hold.");

        if (!hold.IsActive)
            throw new ConflictException(PaymentErrorCodes.HoldExpired, $"Hold '{request.HoldId}' is no longer active.");

        var appt = await _store.FindAppointmentByHoldAsync(request.HoldId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment not found for hold '{request.HoldId}'.");

        // 2. Idempotency: nếu đã có Payment cho appointment → trả row cũ.
        var existing = await _store.FindByAppointmentIdAsync(appt.Id, ct);
        if (existing is not null)
            return ToView(existing);

        // 3. Create payment với stub provider.
        var result = await _provider.CreatePaymentAsync(
            new ProviderCreatePaymentRequest(appt.Id, appt.PriceListedVnd, "Fitmate booking"), ct);

        var payment = Payment.CreatePending(
            appointmentId: appt.Id,
            method: request.Method,
            amountVnd: appt.PriceListedVnd,
            redirectUrl: result.RedirectUrl);

        payment.MarkPaid(result.ProviderTxnRef);
        _store.Add(payment);
        await _store.SaveChangesAsync(ct);

        return ToView(payment);
    }

    public async Task<PaymentView?> GetByIdAsync(
        Guid memberId, Guid paymentId, CancellationToken ct)
    {
        if (memberId == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");

        var payment = await _store.FindByIdAsync(paymentId, ct);
        if (payment is null) return null;

        var appt = await _store.FindAppointmentByHoldAsync(payment.AppointmentId, ct);
        if (appt is null || appt.MemberId != memberId) return null;

        return ToView(payment);
    }

    public object HandleVnpayReturn(VnpayReturnQuery query)
        => new { status = "pending", message = "Real VNPay integration pending UA-008." };

    public object HandleVnpayIpn(VnpayReturnQuery query)
        => new { code = "00", message = "Stub received" };

    private static PaymentView ToView(Payment p) => new(
        p.Id, p.AppointmentId, p.Method, p.Status,
        p.AmountVnd, p.RefundedSoFarVnd,
        p.RedirectUrl, p.ProviderTxnRef,
        p.CreatedAt, p.CompletedAt);
}
