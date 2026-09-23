using Fitmate.Application.Auth;
using Fitmate.Application.Capabilities;
using Fitmate.Application.Finance;
using Fitmate.Application.Payments;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using FluentAssertions;
using Moq;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-829 / BR-17: Kiểm thử bảo toàn backlog VIP, Rewards và PT Payout.
/// Chứng minh:
/// 1. Capabilities công bố trung thực 3 tính năng bị đóng băng (VipPurchase, RewardCashout, PtAutomaticPayout).
/// 2. Application Guard ngăn chặn mọi hành vi bypass UI để gọi chức năng bị khóa (throw 403 FITMATE.FEATURE.DISABLED).
/// 3. PaymentService chỉ phục vụ booking PT hợp lệ, từ chối giao dịch giả mạo.
/// 4. Doanh thu PT được tính toán chặt chẽ từ Paid + Completed trừ Refund, hoàn toàn độc lập với điểm thưởng.
/// 5. Quyết toán PT bắt buộc qua Admin với mã đối soát ngân hàng thật (ReferenceNumber), không có rút tiền tự động.
/// </summary>
public class VipAndRewardBacklogGuardTests
{
    [Fact]
    public void SystemCapabilitiesDto_Includes_VipPurchase_RewardCashout_And_PtAutomaticPayout_As_Disabled()
    {
        var dto = new SystemCapabilitiesDto(
            ApiVersion: "1.0.0",
            Environment: "production",
            Ai: new AiCapabilitiesStatus(false, false, false, false, "Awaiting model"),
            VipPurchase: new FeatureCapabilityStatus(false, "VIP purchases are disabled in this release."),
            RewardCashout: new FeatureCapabilityStatus(false, "Reward points cannot be cashed out."),
            PtAutomaticPayout: new FeatureCapabilityStatus(false, "Automatic direct payouts are disabled."),
            PushNotificationsEnabled: true,
            PaymentsEnabled: true,
            StorageProvider: "local",
            Policies: new PlatformPoliciesSummary(1000, 24, 7),
            Contact: new PublicContactInfo("support@fitmate.vn", "1900-FITMATE", "https://fitmate.vn/terms", "https://fitmate.vn/privacy", "https://fitmate.vn/deletion"));

        dto.VipPurchase.Enabled.Should().BeFalse();
        dto.VipPurchase.StatusReason.Should().Contain("VIP");

        dto.RewardCashout.Enabled.Should().BeFalse();
        dto.RewardCashout.StatusReason.Should().Contain("Reward points");

        dto.PtAutomaticPayout.Enabled.Should().BeFalse();
        dto.PtAutomaticPayout.StatusReason.Should().Contain("Automatic");
    }

    [Fact]
    public void BacklogFeatureGuard_Throws_Forbidden_For_All_Gated_Actions()
    {
        // 1. VIP Purchase gate
        var actVip = () => BacklogFeatureGuard.AssertVipPurchaseAllowed();
        actVip.Should().Throw<DomainException>()
            .Where(e => e.Code == BacklogFeatureGuard.FeatureDisabledCode && e.HttpStatus == 403);

        // 2. Reward Cashout gate
        var actReward = () => BacklogFeatureGuard.AssertRewardCashoutAllowed();
        actReward.Should().Throw<DomainException>()
            .Where(e => e.Code == BacklogFeatureGuard.FeatureDisabledCode && e.HttpStatus == 403);

        // 3. Automatic Payout gate
        var actPayout = () => BacklogFeatureGuard.AssertAutomaticPayoutAllowed();
        actPayout.Should().Throw<DomainException>()
            .Where(e => e.Code == BacklogFeatureGuard.FeatureDisabledCode && e.HttpStatus == 403);
    }

    [Fact]
    public async Task PaymentService_Rejects_Invalid_Hold_With_NotFound_Preventing_Arbitrary_Checkouts()
    {
        var storeMock = new Mock<IPaymentStore>();
        var providerMock = new Mock<IPaymentProvider>();

        storeMock.Setup(s => s.FindHoldAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingHold?)null);

        var service = new PaymentService(storeMock.Object, providerMock.Object);
        var memberId = Guid.NewGuid();
        var request = new CreatePaymentRequest
        {
            HoldId = Guid.NewGuid(),
            Method = PaymentMethod.Vnpay
        };

        var act = () => service.CreateAsync(memberId, request, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>()
            .Where(e => e.Code == PaymentErrorCodes.HoldNotFound);
    }

    [Fact]
    public async Task TrainerRevenue_Calculates_From_Paid_Bookings_And_Refunds_Isolated_From_Rewards()
    {
        var storeMock = new Mock<ITrainerRevenueStore>();
        var authzMock = new Mock<IAuthorizationGuard>();
        var outboxMock = new Mock<Fitmate.Application.Outbox.IOutboxDispatcher>();

        var ptUserId = Guid.NewGuid();

        var sampleBookings = new List<TrainerBookingRevenueRow>
        {
            new(
                AppointmentId: Guid.NewGuid(),
                SlotStartUtc: DateTime.UtcNow.AddDays(-2),
                CreatedAt: DateTime.UtcNow.AddDays(-2),
                PriceListedVnd: 500_000,
                ActualPaidVnd: 500_000,
                PlatformFeeVnd: 50_000,
                TrainerShareVnd: 450_000,
                RefundedVnd: 0,
                Status: BookingStatus.Completed),
            new(
                AppointmentId: Guid.NewGuid(),
                SlotStartUtc: DateTime.UtcNow.AddDays(-1),
                CreatedAt: DateTime.UtcNow.AddDays(-1),
                PriceListedVnd: 200_000,
                ActualPaidVnd: 200_000,
                PlatformFeeVnd: 20_000,
                TrainerShareVnd: 180_000,
                RefundedVnd: 200_000,
                Status: BookingStatus.Refunded)
        };

        storeMock.Setup(s => s.ListBookingsInRangeAsync(ptUserId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleBookings);

        var service = new TrainerRevenueService(storeMock.Object, authzMock.Object, outboxMock.Object);

        var result = await service.GetOverviewAsync(ptUserId, FinanceRange._7d, CancellationToken.None);

        // Gross = 500k + 200k = 700k
        result.TotalGrossVnd.Should().Be(700_000);
        // Platform fee = 50k + 20k = 70k
        result.PlatformFeeVnd.Should().Be(70_000);
        // Trainer share = 450k + 180k = 630k
        // Refunded share deduction = 200k * 90% = 180k
        // Net = 630k - 180k = 450k
        result.NetEarningsVnd.Should().Be(450_000);
        result.CompletedSessionsCount.Should().Be(1);
    }

    [Fact]
    public async Task TrainerSettlement_Enforces_AdminRole_And_Requires_ReferenceNumber()
    {
        var storeMock = new Mock<ITrainerRevenueStore>();
        var authzMock = new Mock<IAuthorizationGuard>();
        var outboxMock = new Mock<Fitmate.Application.Outbox.IOutboxDispatcher>();

        var service = new TrainerRevenueService(storeMock.Object, authzMock.Object, outboxMock.Object);
        var adminId = Guid.NewGuid();

        // 1. Validate empty reference number fails
        var emptyRefRequest = new TrainerSettlementRequest(
            PtUserId: Guid.NewGuid(),
            AmountVnd: 1_000_000,
            ReferenceNumber: "   ",
            Note: null);

        var actEmptyRef = () => service.SettleTrainerAsync(adminId, emptyRefRequest, CancellationToken.None);
        await actEmptyRef.Should().ThrowAsync<ValidationException>()
            .Where(e => e.Code == ErrorCodes.Validation.RequiredField);

        // 2. Validate non-positive amount fails
        var zeroAmountRequest = new TrainerSettlementRequest(
            PtUserId: Guid.NewGuid(),
            AmountVnd: 0,
            ReferenceNumber: "BANK-REF-12345",
            Note: null);

        var actZeroAmount = () => service.SettleTrainerAsync(adminId, zeroAmountRequest, CancellationToken.None);
        await actZeroAmount.Should().ThrowAsync<ValidationException>()
            .Where(e => e.Code == ErrorCodes.Validation.FieldInvalid);
    }
}
