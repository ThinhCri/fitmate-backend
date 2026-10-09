using Fitmate.Application.Outbox;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fitmate.Application.Payments;

public class PaymentService : IPaymentService
{
    private readonly IPaymentStore _store;
    private readonly IPaymentProvider _provider;
    private readonly IOptions<VnpayOptions> _vnpayOptions;
    private readonly IVnpaySecurity? _vnpaySecurity;
    private readonly IOutboxDispatcher? _outbox;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IPaymentStore store,
        IPaymentProvider provider)
        : this(store, provider, Microsoft.Extensions.Options.Options.Create(new VnpayOptions()), null, null, NullLogger<PaymentService>.Instance)
    {
    }

    public PaymentService(
        IPaymentStore store,
        IPaymentProvider provider,
        IOptions<VnpayOptions>? vnpayOptions,
        IVnpaySecurity? vnpaySecurity = null,
        IOutboxDispatcher? outbox = null,
        ILogger<PaymentService>? logger = null)
    {
        _store = store;
        _provider = provider;
        _vnpayOptions = vnpayOptions ?? Microsoft.Extensions.Options.Options.Create(new VnpayOptions());
        _vnpaySecurity = vnpaySecurity;
        _outbox = outbox;
        _logger = logger ?? NullLogger<PaymentService>.Instance;
    }

    public async Task<PaymentView> CreateAsync(
        Guid memberId, CreatePaymentRequest request, CancellationToken ct)
    {
        if (memberId == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");

        if (_provider is MissingPaymentProvider)
            throw new DomainException(ErrorCodes.Payment.ProviderUnavailable, "Payment provider is not configured.", 503);

        if (request.Method != _provider.Method)
            throw new DomainException("PAYMENT_METHOD_UNAVAILABLE", "The requested payment method is not enabled.", 503);

        // 1. Validate hold + get appointment.
        var hold = await _store.FindHoldAsync(request.HoldId, ct)
            ?? throw new NotFoundException(PaymentErrorCodes.HoldNotFound, $"Hold '{request.HoldId}' not found.");

        if (hold.MemberId != memberId)
            throw new ForbiddenException(PaymentErrorCodes.NotOwner, "You do not own this hold.");

        if (!hold.IsActive)
            throw new ConflictException(PaymentErrorCodes.HoldExpired, $"Hold '{request.HoldId}' is no longer active.");

        // A package reserves multiple holds under one future payment. Paying one of
        // them through the single-booking route would corrupt refunds and payouts.
        if (await _store.IsPackageHoldAsync(hold.Id, ct))
            throw new ConflictException("PACKAGE_CHECKOUT_REQUIRED",
                "Buổi này thuộc một gói PT. Không thể thanh toán riêng từng buổi.");

        var appt = await _store.FindAppointmentByHoldAsync(request.HoldId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment not found for hold '{request.HoldId}'.");

        if (appt.PriceListedVnd <= 0)
            throw new ValidationException(PaymentErrorCodes.InvalidAmount, "Payment amount must be greater than zero.",
                new Dictionary<string, string[]> { ["amount"] = new[] { "must be positive" } });

        // Serialize reservation per appointment. Never hold this lock during HTTP.
        var payment = await _store.ExecuteInTransactionAsync(async () =>
        {
            await _store.FindAppointmentForUpdateAsync(appt.Id, ct);
            var existing = await _store.FindByAppointmentIdAsync(appt.Id, ct);
            if (existing != null)
            {
                if (existing.Method != request.Method)
                    throw new ConflictException("PAYMENT_PROVIDER_CONFLICT", "This booking already has a payment with another provider.");
                return existing;
            }
            var pending = Payment.CreatePending(appt.Id, _provider.Method, appt.PriceListedVnd, null);
            _store.Add(pending);
            if (_provider is IPaymentReservation reservation)
                reservation.Reserve(pending.Id, hold.ExpiresAt);
            await _store.SaveChangesAsync(ct);
            return pending;
        }, ct);
        if (payment.RedirectUrl != null || payment.Status != PaymentStatus.Pending)
            return ToView(payment);

        var result = await _provider.CreatePaymentAsync(
            new ProviderCreatePaymentRequest(payment.Id, payment.AmountVnd, "FITMATE"), ct);
        payment.SetCheckoutUrl(result.RedirectUrl);
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
    {
        if (string.IsNullOrWhiteSpace(query.VnpSecureHash))
            return new { isSuccess = false, status = "unverified", code = "97", message = "Missing or invalid signature." };

        return new { isSuccess = query.VnpResponseCode == "00", status = query.VnpResponseCode == "00" ? "paid" : "failed", message = "Callback processed." };
    }

    public object HandleVnpayIpn(VnpayReturnQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.VnpSecureHash))
            return new { RspCode = "97", Message = "Invalid Checksum" };

        return new { RspCode = "00", Message = "Confirm Success" };
    }

    public async Task<VnpayIpnResponse> HandleVnpayIpnAsync(
        IDictionary<string, string> queryParams,
        CancellationToken ct)
    {
        var options = _vnpayOptions.Value;

        // 1. Verify Checksum
        if (_vnpaySecurity == null || string.IsNullOrWhiteSpace(options.HashSecret) || !_vnpaySecurity.VerifyIpnSignature(queryParams, options.HashSecret))
        {
            _logger.LogWarning("VNPay IPN signature verification failed");
            return new VnpayIpnResponse("97", "Invalid Checksum");
        }

        // 2. Verify Merchant Code
        queryParams.TryGetValue("vnp_TmnCode", out var tmnCode);
        if (!string.Equals(tmnCode, options.TmnCode, StringComparison.Ordinal))
        {
            _logger.LogWarning("VNPay IPN TmnCode mismatch: incoming '{Incoming}', expected '{Expected}'", tmnCode, options.TmnCode);
            return new VnpayIpnResponse("97", "Invalid Merchant");
        }

        // 3. Verify Amount Format & Overflow
        if (!queryParams.TryGetValue("vnp_Amount", out var rawAmount) ||
            !long.TryParse(rawAmount, out var scaledAmount) ||
            scaledAmount <= 0)
        {
            _logger.LogWarning("VNPay IPN invalid amount format: '{RawAmount}'", rawAmount);
            return new VnpayIpnResponse("04", "Invalid Amount");
        }

        long amountVnd;
        try
        {
            amountVnd = checked(scaledAmount / 100);
        }
        catch (OverflowException)
        {
            _logger.LogWarning("VNPay IPN amount overflow detected: '{RawAmount}'", rawAmount);
            return new VnpayIpnResponse("04", "Invalid Amount");
        }

        // 4. Verify Order Identity
        queryParams.TryGetValue("vnp_TxnRef", out var rawTxnRef);
        if (string.IsNullOrWhiteSpace(rawTxnRef))
        {
            return new VnpayIpnResponse("01", "Order Not Found");
        }

        Guid paymentId;
        if (!Guid.TryParse(rawTxnRef, out paymentId))
        {
            // Fallback for hex-string format (32 characters without hyphens)
            if (rawTxnRef.Length == 32 && Guid.TryParseExact(rawTxnRef, "N", out var parsedGuid))
            {
                paymentId = parsedGuid;
            }
            else
            {
                return new VnpayIpnResponse("01", "Order Not Found");
            }
        }

        queryParams.TryGetValue("vnp_ResponseCode", out var responseCode);
        queryParams.TryGetValue("vnp_TransactionNo", out var transactionNo);

        // 5. Atomic Settlement within Database Transaction
        return await _store.ExecuteInTransactionAsync(async () =>
        {
            // Lock hierarchy: Hold -> Appointment -> Payment
            var payment = await _store.FindByIdForUpdateAsync(paymentId, ct);
            if (payment == null)
            {
                _logger.LogWarning("VNPay IPN payment record not found: {PaymentId}", paymentId);
                return new VnpayIpnResponse("01", "Order Not Found");
            }
            if (payment.Method != PaymentMethod.Vnpay)
                return new VnpayIpnResponse("01", "Order Not Found");

            if (payment.AmountVnd != amountVnd)
            {
                _logger.LogWarning("VNPay IPN amount mismatch for {PaymentId}: expected {Expected}, got {Actual}",
                    paymentId, payment.AmountVnd, amountVnd);
                return new VnpayIpnResponse("04", "Invalid Amount");
            }

            // Idempotency: Return duplicate ACK only after full verification
            if (payment.Status == PaymentStatus.Paid ||
                payment.Status == PaymentStatus.Refunded ||
                payment.Status == PaymentStatus.PartialRefunded)
            {
                _logger.LogInformation("VNPay IPN duplicate callback received for {PaymentId} in status {Status}",
                    paymentId, payment.Status);
                return new VnpayIpnResponse("02", "Order Already Confirmed");
            }

            // Out-of-order check: Never downgrade Paid or Refunded.
            if (responseCode != "00")
            {
                _logger.LogWarning("VNPay IPN reported payment failure: ResponseCode={ResponseCode} for PaymentId={PaymentId}",
                    responseCode, paymentId);

                if (payment.Status == PaymentStatus.Pending)
                {
                    payment.MarkFailed();
                    await _store.SaveChangesAsync(ct);
                }

                return new VnpayIpnResponse("00", "Confirm Success");
            }

            // Payment succeeded at provider side (ResponseCode == "00")
            // Check hold status to detect late settlement race
            var appt = await _store.FindAppointmentForUpdateAsync(payment.AppointmentId, ct);
            var hold = appt != null ? await _store.FindHoldForUpdateAsync(appt.Id, ct) : null;

            var isHoldValid = hold != null && hold.IsActive;

            if (isHoldValid)
            {
                // Normal settlement: hold is still active
                payment.MarkPaid(transactionNo);

                var platformFeeRate = 0.10m;
                var platformFeeVnd = (long)Math.Round(payment.AmountVnd * platformFeeRate, MidpointRounding.AwayFromZero);
                var trainerShareVnd = payment.AmountVnd - platformFeeVnd;

                appt!.AttachPayment(
                    paymentId: payment.Id,
                    actualPaidVnd: payment.AmountVnd,
                    discountVnd: 0,
                    platformFeeRate: platformFeeRate,
                    platformFeeVnd: platformFeeVnd,
                    trainerShareVnd: trainerShareVnd,
                    policyVersion: "v1");
                appt.ConfirmByPt();

                hold!.Consume();

                _outbox?.Enqueue("payment.paid", new
                {
                    paymentId = payment.Id,
                    appointmentId = appt.Id,
                    amountVnd = payment.AmountVnd,
                    providerTxnNo = transactionNo
                });
                _outbox?.Enqueue("booking.confirmed", new
                {
                    appointmentId = appt.Id,
                    ptUserId = appt.PtUserId,
                    memberId = appt.MemberId,
                    confirmationSource = "verified_payment"
                });

                _logger.LogInformation("VNPay IPN payment successfully settled for PaymentId: {PaymentId}", paymentId);
            }
            else
            {
                // Late settlement: Hold expired or slot was reallocated/released!
                // Do NOT revive booking or reclaim slot. Record Payment as Paid, create Refund in Pending status.
                payment.MarkPaid(transactionNo);

                var refundReason = "Hold expired or slot released before payment settlement";
                var refund = Refund.CreateSystemRefund(payment.Id, payment.AppointmentId, payment.AmountVnd, refundReason);
                _store.AddRefund(refund);

                _outbox?.Enqueue("payment.settlement_discrepancy", new
                {
                    paymentId = payment.Id,
                    appointmentId = payment.AppointmentId,
                    amountVnd = payment.AmountVnd,
                    refundId = refund.Id,
                    reason = refundReason
                });

                _logger.LogWarning("VNPay IPN late payment received after hold expired for PaymentId: {PaymentId}. Created pending refund {RefundId}",
                    paymentId, refund.Id);
            }

            await _store.SaveChangesAsync(ct);
            return new VnpayIpnResponse("00", "Confirm Success");
        }, ct);
    }

    public async Task<ProviderQueryResult> QueryTransactionAsync(Guid paymentId, CancellationToken ct)
    {
        var payment = await _store.FindByIdAsync(paymentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Payment '{paymentId}' not found.");

        if (payment.Method != _provider.Method)
            throw new DomainException("PAYMENT_METHOD_UNAVAILABLE", "Reconcile using the original payment provider.", 503);

        return await _provider.QueryTransactionAsync(
            new ProviderQueryRequest(payment.Id, payment.CreatedAt), ct);
    }

    public async Task<ProviderRefundResult> RefundAsync(
        Guid paymentId,
        long amountVnd,
        string reason,
        Guid adminId,
        CancellationToken ct)
    {
        if (amountVnd <= 0)
            throw new ValidationException(PaymentErrorCodes.InvalidAmount, "Refund amount must be positive.",
                new Dictionary<string, string[]> { ["amount"] = new[] { "must be positive" } });

        Refund refund;
        Payment payment;

        // Step 1: Atomic check & persist Refund intent in Pending status.
        // DO NOT hold DB connection during external HTTP calls!
        (refund, payment) = await _store.ExecuteInTransactionAsync(async () =>
        {
            var p = await _store.FindByIdForUpdateAsync(paymentId, ct)
                ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Payment '{paymentId}' not found.");
            if (p.Method != _provider.Method)
                throw new DomainException("PAYMENT_METHOD_UNAVAILABLE", "Refund using the original payment provider.", 503);

            if (p.Status is not (PaymentStatus.Paid or PaymentStatus.PartialRefunded))
                throw new ConflictException(ErrorCodes.Payment.AlreadyProcessed, $"Cannot refund payment in status {p.Status}.");

            var pendingTotal = await _store.GetPendingRefundsTotalAsync(paymentId, ct);
            if (p.RefundedSoFarVnd + pendingTotal + amountVnd > p.AmountVnd)
            {
                throw new ConflictException(
                    ErrorCodes.Payment.RefundCapExceeded,
                    $"Total refund including pending ({p.RefundedSoFarVnd + pendingTotal + amountVnd}) exceeds paid amount ({p.AmountVnd}).");
            }

            var r = Refund.Create(paymentId, p.AppointmentId, amountVnd, reason, adminId);
            _store.AddRefund(r);
            await _store.SaveChangesAsync(ct);
            return (r, p);
        }, ct);

        if (payment.Method == PaymentMethod.Payos)
            return new ProviderRefundResult(false, "MANUAL_REFUND_REQUIRED", null,
                "Refund intent recorded. An administrator must transfer and reconcile the refund manually.");

        // Step 2: External network call to provider outside DB transaction
        try
        {
            var providerResult = await _provider.RefundAsync(
                new ProviderRefundRequest(
                    RefundId: refund.Id,
                    PaymentId: payment.Id,
                    AmountVnd: amountVnd,
                    ProviderTxnNo: payment.ProviderTxnRef,
                    TransactionDate: payment.CompletedAt ?? DateTime.UtcNow,
                    Reason: reason),
                ct);

            if (providerResult.IsSuccess)
            {
                refund.MarkSucceeded(providerResult.ProviderRefundRef);
                payment.ApplyRefund(amountVnd);
            }
            else
            {
                refund.MarkFailed();
            }

            await _store.SaveChangesAsync(ct);
            return providerResult;
        }
        catch (Exception ex)
        {
            // On timeout or network exception: leave refund in Pending status for reconciliation!
            _logger.LogError(ex, "Timeout or network failure during refund for PaymentId: {PaymentId}, RefundId: {RefundId}",
                paymentId, refund.Id);

            return new ProviderRefundResult(
                IsSuccess: false,
                ResponseCode: "99",
                ProviderRefundRef: null,
                Message: "Refund request pending reconciliation due to network timeout");
        }
    }

    private static PaymentView ToView(Payment p) => new(
        p.Id, p.AppointmentId, p.Method, p.Status,
        p.AmountVnd, p.RefundedSoFarVnd,
        p.RedirectUrl, p.ProviderTxnRef,
        p.CreatedAt, p.CompletedAt);
}
