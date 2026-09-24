using Fitmate.Application.Payments;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Payments.Vnpay;

public class VnpayPaymentAdapter : IPaymentProvider
{
    private readonly IOptions<VnpayOptions> _options;
    private readonly IVnpaySecurity _security;
    private readonly IVnpayClient _client;
    private readonly ILogger<VnpayPaymentAdapter> _logger;

    public VnpayPaymentAdapter(
        IOptions<VnpayOptions> options,
        IVnpaySecurity security,
        IVnpayClient client,
        ILogger<VnpayPaymentAdapter> logger)
    {
        _options = options;
        _security = security;
        _client = client;
        _logger = logger;
    }

    public PaymentMethod Method => PaymentMethod.Vnpay;

    public Task<ProviderCreatePaymentResult> CreatePaymentAsync(
        ProviderCreatePaymentRequest request,
        CancellationToken ct)
    {
        var options = _options.Value;
        if (!options.IsValid())
        {
            throw new DomainException(
                ErrorCodes.Payment.ProviderUnavailable,
                "VNPay credentials are not properly configured.",
                503);
        }

        var txnRef = request.PaymentId.ToString("N");
        var now = DateTime.UtcNow;
        // Hold TTL is 5 minutes, checkout URL valid for 5 minutes to stay in sync with hold
        var expireDate = now.AddMinutes(5);

        var redirectUrl = _security.BuildCheckoutUrl(
            options: options,
            txnRef: txnRef,
            amountVnd: request.AmountVnd,
            orderInfo: request.Description,
            clientIp: request.ClientIp,
            createDate: now,
            expireDate: expireDate);

        _logger.LogInformation("Created VNPay payment redirect URL for PaymentId: {PaymentId}", request.PaymentId);
        return Task.FromResult(new ProviderCreatePaymentResult(txnRef, redirectUrl));
    }

    public async Task<ProviderQueryResult> QueryTransactionAsync(
        ProviderQueryRequest request,
        CancellationToken ct)
    {
        var options = _options.Value;
        if (!options.IsValid())
        {
            throw new DomainException(
                ErrorCodes.Payment.ProviderUnavailable,
                "VNPay credentials are not properly configured.",
                503);
        }

        var requestId = Guid.NewGuid().ToString("N");
        var txnRef = request.PaymentId.ToString("N");
        var transDate = request.TransactionDate.ToString("yyyyMMddHHmmss");
        var createDate = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var ipAddr = string.IsNullOrWhiteSpace(request.ClientIp) ? "127.0.0.1" : request.ClientIp;
        var orderInfo = $"Query transaction {txnRef}";

        var hash = _security.BuildQueryDrHash(
            requestId: requestId,
            version: "2.1.0",
            command: "querydr",
            tmnCode: options.TmnCode,
            txnRef: txnRef,
            transDate: transDate,
            createDate: createDate,
            ipAddr: ipAddr,
            orderInfo: orderInfo,
            hashSecret: options.HashSecret);

        var apiRequest = new VnpayQueryDrApiRequest(
            RequestId: requestId,
            Version: "2.1.0",
            Command: "querydr",
            TmnCode: options.TmnCode,
            TxnRef: txnRef,
            OrderInfo: orderInfo,
            TransactionDate: transDate,
            CreateDate: createDate,
            IpAddr: ipAddr,
            SecureHash: hash);

        var response = await _client.QueryTransactionAsync(apiRequest, ct);

        // Verify response signature using pipe '|' separated specification
        var isSignatureValid = _security.VerifyQueryDrResponseHash(
            responseId: response.ResponseId ?? "",
            command: response.Command ?? "querydr",
            responseCode: response.ResponseCode ?? "",
            message: response.Message ?? "",
            tmnCode: response.TmnCode ?? "",
            txnRef: response.TxnRef ?? "",
            amount: response.Amount ?? "",
            bankCode: response.BankCode ?? "",
            payDate: response.PayDate ?? "",
            transactionNo: response.TransactionNo ?? "",
            transactionType: response.TransactionType ?? "",
            transactionStatus: response.TransactionStatus ?? "",
            orderInfo: response.OrderInfo ?? "",
            promotionCode: response.PromotionCode,
            promotionAmount: response.PromotionAmount,
            hashSecret: options.HashSecret,
            secureHash: response.SecureHash ?? "");

        if (!isSignatureValid)
        {
            _logger.LogWarning("VNPay QueryDR response signature verification failed for TxnRef: {TxnRef}", txnRef);
            return new ProviderQueryResult(
                IsSuccess: false,
                ResponseCode: response.ResponseCode ?? "97",
                TransactionStatus: response.TransactionStatus ?? "97",
                ProviderTxnNo: response.TransactionNo,
                Message: "Invalid response signature from VNPay");
        }

        // Per VNPay spec: ResponseCode == "00" only means Query API succeeded;
        // TransactionStatus == "00" means the payment itself was successful.
        var isPaidSuccess = response.ResponseCode == "00" && response.TransactionStatus == "00";

        return new ProviderQueryResult(
            IsSuccess: isPaidSuccess,
            ResponseCode: response.ResponseCode ?? "99",
            TransactionStatus: response.TransactionStatus ?? "99",
            ProviderTxnNo: response.TransactionNo,
            Message: response.Message ?? (isPaidSuccess ? "Transaction Paid" : "Transaction not completed"));
    }

    public async Task<ProviderRefundResult> RefundAsync(
        ProviderRefundRequest request,
        CancellationToken ct)
    {
        var options = _options.Value;
        if (!options.IsValid())
        {
            throw new DomainException(
                ErrorCodes.Payment.ProviderUnavailable,
                "VNPay credentials are not properly configured.",
                503);
        }

        var requestId = Guid.NewGuid().ToString("N");
        var txnRef = request.PaymentId.ToString("N");
        var scaledAmount = checked(request.AmountVnd * 100).ToString();
        var transDate = request.TransactionDate.ToString("yyyyMMddHHmmss");
        var createDate = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var ipAddr = string.IsNullOrWhiteSpace(request.ClientIp) ? "127.0.0.1" : request.ClientIp;
        var transactionType = "02"; // 02: Full refund, 03: Partial refund
        var transactionNo = request.ProviderTxnNo ?? "0";
        var createBy = "SystemAdmin";

        var hash = _security.BuildRefundHash(
            requestId: requestId,
            version: "2.1.0",
            command: "refund",
            tmnCode: options.TmnCode,
            transactionType: transactionType,
            txnRef: txnRef,
            amount: scaledAmount,
            transactionNo: transactionNo,
            transactionDate: transDate,
            createBy: createBy,
            createDate: createDate,
            ipAddr: ipAddr,
            orderInfo: request.Reason,
            hashSecret: options.HashSecret);

        var apiRequest = new VnpayRefundApiRequest(
            RequestId: requestId,
            Version: "2.1.0",
            Command: "refund",
            TmnCode: options.TmnCode,
            TransactionType: transactionType,
            TxnRef: txnRef,
            Amount: scaledAmount,
            TransactionNo: transactionNo,
            TransactionDate: transDate,
            CreateBy: createBy,
            CreateDate: createDate,
            IpAddr: ipAddr,
            OrderInfo: request.Reason,
            SecureHash: hash);

        var response = await _client.RefundAsync(apiRequest, ct);

        var isSignatureValid = _security.VerifyRefundResponseHash(
            responseId: response.ResponseId ?? "",
            command: response.Command ?? "refund",
            responseCode: response.ResponseCode ?? "",
            message: response.Message ?? "",
            tmnCode: response.TmnCode ?? "",
            txnRef: response.TxnRef ?? "",
            amount: response.Amount ?? "",
            bankCode: response.BankCode ?? "",
            payDate: response.PayDate ?? "",
            transactionNo: response.TransactionNo ?? "",
            transactionType: response.TransactionType ?? "",
            transactionStatus: response.TransactionStatus ?? "",
            orderInfo: response.OrderInfo ?? "",
            hashSecret: options.HashSecret,
            secureHash: response.SecureHash ?? "");

        if (!isSignatureValid)
        {
            _logger.LogWarning("VNPay Refund response signature verification failed for TxnRef: {TxnRef}", txnRef);
            return new ProviderRefundResult(
                IsSuccess: false,
                ResponseCode: response.ResponseCode ?? "97",
                ProviderRefundRef: null,
                Message: "Invalid response signature from VNPay");
        }

        var isRefundSuccess = response.ResponseCode == "00";
        return new ProviderRefundResult(
            IsSuccess: isRefundSuccess,
            ResponseCode: response.ResponseCode ?? "99",
            ProviderRefundRef: response.TransactionNo,
            Message: response.Message ?? (isRefundSuccess ? "Refund Success" : "Refund Failed"));
    }
}

