using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using System.Text.Json.Serialization;

namespace Fitmate.Application.Payments;

public sealed record PaymentView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("appointment_id")] Guid AppointmentId,
    [property: JsonPropertyName("method")] PaymentMethod Method,
    [property: JsonPropertyName("status")]
    [property: JsonConverter(typeof(JsonStringEnumConverter<PaymentStatus>))] PaymentStatus Status,
    [property: JsonPropertyName("amount_vnd")] long AmountVnd,
    [property: JsonPropertyName("refunded_so_far_vnd")] long RefundedSoFarVnd,
    [property: JsonPropertyName("redirect_url")] string? RedirectUrl,
    [property: JsonPropertyName("provider_txn_ref")] string? ProviderTxnRef,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("completed_at")] DateTime? CompletedAt,
    [property: JsonPropertyName("qr_code")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? QrCode = null,
    [property: JsonPropertyName("account_number")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AccountNumber = null,
    [property: JsonPropertyName("account_name")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AccountName = null,
    [property: JsonPropertyName("bin")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Bin = null,
    [property: JsonPropertyName("description")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description = null,
    [property: JsonPropertyName("order_code")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? OrderCode = null);

public sealed record CreatePaymentRequest
{
    [JsonPropertyName("hold_id")]
    public required Guid HoldId { get; init; }

    [JsonPropertyName("method")]
    public required PaymentMethod Method { get; init; }
}

public sealed record ProviderCreatePaymentRequest(
    Guid PaymentId,
    long AmountVnd,
    string Description,
    string? ClientIp = null);

public sealed record ProviderCreatePaymentResult(
    string ProviderTxnRef,
    string RedirectUrl,
    string? QrCode = null,
    string? AccountNumber = null,
    string? AccountName = null,
    string? Bin = null,
    string? Description = null,
    long? OrderCode = null);

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
