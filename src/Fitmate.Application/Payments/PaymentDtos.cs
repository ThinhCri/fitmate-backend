using Fitmate.Domain.Common;
using Fitmate.Domain.Payments;

namespace Fitmate.Application.Payments;

// ─── Query ─────────────────────────────────────────────────────────────

public sealed record PaymentView(
    Guid Id,
    Guid AppointmentId,
    PaymentMethod Method,
    PaymentStatus Status,
    long AmountVnd,
    string? RedirectUrl,
    string? ProviderTxnRef,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public sealed record RefundView(
    Guid Id,
    Guid PaymentId,
    Guid AppointmentId,
    long AmountVnd,
    string Reason,
    RefundStatus Status);

// ─── Command ──────────────────────────────────────────────────────────

public sealed record CreatePaymentRequest
{
    public required Guid HoldId { get; init; }
    public required PaymentMethod Method { get; init; }
    public long? DiscountVnd { get; init; }
}

public sealed record ProviderCreatePaymentRequest(
    Guid PaymentId,
    long AmountVnd,
    string Description);

public sealed record ProviderCreatePaymentResult(
    string ProviderTxnRef,
    string RedirectUrl);

public sealed record VnpayReturnQuery
{
    public string? VnpSecureHash { get; init; }
    public string? VnpTxnRef { get; init; }
    public string? VnpResponseCode { get; init; }
}
