using System.Net;
using System.Security.Cryptography;
using System.Text;
using Fitmate.Application.Payments;

namespace Fitmate.Infrastructure.Payments.Vnpay;

public class VnpaySecurity : IVnpaySecurity
{
    public string BuildCheckoutUrl(
        VnpayOptions options,
        string txnRef,
        long amountVnd,
        string orderInfo,
        string? clientIp,
        DateTime createDate,
        DateTime expireDate)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.TmnCode)) throw new ArgumentException("TmnCode is required", nameof(options));
        if (string.IsNullOrWhiteSpace(options.HashSecret)) throw new ArgumentException("HashSecret is required", nameof(options));
        if (amountVnd <= 0) throw new ArgumentOutOfRangeException(nameof(amountVnd), "Amount must be positive");

        // Scaling VNPay amount: VNPay requires amount * 100 with checked overflow
        long scaledAmount = checked(amountVnd * 100);

        var data = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Version"] = "2.1.0",
            ["vnp_Command"] = "pay",
            ["vnp_TmnCode"] = options.TmnCode,
            ["vnp_Amount"] = scaledAmount.ToString(),
            ["vnp_CreateDate"] = createDate.ToString("yyyyMMddHHmmss"),
            ["vnp_CurrCode"] = "VND",
            ["vnp_IpAddr"] = string.IsNullOrWhiteSpace(clientIp) ? "127.0.0.1" : clientIp,
            ["vnp_Locale"] = "vn",
            ["vnp_OrderInfo"] = orderInfo,
            ["vnp_OrderType"] = "other",
            ["vnp_ReturnUrl"] = options.ReturnUrl,
            ["vnp_TxnRef"] = txnRef,
            ["vnp_ExpireDate"] = expireDate.ToString("yyyyMMddHHmmss")
        };

        var queryBuilder = new StringBuilder();
        var hashDataBuilder = new StringBuilder();

        foreach (var (key, value) in data)
        {
            if (string.IsNullOrEmpty(value)) continue;

            var encodedValue = WebUtility.UrlEncode(value);
            if (hashDataBuilder.Length > 0)
            {
                hashDataBuilder.Append('&');
                queryBuilder.Append('&');
            }

            hashDataBuilder.Append(key);
            hashDataBuilder.Append('=');
            hashDataBuilder.Append(encodedValue);

            queryBuilder.Append(WebUtility.UrlEncode(key));
            queryBuilder.Append('=');
            queryBuilder.Append(encodedValue);
        }

        var secureHash = HmacSha512(hashDataBuilder.ToString(), options.HashSecret);
        var baseUrl = options.PaymentUrl.TrimEnd('?', '&');
        return $"{baseUrl}?{queryBuilder}&vnp_SecureHash={secureHash}";
    }

    public bool VerifyIpnSignature(
        IDictionary<string, string> queryParams,
        string hashSecret)
    {
        if (queryParams == null || string.IsNullOrWhiteSpace(hashSecret)) return false;

        string? incomingHash = null;
        var data = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in queryParams)
        {
            if (string.Equals(key, "vnp_SecureHash", StringComparison.OrdinalIgnoreCase))
            {
                incomingHash = value;
                continue;
            }

            if (string.Equals(key, "vnp_SecureHashType", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(value) && key.StartsWith("vnp_", StringComparison.OrdinalIgnoreCase))
            {
                data[key] = value;
            }
        }

        if (string.IsNullOrWhiteSpace(incomingHash)) return false;

        var hashDataBuilder = new StringBuilder();
        foreach (var (key, value) in data)
        {
            if (hashDataBuilder.Length > 0) hashDataBuilder.Append('&');
            hashDataBuilder.Append(key);
            hashDataBuilder.Append('=');
            hashDataBuilder.Append(WebUtility.UrlEncode(value));
        }

        var calculatedHash = HmacSha512(hashDataBuilder.ToString(), hashSecret);
        return SecureCompare(calculatedHash, incomingHash);
    }

    public string BuildQueryDrHash(
        string requestId,
        string version,
        string command,
        string tmnCode,
        string txnRef,
        string transDate,
        string createDate,
        string ipAddr,
        string orderInfo,
        string hashSecret)
    {
        // VNPay QueryDR raw data format:
        // vnp_RequestId|vnp_Version|vnp_Command|vnp_TmnCode|vnp_TxnRef|vnp_TransDate|vnp_CreateDate|vnp_IpAddr|vnp_OrderInfo
        var rawData = $"{requestId}|{version}|{command}|{tmnCode}|{txnRef}|{transDate}|{createDate}|{ipAddr}|{orderInfo}";
        return HmacSha512(rawData, hashSecret);
    }

    public bool VerifyQueryDrResponseHash(
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
        string secureHash)
    {
        if (string.IsNullOrWhiteSpace(secureHash)) return false;

        // Base response format:
        // vnp_ResponseId|vnp_Command|vnp_ResponseCode|vnp_Message|vnp_TmnCode|vnp_TxnRef|vnp_Amount|vnp_BankCode|vnp_PayDate|vnp_TransactionNo|vnp_TransactionType|vnp_TransactionStatus|vnp_OrderInfo
        var baseData = $"{responseId}|{command}|{responseCode}|{message}|{tmnCode}|{txnRef}|{amount}|{bankCode}|{payDate}|{transactionNo}|{transactionType}|{transactionStatus}|{orderInfo}";

        if (!string.IsNullOrEmpty(promotionCode) || !string.IsNullOrEmpty(promotionAmount))
        {
            var withPromotion = $"{baseData}|{promotionCode ?? ""}|{promotionAmount ?? ""}";
            if (SecureCompare(HmacSha512(withPromotion, hashSecret), secureHash))
                return true;
        }

        var calculated = HmacSha512(baseData, hashSecret);
        return SecureCompare(calculated, secureHash);
    }

    public string BuildRefundHash(
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
        string hashSecret)
    {
        // VNPay Refund raw data format:
        // vnp_RequestId|vnp_Version|vnp_Command|vnp_TmnCode|vnp_TransactionType|vnp_TxnRef|vnp_Amount|vnp_TransactionNo|vnp_TransactionDate|vnp_CreateBy|vnp_CreateDate|vnp_IpAddr|vnp_OrderInfo
        var rawData = $"{requestId}|{version}|{command}|{tmnCode}|{transactionType}|{txnRef}|{amount}|{transactionNo}|{transactionDate}|{createBy}|{createDate}|{ipAddr}|{orderInfo}";
        return HmacSha512(rawData, hashSecret);
    }

    public bool VerifyRefundResponseHash(
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
        string secureHash)
    {
        if (string.IsNullOrWhiteSpace(secureHash)) return false;

        var rawData = $"{responseId}|{command}|{responseCode}|{message}|{tmnCode}|{txnRef}|{amount}|{bankCode}|{payDate}|{transactionNo}|{transactionType}|{transactionStatus}|{orderInfo}";
        var calculated = HmacSha512(rawData, hashSecret);
        return SecureCompare(calculated, secureHash);
    }

    public bool SecureCompare(string? a, string? b)
    {
        if (a == null || b == null) return false;
        var bytesA = Encoding.UTF8.GetBytes(a.Trim().ToUpperInvariant());
        var bytesB = Encoding.UTF8.GetBytes(b.Trim().ToUpperInvariant());
        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }

    public string HmacSha512(string rawData, string secretKey)
    {
        ArgumentNullException.ThrowIfNull(rawData);
        ArgumentNullException.ThrowIfNull(secretKey);

        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        var inputBytes = Encoding.UTF8.GetBytes(rawData);

        using var hmac = new HMACSHA512(keyBytes);
        var hashBytes = hmac.ComputeHash(inputBytes);
        return Convert.ToHexString(hashBytes);
    }
}
