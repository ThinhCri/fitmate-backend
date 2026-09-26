using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using System.Text.Json.Serialization;

namespace Fitmate.Application.Payments;

public sealed record PaymentView(
    Guid Id,
    [property: JsonPropertyName("appointment_id")] Guid AppointmentId,
    PaymentMethod Method,
    [property: JsonConverter(typeof(JsonStringEnumConverter<PaymentStatus>))] PaymentStatus Status,
    [property: JsonPropertyName("amount_vnd")] long AmountVnd,
    [property: JsonPropertyName("refunded_so_far_vnd")] long RefundedSoFarVnd,
    [property: JsonPropertyName("redirect_url")] string? RedirectUrl,
    [property: JsonPropertyName("provider_txn_ref")] string? ProviderTxnRef,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("completed_at")] DateTime? CompletedAt);

public sealed record CreatePaymentRequest
{
    [JsonPropertyName("hold_id")]
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
