using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using FluentAssertions;

namespace Fitmate.UnitTests;

public sealed class SessionPackageTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);

    private static (Guid HoldId, Guid AppointmentId, DateTime SlotStartUtc)[] Slots(int count) =>
        Enumerable.Range(0, count)
            .Select(i => (Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(2 + i)))
            .ToArray();

    [Theory]
    [InlineData(4, 100_001)]
    [InlineData(8, 100_003)]
    public void CreateHold_PersistsOrderedExactVndAllocations(int count, long price)
    {
        var slots = Slots(count).Reverse().ToArray();
        var package = SessionPackage.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            price, slots, Now, TimeSpan.FromMinutes(10));

        package.Status.Should().Be(SessionPackageStatus.Held);
        package.TotalPriceVnd.Should().Be(price * count);
        package.HoldExpiresAtUtc.Should().Be(Now.AddMinutes(10));
        package.InitialScheduleDeadlineUtc.Should().Be(Now.AddDays(28));
        package.MakeUpDeadlineUtc.Should().Be(Now.AddDays(42));
        package.Sessions.Select(s => s.InitialSlotStartUtc).Should().BeInAscendingOrder();
        package.Sessions.Select(s => s.Sequence).Should().Equal(Enumerable.Range(1, count));
        package.Sessions.Sum(s => s.PaidAllocationVnd).Should().Be(package.TotalPriceVnd);
        package.Sessions.Sum(s => s.PlatformFeeAllocationVnd).Should().Be(
            (long)Math.Round(package.TotalPriceVnd * 0.10m, MidpointRounding.AwayFromZero));
        package.Sessions.Should().OnlyContain(s => s.PaidAllocationVnd ==
            s.PlatformFeeAllocationVnd + s.TrainerShareAllocationVnd);
    }

    [Theory]
    [InlineData(4, 350_001)]
    [InlineData(8, 650_003)]
    public void CreateHold_UsesPtPackagePriceAsImmutableAllocation(int count, long packagePrice)
    {
        var package = SessionPackage.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            100_000, Slots(count), Now, TimeSpan.FromMinutes(10), packagePrice);

        package.TotalPriceVnd.Should().Be(packagePrice);
        package.Sessions.Sum(s => s.PaidAllocationVnd).Should().Be(packagePrice);
        package.Sessions.Sum(s => s.PlatformFeeAllocationVnd).Should().Be(
            (long)Math.Round(packagePrice * 0.10m, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void CreateHold_RejectsPackagePriceAboveSeparateSessions()
    {
        Action attempt = () => SessionPackage.CreateHold(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 100_000, Slots(4), Now, TimeSpan.FromMinutes(10), 400_001);
        attempt.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CreateHold_RejectsInvalidSlotSets()
    {
        var member = Guid.NewGuid();
        var pt = Guid.NewGuid();
        var service = Guid.NewGuid();
        Action wrongCount = () => SessionPackage.CreateHold(member, pt, service, 100_000,
            Slots(3), Now, TimeSpan.FromMinutes(10));
        wrongCount.Should().Throw<ArgumentException>();
        // A copied slot identity must be rejected even when start times differ.
        var repeated = Slots(4);
        repeated[3] = (repeated[3].HoldId, repeated[0].AppointmentId, Now.AddDays(7));
        Action duplicate = () => SessionPackage.CreateHold(member, pt, service, 100_000,
            repeated, Now, TimeSpan.FromMinutes(10));
        duplicate.Should().Throw<ArgumentException>();
        var repeatedHold = Slots(4);
        repeatedHold[3] = (repeatedHold[0].HoldId, repeatedHold[3].AppointmentId, Now.AddDays(7));
        Action duplicateHold = () => SessionPackage.CreateHold(member, pt, service, 100_000,
            repeatedHold, Now, TimeSpan.FromMinutes(10));
        duplicateHold.Should().Throw<ArgumentException>();
        var late = Slots(4);
        late[3] = (late[3].HoldId, late[3].AppointmentId, Now.AddDays(29));
        Action tooLate = () => SessionPackage.CreateHold(member, pt, service, 100_000,
            late, Now, TimeSpan.FromMinutes(10));
        tooLate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PaymentTransition_RequiresExactAmountAndActiveHold()
    {
        var package = SessionPackage.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            250_000, Slots(4), Now, TimeSpan.FromMinutes(10));
        var paymentId = Guid.NewGuid();
        Action expiredAttach = () => package.AttachPayment(paymentId, Now.AddMinutes(10));
        expiredAttach.Should().Throw<InvalidOperationException>();
        package.AttachPayment(paymentId, Now.AddMinutes(5));
        Action pendingMustNotExpire = () => package.Expire(Now.AddMinutes(11));
        pendingMustNotExpire.Should().Throw<InvalidOperationException>();
        Action wrongAmount = () => package.MarkPaid(paymentId, 1, Now.AddMinutes(6));
        wrongAmount.Should().Throw<InvalidOperationException>();
        package.MarkPaid(paymentId, 1_000_000, Now.AddMinutes(6));
        package.Status.Should().Be(SessionPackageStatus.Active);
        Action cannotExpirePaid = () => package.Expire(Now.AddMinutes(11));
        cannotExpirePaid.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OnlinePreparation_BlocksCheckoutUntilReady_AndCanExpireSafely()
    {
        var package = SessionPackage.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            100_000, Slots(4), Now, TimeSpan.FromMinutes(10));
        package.BeginOnlinePreparation(Now.AddMinutes(1));
        package.Status.Should().Be(SessionPackageStatus.OnlinePreparing);
        Action prematurePayment = () => package.AttachPayment(Guid.NewGuid(), Now.AddMinutes(2));
        prematurePayment.Should().Throw<InvalidOperationException>();
        package.CompleteOnlinePreparation(Now.AddMinutes(3));
        package.Status.Should().Be(SessionPackageStatus.Held);

        var stale = SessionPackage.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            100_000, Slots(4), Now, TimeSpan.FromMinutes(10));
        stale.BeginOnlinePreparation(Now.AddMinutes(1));
        Action tooLate = () => stale.CompleteOnlinePreparation(Now.AddMinutes(10));
        tooLate.Should().Throw<InvalidOperationException>();
        stale.Expire(Now.AddMinutes(10));
        stale.Status.Should().Be(SessionPackageStatus.Expired);
        Action afterExpiry = () => stale.AttachPayment(Guid.NewGuid(), Now.AddMinutes(11));
        afterExpiry.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PackagePayment_HasExactlyOneSubject_AndRejectsSingleBookingPath()
    {
        var packageId = Guid.NewGuid();
        var payment = Payment.CreatePendingForPackage(packageId, PaymentMethod.Payos, 800_000);

        payment.PackageId.Should().Be(packageId);
        payment.AppointmentId.Should().BeNull();
        payment.AmountVnd.Should().Be(800_000);
        Action singleBookingPath = () => payment.RequireAppointmentId();
        singleBookingPath.Should().Throw<ConflictException>()
            .Where(e => e.Code == "PACKAGE_PAYMENT_FLOW_REQUIRED");
    }

    [Fact]
    public void StopRequest_FreezesRefundEligibilityAtRequestTime()
    {
        var package = SessionPackage.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            100_001, Slots(4), Now, TimeSpan.FromMinutes(10));
        var paymentId = Guid.NewGuid();
        package.AttachPayment(paymentId, Now.AddMinutes(1));
        package.MarkPaid(paymentId, package.TotalPriceVnd, Now.AddMinutes(2));

        var requestedAt = Now.AddHours(1);
        var sessions = package.Sessions.OrderBy(s => s.Sequence).Select((s, i) =>
            (s.AppointmentId,
             Status: i == 3 ? AppointmentStatus.Completed : AppointmentStatus.Confirmed,
             SlotStartUtc: i == 0 ? requestedAt.AddHours(47) : requestedAt.AddHours(48 + i)))
            .ToArray();
        var stop = package.RequestStop("Tôi cần tạm dừng vì lịch công tác thay đổi.", sessions, requestedAt);

        package.Status.Should().Be(SessionPackageStatus.StopRequested);
        stop.PtResponseDeadlineUtc.Should().Be(requestedAt.AddHours(24));
        stop.Sessions.Should().HaveCount(4);
        stop.Sessions.Select(s => s.EligibleForRefund).Should().Equal(false, true, true, false);
        stop.RefundQuoteVnd.Should().Be(package.Sessions.Skip(1).Take(2).Sum(s => s.PaidAllocationVnd));
        stop.Sessions.Should().OnlyContain(s => s.StopRequestId == stop.Id);
        Action duplicate = () => package.RequestStop("Lý do hợp lệ nhưng lặp lại.", sessions, requestedAt);
        duplicate.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void StopRequest_RejectsIncompleteOrUnpaidSnapshot()
    {
        var package = SessionPackage.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            100_000, Slots(4), Now, TimeSpan.FromMinutes(10));
        var sessions = package.Sessions.Select(s =>
            (s.AppointmentId, AppointmentStatus.Confirmed, s.InitialSlotStartUtc)).ToArray();
        Action unpaid = () => package.RequestStop("Lý do hợp lệ để dừng gói.", sessions, Now);
        unpaid.Should().Throw<InvalidOperationException>();

        var paymentId = Guid.NewGuid();
        package.AttachPayment(paymentId, Now.AddMinutes(1));
        package.MarkPaid(paymentId, package.TotalPriceVnd, Now.AddMinutes(2));
        Action incomplete = () => package.RequestStop("Lý do hợp lệ để dừng gói.", sessions[..3], Now);
        incomplete.Should().Throw<ArgumentException>();
        package.Status.Should().Be(SessionPackageStatus.Active);
    }

    [Fact]
    public void StopRequest_PtAcceptsOrTimeoutApproves_WithoutPtVeto()
    {
        var package = SessionPackage.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            100_000, Slots(4), Now, TimeSpan.FromMinutes(10));
        var paymentId = Guid.NewGuid();
        package.AttachPayment(paymentId, Now.AddMinutes(1));
        package.MarkPaid(paymentId, package.TotalPriceVnd, Now.AddMinutes(2));
        var sessions = package.Sessions.Select(s =>
            (s.AppointmentId, AppointmentStatus.Confirmed, s.InitialSlotStartUtc)).ToArray();
        var request = package.RequestStop("Lịch làm việc của tôi thay đổi dài ngày.", sessions, Now);

        request.ApproveByPt(Now.AddHours(23)).Should().BeTrue();
        request.ApprovalSource.Should().Be(SessionPackageStopApprovalSource.PtAccepted);
        request.ApproveByPt(Now.AddHours(23)).Should().BeFalse();
        Action cannotWithdraw = () => package.WithdrawStopRequest(request.Id, Now.AddHours(23));
        cannotWithdraw.Should().Throw<InvalidOperationException>();

        var other = SessionPackage.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            100_000, Slots(4), Now, TimeSpan.FromMinutes(10));
        var otherPayment = Guid.NewGuid();
        other.AttachPayment(otherPayment, Now.AddMinutes(1));
        other.MarkPaid(otherPayment, other.TotalPriceVnd, Now.AddMinutes(2));
        var otherSessions = other.Sessions.Select(s =>
            (s.AppointmentId, AppointmentStatus.Confirmed, s.InitialSlotStartUtc)).ToArray();
        var timedOut = other.RequestStop("Tôi không thể tiếp tục lịch tập cũ.", otherSessions, Now);
        Action tooEarly = () => timedOut.ApproveAfterTimeout(Now.AddHours(23));
        tooEarly.Should().Throw<InvalidOperationException>();
        timedOut.ApproveAfterTimeout(Now.AddHours(24)).Should().BeTrue();
        timedOut.ApprovalSource.Should().Be(SessionPackageStopApprovalSource.PtResponseTimedOut);
    }

    [Fact]
    public void StopRequest_WithdrawnHistoryAllowsNewRequest()
    {
        var package = SessionPackage.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            100_000, Slots(4), Now, TimeSpan.FromMinutes(10));
        var paymentId = Guid.NewGuid();
        package.AttachPayment(paymentId, Now.AddMinutes(1));
        package.MarkPaid(paymentId, package.TotalPriceVnd, Now.AddMinutes(2));
        var sessions = package.Sessions.Select(s =>
            (s.AppointmentId, AppointmentStatus.Confirmed, s.InitialSlotStartUtc)).ToArray();
        var first = package.RequestStop("Lịch công tác của tôi thay đổi.", sessions, Now);
        package.WithdrawStopRequest(first.Id, Now.AddHours(1));
        first.Status.Should().Be(SessionPackageStopRequestStatus.Withdrawn);
        package.Status.Should().Be(SessionPackageStatus.Active);
        var second = package.RequestStop("Tôi lại có lịch công tác khác.", sessions, Now.AddHours(2));
        second.Id.Should().NotBe(first.Id);
        package.StopRequests.Should().HaveCount(2);
    }
}
