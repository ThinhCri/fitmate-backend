using System.Text.Json.Serialization;

namespace Fitmate.Infrastructure.Payments.Vnpay;

public sealed record VnpayQueryDrApiRequest(
    [property: JsonPropertyName("vnp_RequestId")] string RequestId,
    [property: JsonPropertyName("vnp_Version")] string Version,
    [property: JsonPropertyName("vnp_Command")] string Command,
    [property: JsonPropertyName("vnp_TmnCode")] string TmnCode,
    [property: JsonPropertyName("vnp_TxnRef")] string TxnRef,
    [property: JsonPropertyName("vnp_OrderInfo")] string OrderInfo,
    [property: JsonPropertyName("vnp_TransactionDate")] string TransactionDate,
    [property: JsonPropertyName("vnp_CreateDate")] string CreateDate,
    [property: JsonPropertyName("vnp_IpAddr")] string IpAddr,
    [property: JsonPropertyName("vnp_SecureHash")] string SecureHash);

public sealed record VnpayQueryDrApiResponse(
    [property: JsonPropertyName("vnp_ResponseId")] string? ResponseId,
    [property: JsonPropertyName("vnp_Command")] string? Command,
    [property: JsonPropertyName("vnp_ResponseCode")] string? ResponseCode,
    [property: JsonPropertyName("vnp_Message")] string? Message,
    [property: JsonPropertyName("vnp_TmnCode")] string? TmnCode,
    [property: JsonPropertyName("vnp_TxnRef")] string? TxnRef,
    [property: JsonPropertyName("vnp_Amount")] string? Amount,
    [property: JsonPropertyName("vnp_BankCode")] string? BankCode,
    [property: JsonPropertyName("vnp_PayDate")] string? PayDate,
    [property: JsonPropertyName("vnp_TransactionNo")] string? TransactionNo,
    [property: JsonPropertyName("vnp_TransactionType")] string? TransactionType,
    [property: JsonPropertyName("vnp_TransactionStatus")] string? TransactionStatus,
    [property: JsonPropertyName("vnp_OrderInfo")] string? OrderInfo,
    [property: JsonPropertyName("vnp_PromotionCode")] string? PromotionCode,
    [property: JsonPropertyName("vnp_PromotionAmount")] string? PromotionAmount,
    [property: JsonPropertyName("vnp_SecureHash")] string? SecureHash);

public sealed record VnpayRefundApiRequest(
    [property: JsonPropertyName("vnp_RequestId")] string RequestId,
    [property: JsonPropertyName("vnp_Version")] string Version,
    [property: JsonPropertyName("vnp_Command")] string Command,
    [property: JsonPropertyName("vnp_TmnCode")] string TmnCode,
    [property: JsonPropertyName("vnp_TransactionType")] string TransactionType,
    [property: JsonPropertyName("vnp_TxnRef")] string TxnRef,
    [property: JsonPropertyName("vnp_Amount")] string Amount,
    [property: JsonPropertyName("vnp_TransactionNo")] string TransactionNo,
    [property: JsonPropertyName("vnp_TransactionDate")] string TransactionDate,
    [property: JsonPropertyName("vnp_CreateBy")] string CreateBy,
    [property: JsonPropertyName("vnp_CreateDate")] string CreateDate,
    [property: JsonPropertyName("vnp_IpAddr")] string IpAddr,
    [property: JsonPropertyName("vnp_OrderInfo")] string OrderInfo,
    [property: JsonPropertyName("vnp_SecureHash")] string SecureHash);

public sealed record VnpayRefundApiResponse(
    [property: JsonPropertyName("vnp_ResponseId")] string? ResponseId,
    [property: JsonPropertyName("vnp_Command")] string? Command,
    [property: JsonPropertyName("vnp_ResponseCode")] string? ResponseCode,
    [property: JsonPropertyName("vnp_Message")] string? Message,
    [property: JsonPropertyName("vnp_TmnCode")] string? TmnCode,
    [property: JsonPropertyName("vnp_TxnRef")] string? TxnRef,
    [property: JsonPropertyName("vnp_Amount")] string? Amount,
    [property: JsonPropertyName("vnp_BankCode")] string? BankCode,
    [property: JsonPropertyName("vnp_PayDate")] string? PayDate,
    [property: JsonPropertyName("vnp_TransactionNo")] string? TransactionNo,
    [property: JsonPropertyName("vnp_TransactionType")] string? TransactionType,
    [property: JsonPropertyName("vnp_TransactionStatus")] string? TransactionStatus,
    [property: JsonPropertyName("vnp_OrderInfo")] string? OrderInfo,
    [property: JsonPropertyName("vnp_SecureHash")] string? SecureHash);

public interface IVnpayClient
{
    Task<VnpayQueryDrApiResponse> QueryTransactionAsync(
        VnpayQueryDrApiRequest request,
        CancellationToken ct);

    Task<VnpayRefundApiResponse> RefundAsync(
        VnpayRefundApiRequest request,
        CancellationToken ct);
}

