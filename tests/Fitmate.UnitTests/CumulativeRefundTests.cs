using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fitmate.Application.Auth;
using Fitmate.Application.Booking;
using Fitmate.Application.Common;
using Fitmate.Application.Finance;
using Fitmate.Application.Outbox;
using Fitmate.Application.Payments;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Finance;
using Fitmate.Domain.Payment;
using FluentAssertions;
using Moq;
using Xunit;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-810: Kiểm thử nghiệp vụ hoàn tiền lũy kế (cumulative refund) và cancellation hỗ trợ PartialRefunded.
/// </summary>
public class CumulativeRefundTests
{
    private sealed class CapturingOutbox : IOutboxDispatcher
    {
        public List<(string Type, object Payload)> Messages { get; } = new();
        public void Enqueue(string type, object payload) => Messages.Add((type, payload));
    }

    [Fact]
    public void CumulativeFeeDelta_MultiStepRefunds_SumsToExactOriginalSnapshot()
    {
        long actualPaidVnd = 500_000;
        long originalPlatformFeeVnd = 50_000;
        long originalTrainerShareVnd = 450_000;

        // Step 1: Refund 150,000
        long refund1 = 150_000;
        long cumRefund1 = refund1;
        long fee1 = (long)Math.Round(originalPlatformFeeVnd * (decimal)cumRefund1 / actualPaidVnd, MidpointRounding.AwayFromZero);
        long trainer1 = refund1 - fee1;

        fee1.Should().Be(15_000);
        trainer1.Should().Be(135_000);

        // Step 2: Refund remaining 350,000
        long refund2 = 350_000;
        long cumRefund2 = cumRefund1 + refund2;
        long cumFee2 = (long)Math.Round(originalPlatformFeeVnd * (decimal)cumRefund2 / actualPaidVnd, MidpointRounding.AwayFromZero);
        long fee2 = cumFee2 - fee1;
        long trainer2 = refund2 - fee2;

        fee2.Should().Be(35_000);
        trainer2.Should().Be(315_000);

        // Verify total sums match exact original values with zero drift
        (fee1 + fee2).Should().Be(originalPlatformFeeVnd);
        (trainer1 + trainer2).Should().Be(originalTrainerShareVnd);
        (refund1 + refund2).Should().Be(actualPaidVnd);
    }

    [Fact]
    public async Task CancelAsync_WhenPaymentIsPaid_CreatesFullRefundIntent()
    {
        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        var appt = Appointment.CreateHold(memberId, ptId, serviceId, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(7), 60, 500_000);
        var payment = Payment.CreatePending(appt.Id, PaymentMethod.Payos, 500_000, null);
        payment.MarkPaid("TXN-PAID-1");
        appt.AttachPayment(payment.Id, 500_000, 0, 0.10m, 50_000, 450_000, "v1");

        var apptStoreMock = new Mock<IAppointmentStore>();
        apptStoreMock.Setup(s => s.FindByIdAsync(appt.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appt);
        apptStoreMock.Setup(s => s.ExecuteWithScheduleLockAsync(It.IsAny<Guid>(), It.IsAny<Func<Task<Appointment>>>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, Func<Task<Appointment>>, CancellationToken>((_, func, _) => func());

        var addedRefunds = new List<Refund>();
        var paymentStoreMock = new Mock<IPaymentStore>();
        paymentStoreMock.Setup(s => s.FindByIdForUpdateAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        paymentStoreMock.Setup(s => s.GetPendingRefundsTotalAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0L);
        paymentStoreMock.Setup(s => s.AddRefund(It.IsAny<Refund>())).Callback<Refund>(r => addedRefunds.Add(r));

        var outbox = new CapturingOutbox();
        var authzMock = new Mock<IAuthorizationGuard>();
        var holdStoreMock = new Mock<IBookingHoldStore>();
        var serviceLookupMock = new Mock<IServiceBookingLookup>();

        var bookingService = new BookingService(
            apptStoreMock.Object,
            holdStoreMock.Object,
            serviceLookupMock.Object,
            outbox,
            authzMock.Object,
            payments: paymentStoreMock.Object);

        var result = await bookingService.CancelAsync(memberId, appt.Id, "Member bận việc gia đình", CancellationToken.None);

        result.Status.Should().Be(AppointmentStatus.Cancelled);
        addedRefunds.Should().HaveCount(1);
        addedRefunds[0].AmountVnd.Should().Be(500_000);
        addedRefunds[0].PaymentId.Should().Be(payment.Id);
        addedRefunds[0].Status.Should().Be(RefundStatus.Pending);

        outbox.Messages.Should().Contain(m => m.Type == "payment.refund_requested");
        outbox.Messages.Should().Contain(m => m.Type == "booking.cancelled");
    }

    [Fact]
    public async Task CancelAsync_WhenPaymentIsPartialRefunded_CreatesRefundForRemainingBalance()
    {
        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        var appt = Appointment.CreateHold(memberId, ptId, serviceId, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(7), 60, 500_000);
        var payment = Payment.CreatePending(appt.Id, PaymentMethod.Payos, 500_000, null);
        payment.MarkPaid("TXN-PAID-2");
        payment.ApplyRefund(200_000); // 200k already refunded
        payment.Status.Should().Be(PaymentStatus.PartialRefunded);
        appt.AttachPayment(payment.Id, 500_000, 0, 0.10m, 50_000, 450_000, "v1");

        var apptStoreMock = new Mock<IAppointmentStore>();
        apptStoreMock.Setup(s => s.FindByIdAsync(appt.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appt);
        apptStoreMock.Setup(s => s.ExecuteWithScheduleLockAsync(It.IsAny<Guid>(), It.IsAny<Func<Task<Appointment>>>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, Func<Task<Appointment>>, CancellationToken>((_, func, _) => func());

        var addedRefunds = new List<Refund>();
        var paymentStoreMock = new Mock<IPaymentStore>();
        paymentStoreMock.Setup(s => s.FindByIdForUpdateAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        paymentStoreMock.Setup(s => s.GetPendingRefundsTotalAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(50_000L); // 50k already pending
        paymentStoreMock.Setup(s => s.AddRefund(It.IsAny<Refund>())).Callback<Refund>(r => addedRefunds.Add(r));

        var outbox = new CapturingOutbox();
        var authzMock = new Mock<IAuthorizationGuard>();
        var holdStoreMock = new Mock<IBookingHoldStore>();
        var serviceLookupMock = new Mock<IServiceBookingLookup>();

        var bookingService = new BookingService(
            apptStoreMock.Object,
            holdStoreMock.Object,
            serviceLookupMock.Object,
            outbox,
            authzMock.Object,
            payments: paymentStoreMock.Object);

        // Remaining = 500,000 - 200,000 (refunded so far) - 50,000 (pending) = 250,000
        var result = await bookingService.CancelAsync(memberId, appt.Id, "Member hủy trước giờ tập", CancellationToken.None);

        result.Status.Should().Be(AppointmentStatus.Cancelled);
        addedRefunds.Should().HaveCount(1);
        addedRefunds[0].AmountVnd.Should().Be(250_000);
        addedRefunds[0].PaymentId.Should().Be(payment.Id);
        addedRefunds[0].Status.Should().Be(RefundStatus.Pending);
    }
}
