namespace Fitmate.Application.Payments;

public interface IVnpaySecurity
{
    string BuildCheckoutUrl(
        VnpayOptions options,
        string txnRef,
        long amountVnd,
        string orderInfo,
        string? clientIp,
        DateTime createDate,
        DateTime expireDate);

    bool VerifyIpnSignature(
        IDictionary<string, string> queryParams,
        string hashSecret);

    string BuildQueryDrHash(
        string requestId,
        string version,
        string command,
        string tmnCode,
        string txnRef,
        string transDate,
        string createDate,
        string ipAddr,
        string orderInfo,
        string hashSecret);

    bool VerifyQueryDrResponseHash(
        string responseId,
        string command,
        string responseCode,
        string message,
        string tmnCode,
        string txnRef,
        string amount,
        string bankCode,
        string payDate,
        string transactionNo,
        string transactionType,
        string transactionStatus,
        string orderInfo,
        string? promotionCode,
        string? promotionAmount,
        string hashSecret,
        string secureHash);

    string BuildRefundHash(
        string requestId,
        string version,
        string command,
        string tmnCode,
        string transactionType,
        string txnRef,
        string amount,
        string transactionNo,
        string transactionDate,
        string createBy,
        string createDate,
        string ipAddr,
        string orderInfo,
        string hashSecret);

    bool VerifyRefundResponseHash(
        string responseId,
        string command,
        string responseCode,
        string message,
        string tmnCode,
        string txnRef,
        string amount,
        string bankCode,
        string payDate,
        string transactionNo,
        string transactionType,
        string transactionStatus,
        string orderInfo,
        string hashSecret,
        string secureHash);

    bool SecureCompare(string? a, string? b);
    string HmacSha512(string rawData, string secretKey);
}

