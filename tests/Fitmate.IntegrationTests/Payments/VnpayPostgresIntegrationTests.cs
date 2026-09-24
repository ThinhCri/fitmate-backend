using System.Net;
using System.Net.Http.Json;
using Fitmate.Application.Payments;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Payments.Vnpay;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fitmate.IntegrationTests.Payments;

/// <summary>
/// BE-400: PostgreSQL integration tests for real VNPay IPN webhook settlement and concurrency.
/// Covers:
/// 1. Concurrent duplicate IPN race -> exactly one '00' success and one '02' duplicate ACK.
/// 2. Late IPN after hold expired -> records Payment.Status = Paid, creates Refund.Pending, does NOT reclaim slot.
/// 3. Out-of-order failure callback -> does NOT downgrade Paid status.
/// 4. Invalid signature and tampered amount -> returns '97' and '04', DB unaffected.
/// </summary>
public class VnpayPostgresIntegrationTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;
    private readonly IVnpaySecurity _security;
    private readonly VnpayOptions _vnpayOptions;

    public VnpayPostgresIntegrationTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();

        _security = _factory.Services.GetRequiredService<IVnpaySecurity>();
        _vnpayOptions = _factory.Services.GetRequiredService<IOptions<VnpayOptions>>().Value;

        if (string.IsNullOrWhiteSpace(_vnpayOptions.HashSecret))
        {
            _vnpayOptions.HashSecret = "VNPAYINTEGRATIONTESTSECRETKEY1234567890ABCDEF1234567890ABCDEF";
            _vnpayOptions.TmnCode = "FITMATETEST";
        }
    }

    private string BuildSignedIpnQueryString(Dictionary<string, string> queryParams)
    {
        var sorted = new SortedDictionary<string, string>(queryParams, StringComparer.Ordinal);
        var rawData = string.Join("&", sorted.Select(kv => $"{kv.Key}={WebUtility.UrlEncode(kv.Value)}"));
        var hash = _security.HmacSha512(rawData, _vnpayOptions.HashSecret);

        var fullQuery = string.Join("&", queryParams.Select(kv => $"{kv.Key}={WebUtility.UrlEncode(kv.Value)}"));
        return $"{fullQuery}&vnp_SecureHash={hash}";
    }

    private async Task<(Guid MemberId, Guid PtId, Guid HoldId, Guid ApptId, Guid PaymentId)> SeedPendingBookingAndPaymentAsync(
        long amountVnd, bool isExpired = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();

        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var slotStart = DateTime.UtcNow.AddHours(2);

        var hold = BookingHold.Create(memberId, ptId, serviceId, slotStart, TimeSpan.FromMinutes(5));
        var appt = Appointment.CreateHold(memberId, ptId, serviceId, slotStart, slotStart, 60, amountVnd);

        if (isExpired)
        {
            hold.Release();
        }

        var payment = Payment.CreatePending(appt.Id, PaymentMethod.Vnpay, amountVnd, "https://mock.redirect.url");

        db.BookingHolds.Add(hold);
        db.Appointments.Add(appt);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        return (memberId, ptId, hold.Id, appt.Id, payment.Id);
    }

    [Fact]
    public async Task Concurrent_Ipn_Race_Produces_Exactly_One_00_And_One_02_Duplicate_Ack()
    {
        var amountVnd = 350_000L;
        var (_, _, _, apptId, paymentId) = await SeedPendingBookingAndPaymentAsync(amountVnd);

        var queryParams = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = _vnpayOptions.TmnCode,
            ["vnp_Amount"] = (amountVnd * 100).ToString(),
            ["vnp_BankCode"] = "NCB",
            ["vnp_BankTranNo"] = "VNPCONCURRENT123",
            ["vnp_CardType"] = "ATM",
            ["vnp_OrderInfo"] = "Thanh toan lich tap Fitmate",
            ["vnp_PayDate"] = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            ["vnp_ResponseCode"] = "00",
            ["vnp_TransactionNo"] = "99887766",
            ["vnp_TransactionStatus"] = "00",
            ["vnp_TxnRef"] = paymentId.ToString("N")
        };

        var queryString = BuildSignedIpnQueryString(queryParams);

        var client1 = _factory.CreateClient();
        var client2 = _factory.CreateClient();

        // Fire both IPN requests concurrently
        var task1 = client1.GetFromJsonAsync<VnpayIpnResponse>($"/api/v1/payments/vnpay/ipn?{queryString}");
        var task2 = client2.GetFromJsonAsync<VnpayIpnResponse>($"/api/v1/payments/vnpay/ipn?{queryString}");

        var responses = await Task.WhenAll(task1, task2);

        var rspCodes = responses.Select(r => r?.RspCode).ToList();
        rspCodes.Should().Contain("00"); // Exactly one '00' Confirm Success
        rspCodes.Should().Contain("02"); // Exactly one '02' Order Already Confirmed

        // Verify DB State
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId);
        payment.Should().NotBeNull();
        payment!.Status.Should().Be(PaymentStatus.Paid);
        payment.ProviderTxnRef.Should().Be("99887766");

        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == apptId);
        appt.Should().NotBeNull();
        appt!.Status.Should().Be(BookingStatus.Paid);
        appt.PaymentId.Should().Be(paymentId);
    }

    [Fact]
    public async Task Late_Ipn_After_Hold_Expired_Records_Paid_And_Pending_Refund_Without_Claiming_Slot()
    {
        var amountVnd = 500_000L;
        // Hold is expired
        var (_, _, _, apptId, paymentId) = await SeedPendingBookingAndPaymentAsync(amountVnd, isExpired: true);

        var queryParams = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = _vnpayOptions.TmnCode,
            ["vnp_Amount"] = (amountVnd * 100).ToString(),
            ["vnp_BankCode"] = "NCB",
            ["vnp_BankTranNo"] = "VNPLATE123",
            ["vnp_CardType"] = "ATM",
            ["vnp_OrderInfo"] = "Thanh toan lich tap muon",
            ["vnp_PayDate"] = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            ["vnp_ResponseCode"] = "00",
            ["vnp_TransactionNo"] = "77665544",
            ["vnp_TransactionStatus"] = "00",
            ["vnp_TxnRef"] = paymentId.ToString("N")
        };

        var queryString = BuildSignedIpnQueryString(queryParams);
        var client = _factory.CreateClient();

        var response = await client.GetFromJsonAsync<VnpayIpnResponse>($"/api/v1/payments/vnpay/ipn?{queryString}");
        response.Should().NotBeNull();
        response!.RspCode.Should().Be("00"); // VNPay notified that payment was received

        // Verify DB State:
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId);
        payment.Should().NotBeNull();
        payment!.Status.Should().Be(PaymentStatus.Paid);

        // Appointment MUST NOT be revived or attached as Paid
        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == apptId);
        appt.Should().NotBeNull();
        appt!.Status.Should().Be(BookingStatus.Held);
        appt.PaymentId.Should().BeNull();

        // A Refund record in Pending status must have been automatically created
        var refund = await db.Refunds.FirstOrDefaultAsync(r => r.PaymentId == paymentId);
        refund.Should().NotBeNull();
        refund!.Status.Should().Be(RefundStatus.Pending);
        refund.AmountVnd.Should().Be(amountVnd);
    }

    [Fact]
    public async Task Out_Of_Order_Failure_Callback_Does_Not_Downgrade_Paid_State()
    {
        var amountVnd = 400_000L;
        var (_, _, _, _, paymentId) = await SeedPendingBookingAndPaymentAsync(amountVnd);

        // 1. First IPN arrives and marks payment Paid
        var successParams = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = _vnpayOptions.TmnCode,
            ["vnp_Amount"] = (amountVnd * 100).ToString(),
            ["vnp_ResponseCode"] = "00",
            ["vnp_TransactionNo"] = "11223344",
            ["vnp_TxnRef"] = paymentId.ToString("N")
        };
        var successQuery = BuildSignedIpnQueryString(successParams);
        var client = _factory.CreateClient();

        var firstResp = await client.GetFromJsonAsync<VnpayIpnResponse>($"/api/v1/payments/vnpay/ipn?{successQuery}");
        firstResp!.RspCode.Should().Be("00");

        // 2. Later, an out-of-order failure callback arrives
        var failureParams = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = _vnpayOptions.TmnCode,
            ["vnp_Amount"] = (amountVnd * 100).ToString(),
            ["vnp_ResponseCode"] = "24", // Customer cancelled on payment portal
            ["vnp_TransactionNo"] = "0",
            ["vnp_TxnRef"] = paymentId.ToString("N")
        };
        var failureQuery = BuildSignedIpnQueryString(failureParams);

        var lateResp = await client.GetFromJsonAsync<VnpayIpnResponse>($"/api/v1/payments/vnpay/ipn?{failureQuery}");
        lateResp!.RspCode.Should().Be("02"); // Already confirmed, duplicate ACK returned

        // Verify DB State: Payment is still Paid!
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId);
        payment!.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public async Task Tampered_Amount_And_Invalid_Signature_Rejected_Without_Db_Mutation()
    {
        var amountVnd = 200_000L;
        var (_, _, _, _, paymentId) = await SeedPendingBookingAndPaymentAsync(amountVnd);

        var client = _factory.CreateClient();

        // 1. Invalid signature
        var invalidSigQuery = $"vnp_TmnCode={_vnpayOptions.TmnCode}&vnp_Amount=20000000&vnp_TxnRef={paymentId:N}&vnp_ResponseCode=00&vnp_SecureHash=INVALID_HASH";
        var sigResp = await client.GetFromJsonAsync<VnpayIpnResponse>($"/api/v1/payments/vnpay/ipn?{invalidSigQuery}");
        sigResp!.RspCode.Should().Be("97"); // Invalid checksum

        // 2. Tampered amount (amount is signed for 100,000 but payment was for 200,000)
        var tamperedParams = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = _vnpayOptions.TmnCode,
            ["vnp_Amount"] = "10000000", // 100,000 VND instead of 200,000
            ["vnp_ResponseCode"] = "00",
            ["vnp_TransactionNo"] = "55443322",
            ["vnp_TxnRef"] = paymentId.ToString("N")
        };
        var tamperedQuery = BuildSignedIpnQueryString(tamperedParams);
        var amountResp = await client.GetFromJsonAsync<VnpayIpnResponse>($"/api/v1/payments/vnpay/ipn?{tamperedQuery}");
        amountResp!.RspCode.Should().Be("04"); // Invalid amount

        // Verify DB State: Payment still Pending
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId);
        payment!.Status.Should().Be(PaymentStatus.Pending);
    }
}

