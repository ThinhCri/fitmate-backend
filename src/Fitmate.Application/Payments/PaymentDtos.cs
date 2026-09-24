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

public sealed record ProviderCreatePaymentRequest(
    Guid PaymentId,
    long AmountVnd,
    string Description,
    string? ClientIp = null);

public sealed record ProviderCreatePaymentResult(string ProviderTxnRef, string RedirectUrl);

public sealed record ProviderQueryRequest(
    Guid PaymentId,
    DateTime TransactionDate,
    string? ClientIp = null);

public sealed record ProviderQueryResult(
    bool IsSuccess,
    string ResponseCode,
    string TransactionStatus,
    string? ProviderTxnNo,
    string Message);

public sealed record ProviderRefundRequest(
    Guid RefundId,
    Guid PaymentId,
    long AmountVnd,
    string? ProviderTxnNo,
    DateTime TransactionDate,
    string Reason,
    string? ClientIp = null);

public sealed record ProviderRefundResult(
    bool IsSuccess,
    string ResponseCode,
    string? ProviderRefundRef,
    string Message);

public sealed record VnpayReturnQuery(
    string? VnpSecureHash,
    string? VnpTxnRef,
    string? VnpResponseCode);

public sealed record VnpayIpnResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("RspCode")] string RspCode,
    [property: System.Text.Json.Serialization.JsonPropertyName("Message")] string Message);
