using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;

namespace Fitmate.Application.Payments;

public sealed record PaymentView(
    Guid Id,
    Guid AppointmentId,
    PaymentMethod Method,
    PaymentStatus Status,
    long AmountVnd,
    long RefundedSoFarVnd,
    string? RedirectUrl,
    string? ProviderTxnRef,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public sealed record CreatePaymentRequest
{
    public required Guid HoldId { get; init; }
    public required PaymentMethod Method { get; init; }
}

public sealed record ProviderCreatePaymentRequest(Guid PaymentId, long AmountVnd, string Description);

public sealed record ProviderCreatePaymentResult(string ProviderTxnRef, string RedirectUrl);

public sealed record VnpayReturnQuery(
    string? VnpSecureHash,
    string? VnpTxnRef,
    string? VnpResponseCode);
