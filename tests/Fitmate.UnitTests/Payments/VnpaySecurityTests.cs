using System.Net;
using Fitmate.Application.Payments;
using Fitmate.Infrastructure.Payments.Vnpay;
using FluentAssertions;
using Xunit;

namespace Fitmate.UnitTests.Payments;

public class VnpaySecurityTests
{
    private readonly VnpaySecurity _sut = new();

    private static VnpayOptions CreateSampleOptions() => new()
    {
        TmnCode = "FITMATETEST01",
        HashSecret = "VNPAYTESTSECRETKEY1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF",
        PaymentUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
        ReturnUrl = "http://localhost:5000/api/v1/payments/vnpay/return",
        ApiUrl = "https://sandbox.vnpayment.vn/merchant_webapi/api/transaction"
    };

    [Fact]
    public void BuildCheckoutUrl_CanonicalAsciiSorting_ParametersOrderedAlphabetically()
    {
        var options = CreateSampleOptions();
        var createDate = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var expireDate = createDate.AddMinutes(5);

        var url = _sut.BuildCheckoutUrl(
            options: options,
            txnRef: "txn_001",
            amountVnd: 500_000L,
            orderInfo: "Thanh toan lich tap Fitmate",
            clientIp: "127.0.0.1",
            createDate: createDate,
            expireDate: expireDate);

        url.Should().StartWith("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?");
        url.Should().Contain("vnp_SecureHash=");

        var query = url.Substring(url.IndexOf('?') + 1);
        var parts = query.Split('&');

        // Extract parameter keys in order (excluding hash)
        var keys = parts
            .Where(p => !p.StartsWith("vnp_SecureHash="))
            .Select(p => p.Split('=')[0])
            .ToList();

        // Verify ordinal sorting
        var sortedKeys = new List<string>(keys);
        sortedKeys.Sort(StringComparer.Ordinal);
        keys.Should().Equal(sortedKeys);
    }

    [Fact]
    public void BuildCheckoutUrl_ScalesAmountBy100()
    {
        var options = CreateSampleOptions();
        var now = DateTime.UtcNow;

        var url = _sut.BuildCheckoutUrl(
            options: options,
            txnRef: "txn_scale",
            amountVnd: 250_000L,
            orderInfo: "Test scale",
            clientIp: "10.0.0.1",
            createDate: now,
            expireDate: now.AddMinutes(5));

        // 250,000 * 100 = 25,000,000
        url.Should().Contain("vnp_Amount=25000000");
    }

    [Fact]
    public void BuildCheckoutUrl_AmountOverflow_ThrowsOverflowException()
    {
        var options = CreateSampleOptions();
        var now = DateTime.UtcNow;

        // Passing a huge amount that overflows long.MaxValue when multiplied by 100
        var act = () => _sut.BuildCheckoutUrl(
            options: options,
            txnRef: "txn_overflow",
            amountVnd: long.MaxValue / 50,
            orderInfo: "Test overflow",
            clientIp: "10.0.0.1",
            createDate: now,
            expireDate: now.AddMinutes(5));

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void VerifyIpnSignature_ValidSignature_ReturnsTrue()
    {
        var options = CreateSampleOptions();
        var rawParams = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = options.TmnCode,
            ["vnp_Amount"] = "50000000",
            ["vnp_BankCode"] = "NCB",
            ["vnp_BankTranNo"] = "VNP14567890",
            ["vnp_CardType"] = "ATM",
            ["vnp_OrderInfo"] = "Thanh toan",
            ["vnp_PayDate"] = "20260924120500",
            ["vnp_ResponseCode"] = "00",
            ["vnp_TransactionNo"] = "14567890",
            ["vnp_TransactionStatus"] = "00",
            ["vnp_TxnRef"] = "a1b2c3d4e5f67890"
        };

        // Compute genuine hash
        var sorted = new SortedDictionary<string, string>(rawParams, StringComparer.Ordinal);
        var rawData = string.Join("&", sorted.Select(kv => $"{kv.Key}={WebUtility.UrlEncode(kv.Value)}"));
        var signature = _sut.HmacSha512(rawData, options.HashSecret);

        rawParams["vnp_SecureHash"] = signature;
        rawParams["vnp_SecureHashType"] = "HMACSHA512";

        var isValid = _sut.VerifyIpnSignature(rawParams, options.HashSecret);
        isValid.Should().BeTrue();
    }

    [Fact]
    public void VerifyIpnSignature_TamperedAmount_ReturnsFalse()
    {
        var options = CreateSampleOptions();
        var rawParams = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = options.TmnCode,
            ["vnp_Amount"] = "50000000",
            ["vnp_OrderInfo"] = "Thanh toan",
            ["vnp_ResponseCode"] = "00",
            ["vnp_TxnRef"] = "a1b2c3d4e5f67890"
        };

        var sorted = new SortedDictionary<string, string>(rawParams, StringComparer.Ordinal);
        var rawData = string.Join("&", sorted.Select(kv => $"{kv.Key}={WebUtility.UrlEncode(kv.Value)}"));
        var signature = _sut.HmacSha512(rawData, options.HashSecret);

        // Tamper amount
        rawParams["vnp_Amount"] = "10000000";
        rawParams["vnp_SecureHash"] = signature;

        var isValid = _sut.VerifyIpnSignature(rawParams, options.HashSecret);
        isValid.Should().BeFalse();
    }

    [Fact]
    public void BuildQueryDrHash_UsesPipeSeparatorFormat()
    {
        var secret = "TEST_SECRET";
        var hash = _sut.BuildQueryDrHash(
            requestId: "req_01",
            version: "2.1.0",
            command: "querydr",
            tmnCode: "FITMATETEST01",
            txnRef: "txn_001",
            transDate: "20260924120000",
            createDate: "20260924120500",
            ipAddr: "127.0.0.1",
            orderInfo: "Query test",
            hashSecret: secret);

        var expectedRawData = "req_01|2.1.0|querydr|FITMATETEST01|txn_001|20260924120000|20260924120500|127.0.0.1|Query test";
        var expectedHash = _sut.HmacSha512(expectedRawData, secret);

        hash.Should().Be(expectedHash);
    }

    [Fact]
    public void VerifyQueryDrResponseHash_ValidAndInvalidSignature()
    {
        var secret = "TEST_SECRET";
        var rawResponseData = "res_01|querydr|00|Success|FITMATETEST01|txn_001|50000000|NCB|20260924120500|14567890|01|00|Query test";
        var validHash = _sut.HmacSha512(rawResponseData, secret);

        var valid = _sut.VerifyQueryDrResponseHash(
            responseId: "res_01",
            command: "querydr",
            responseCode: "00",
            message: "Success",
            tmnCode: "FITMATETEST01",
            txnRef: "txn_001",
            amount: "50000000",
            bankCode: "NCB",
            payDate: "20260924120500",
            transactionNo: "14567890",
            transactionType: "01",
            transactionStatus: "00",
            orderInfo: "Query test",
            promotionCode: null,
            promotionAmount: null,
            hashSecret: secret,
            secureHash: validHash);

        valid.Should().BeTrue();

        var invalid = _sut.VerifyQueryDrResponseHash(
            responseId: "res_01",
            command: "querydr",
            responseCode: "00",
            message: "Success",
            tmnCode: "FITMATETEST01",
            txnRef: "txn_001",
            amount: "50000000",
            bankCode: "NCB",
            payDate: "20260924120500",
            transactionNo: "14567890",
            transactionType: "01",
            transactionStatus: "00",
            orderInfo: "Query test",
            promotionCode: null,
            promotionAmount: null,
            hashSecret: secret,
            secureHash: "INVALID_HASH_123");

        invalid.Should().BeFalse();
    }

    [Fact]
    public void BuildRefundHash_UsesPipeSeparatorFormat()
    {
        var secret = "TEST_SECRET";
        var hash = _sut.BuildRefundHash(
            requestId: "req_ref_01",
            version: "2.1.0",
            command: "refund",
            tmnCode: "FITMATETEST01",
            transactionType: "02",
            txnRef: "txn_001",
            amount: "50000000",
            transactionNo: "14567890",
            transactionDate: "20260924120000",
            createBy: "Admin",
            createDate: "20260924121000",
            ipAddr: "127.0.0.1",
            orderInfo: "Hoan tien tap",
            hashSecret: secret);

        var expectedRaw = "req_ref_01|2.1.0|refund|FITMATETEST01|02|txn_001|50000000|14567890|20260924120000|Admin|20260924121000|127.0.0.1|Hoan tien tap";
        var expectedHash = _sut.HmacSha512(expectedRaw, secret);

        hash.Should().Be(expectedHash);
    }

    [Fact]
    public void SecureCompare_HandlesCaseInsensitiveAndTimingSafeComparison()
    {
        var hash1 = "A1B2C3D4E5F6";
        var hash2 = "a1b2c3d4e5f6";
        var hash3 = "A1B2C3D4E5F7";

        _sut.SecureCompare(hash1, hash2).Should().BeTrue();
        _sut.SecureCompare(hash1, hash3).Should().BeFalse();
        _sut.SecureCompare(null, hash1).Should().BeFalse();
        _sut.SecureCompare(hash1, null).Should().BeFalse();
    }
}
