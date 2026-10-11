using System.Text.Json;
using Fitmate.Application.Auth;
using Fitmate.Application.Finance;
using Fitmate.Application.Payments;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Payments.Payos;
using FluentAssertions;
using Moq;

namespace Fitmate.UnitTests;

public class PayosAndManualSettlementTests
{
    [Fact]
    public void Signature_matches_official_payment_webhook_vector_and_rejects_tampering()
    {
        // Public test vector: https://payos.vn/docs/tich-hop-webhook/kiem-tra-du-lieu-voi-signature/
        const string key = "1a54716c8f0efb2744fb28b6e38b25da7f67a925d98bc1c18bd8faaecadd7675";
        const string signature = "412e915d2871504ed31be63c8f62a149a4410d34c4c42affc9006ef9917eaa03";
        const string json = """
            {"orderCode":123,"amount":3000,"description":"VQRIO123","accountNumber":"12345678",
            "reference":"TF230204212323","transactionDateTime":"2023-02-04 18:25:00","currency":"VND",
            "paymentLinkId":"124c33293c43417ab7879e14c8d9eb18","code":"00","desc":"Thành công",
            "counterAccountBankId":"","counterAccountBankName":"","counterAccountName":"","counterAccountNumber":"",
            "virtualAccountName":"","virtualAccountNumber":""}
            """;
        using var original = JsonDocument.Parse(json);
        PayosSignature.Verify(original.RootElement, signature, key).Should().BeTrue();
        using var changed = JsonDocument.Parse(json.Replace("3000", "3001"));
        PayosSignature.Verify(changed.RootElement, signature, key).Should().BeFalse();
        PayosSignature.Verify(original.RootElement, new string('x', 64), key).Should().BeFalse();
        PayosSignature.Verify(original.RootElement, signature, "").Should().BeFalse();
    }

    [Fact]
    public void Merchant_configuration_is_fail_closed()
    {
        var config = new PayosOptions();
        config.IsValid().Should().BeFalse();
        config.ClientId = "test-client"; config.ApiKey = "test-api"; config.ChecksumKey = "test-checksum";
        config.AccountNumber = "test-account"; config.ReturnUrl = "https://example.test/return"; config.CancelUrl = "https://example.test/cancel";
        config.IsValid().Should().BeFalse();
        config.MerchantApproved = true;
        config.IsValid().Should().BeTrue();
        config.ReturnUrl = "http://example.test/return";
        config.IsValid().Should().BeFalse();
    }

    [Fact]
    public void Completion_alone_never_authorizes_transfer()
    {
        var settlement = ManualPtSettlement.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var act = () => settlement.RecordTransfer(Guid.NewGuid(), 90000, "bank:one", "case:one", DateTime.UtcNow, DateTime.UtcNow);
        act.Should().Throw<ConflictException>();
        settlement.Status.Should().Be("on_hold");
    }

    [Fact]
    public void Approval_requires_both_calls_after_session_and_no_dispute()
    {
        var now = DateTime.UtcNow;
        var settlement = ManualPtSettlement.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var act = () => settlement.Approve(Guid.NewGuid(), 90000, now.AddHours(-3), now.AddMinutes(-5),
            now.AddHours(-1), "case:session", "case:payee", true, now,
            "970422", "MB", "123456789", "NGUYEN VAN A", now.AddDays(-1));
        act.Should().Throw<DomainException>();
        act = () => settlement.Approve(Guid.NewGuid(), 90000, now.AddMinutes(-5), now.AddMinutes(-5),
            now.AddHours(-1), "case:session", "case:payee", false, now,
            "970422", "MB", "123456789", "NGUYEN VAN A", now.AddDays(-1));
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Approved_then_transferred_is_idempotent_and_immutable()
    {
        var now = DateTime.UtcNow;
        var admin = Guid.NewGuid();
        var settlement = ManualPtSettlement.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        settlement.Approve(admin, 90000, now.AddMinutes(-5), now.AddMinutes(-4), now.AddHours(-1),
            "case:session", "case:payee", true, now,
            "970422", "MB", "123456789", "NGUYEN VAN A", now.AddDays(-1));
        settlement.Status.Should().Be("approved");
        settlement.TransferredAt.Should().BeNull();
        settlement.PayeeAccountName.Should().Be("NGUYEN VAN A");
        settlement.RecordTransfer(admin, 90000, "bank:one", "case:transfer", now, now);
        settlement.RecordTransfer(admin, 90000, "bank:one", "case:transfer", now, now);
        settlement.Status.Should().Be("paid");
        var different = () => settlement.RecordTransfer(admin, 90000, "bank:two", "case:transfer", now, now);
        different.Should().Throw<ConflictException>();
        var hold = () => settlement.Hold("Dispute");
        hold.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Pending_refund_blocks_eligibility_and_partial_refund_uses_cumulative_rounding()
    {
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1), 60, 101);
        var payment = Payment.CreatePending(appt.Id, PaymentMethod.Payos, 101, null);
        payment.MarkPaid("test-txn");
        appt.AttachPayment(payment.Id, 101, 0, .10m, 10, 91, "v1");
        appt.ForceStatusForTesting(AppointmentStatus.Completed, DateTime.UtcNow.AddHours(-25));
        payment.ApplyRefund(5); // Original fee ratio: round(10 * 5 / 101) = 0, not round(5 * .1) = 1.
        var context = new SettlementContext(appt, payment, ManualPtSettlement.Create(appt.Id, appt.PtUserId, payment.Id), false);
        ManualSettlementService.EligibleAmount(context).Should().Be(86);
        var blocked = () => ManualSettlementService.EligibleAmount(context with { HasPendingRefund = true });
        blocked.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Payout_is_blocked_until_24_hours_after_confirmed_completion()
    {
        var slot = DateTime.UtcNow.AddDays(-2);
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), slot, slot, 60, 100_000);
        var payment = Payment.CreatePending(appt.Id, PaymentMethod.Payos, 100_000, null);
        payment.MarkPaid("test-waiting-period");
        appt.AttachPayment(payment.Id, 100_000, 0, .10m, 10_000, 90_000, "v1");
        appt.ForceStatusForTesting(AppointmentStatus.Completed, DateTime.UtcNow.AddHours(-23));
        var context = new SettlementContext(appt, payment,
            ManualPtSettlement.Create(appt.Id, appt.PtUserId, payment.Id), false);

        var act = () => ManualSettlementService.EligibleAmount(context);

        act.Should().Throw<ConflictException>()
            .Which.Code.Should().Be("SETTLEMENT_WAITING_PERIOD");
    }

    [Fact]
    public void Package_payout_uses_verified_session_allocation_and_its_own_refunds()
    {
        var slot = DateTime.UtcNow.AddDays(-2);
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), slot, slot, 60, 100000);
        var payment = Payment.CreatePendingForPackage(Guid.NewGuid(), PaymentMethod.Payos, 400000);
        payment.MarkPaid("test-package-payout");
        appt.AttachPayment(payment.Id, 100000, 0, .10m, 10000, 90000, "package-v1");
        appt.ForceStatusForTesting(AppointmentStatus.Completed, DateTime.UtcNow.AddHours(-25));
        payment.ApplyRefund(25000); // May belong to this or another session, never allocate globally.
        var baseContext = new SettlementContext(appt, payment,
            ManualPtSettlement.Create(appt.Id, appt.PtUserId, payment.Id), false);

        var unverified = () => ManualSettlementService.EligibleAmount(baseContext);
        unverified.Should().Throw<ConflictException>();
        ManualSettlementService.EligibleAmount(baseContext with
        {
            PackageSessionVerified = true, PackageSessionRefundedVnd = 0
        }).Should().Be(90000);
        ManualSettlementService.EligibleAmount(baseContext with
        {
            PackageSessionVerified = true, PackageSessionRefundedVnd = 25000
        }).Should().Be(67500);
    }

    [Fact]
    public async Task Non_admin_cannot_review_or_transfer_in_application_layer()
    {
        var auth = new Mock<IAuthorizationGuard>();
        auth.Setup(a => a.EnsureRoleAsync(It.IsAny<Guid>(), "Admin", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("FORBIDDEN", "Admin required."));
        var store = new Mock<IManualSettlementStore>(MockBehavior.Strict);
        var service = new ManualSettlementService(store.Object, auth.Object, Mock.Of<IFinanceStepUp>());
        var act = () => service.RecordTransferAsync(Guid.NewGuid(), Guid.NewGuid(), new("bank", "case", DateTime.UtcNow), default);
        await act.Should().ThrowAsync<ForbiddenException>();
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Admin_without_step_up_cannot_mutate_finance()
    {
        var auth = new Mock<IAuthorizationGuard>();
        auth.Setup(a => a.EnsureRoleAsync(It.IsAny<Guid>(), "Admin", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var stepUp = new Mock<IFinanceStepUp>();
        stepUp.Setup(s => s.EnsureAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("FINANCE_MFA_REQUIRED", "MFA required."));
        var store = new Mock<IManualSettlementStore>(MockBehavior.Strict);
        var service = new ManualSettlementService(store.Object, auth.Object, stepUp.Object);
        var act = () => service.CreateRefundAsync(Guid.NewGuid(), new(Guid.NewGuid(), 100, "Refund"), default);
        await act.Should().ThrowAsync<ForbiddenException>();
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public void Payment_contract_uses_snake_case_and_lowercase_provider()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var request = JsonSerializer.Deserialize<CreatePaymentRequest>("{\"hold_id\":\"11111111-1111-1111-1111-111111111111\",\"method\":\"payos\"}", options)!;
        request.HoldId.Should().NotBeEmpty();
        request.Method.Should().Be(PaymentMethod.Payos);
        var view = new PaymentView(Guid.NewGuid(), Guid.NewGuid(), PaymentMethod.Payos, PaymentStatus.Pending, 100000, 0, null, null, DateTime.UtcNow, null);
        var json = JsonSerializer.Serialize(view, options);
        json.Should().Contain("\"amount_vnd\":100000").And.Contain("\"method\":\"payos\"").And.Contain("\"status\":\"Pending\"");
    }
}
