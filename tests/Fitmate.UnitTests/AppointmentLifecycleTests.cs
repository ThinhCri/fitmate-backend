using FluentAssertions;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Xunit;

namespace Fitmate.UnitTests;

public class AppointmentLifecycleTests
{
    [Fact]
    public void CreateHold_PaymentId_Is_Null_And_Status_Is_Held()
    {
        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var slot = DateTime.UtcNow.AddHours(3);

        var appt = Appointment.CreateHold(memberId, ptId, serviceId, slot, slot, 60, 200_000);

        appt.Status.Should().Be(AppointmentStatus.Held);
        appt.PaymentId.Should().BeNull();
        appt.MemberId.Should().Be(memberId);
        appt.PtUserId.Should().Be(ptId);
        appt.ServiceId.Should().Be(serviceId);
        appt.PriceListedVnd.Should().Be(200_000);
        appt.DurationMinutes.Should().Be(60);
        appt.HoldExpiresAtUtc.Should().NotBeNull();
        appt.HoldExpiresAtUtc!.Value.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(10), TimeSpan.FromSeconds(5));
        appt.CompletedAtUtc.Should().BeNull();
        appt.LegacyCompletionTimeUnknown.Should().BeFalse();
    }

    [Fact]
    public void AttachPayment_Moves_Held_To_Pending_And_Calculates_AcceptanceDeadline()
    {
        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var slot = DateTime.UtcNow.AddHours(5); // slot_start is 5h in future
        var appt = Appointment.CreateHold(memberId, ptId, Guid.NewGuid(), slot, slot, 60, 300_000);
        var paymentId = Guid.NewGuid();

        // paid_at + 24h vs slot_start (5h) -> min is slot_start
        appt.AttachPayment(paymentId, 300_000, 0, 0.10m, 30_000, 270_000, "v1");

        appt.Status.Should().Be(AppointmentStatus.Pending);
        appt.PaymentId.Should().Be(paymentId);
        appt.ActualPaidVnd.Should().Be(300_000);
        appt.PlatformFeeVnd.Should().Be(30_000);
        appt.TrainerShareVnd.Should().Be(270_000);
        appt.AcceptanceDeadlineUtc.Should().Be(slot);
    }

    [Fact]
    public void AttachPayment_When_Slot_Is_Days_Away_AcceptanceDeadline_Is_24h_After_Payment()
    {
        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var slot = DateTime.UtcNow.AddDays(5); // slot_start is 5 days away
        var appt = Appointment.CreateHold(memberId, ptId, Guid.NewGuid(), slot, slot, 60, 300_000);
        var paymentId = Guid.NewGuid();

        var beforePayment = DateTime.UtcNow;
        appt.AttachPayment(paymentId, 300_000, 0, 0.10m, 30_000, 270_000, "v1");
        var afterPayment = DateTime.UtcNow;

        appt.Status.Should().Be(AppointmentStatus.Pending);
        appt.AcceptanceDeadlineUtc.Should().NotBeNull();
        appt.AcceptanceDeadlineUtc!.Value.Should().BeOnOrAfter(beforePayment.AddHours(24));
        appt.AcceptanceDeadlineUtc!.Value.Should().BeOnOrBefore(afterPayment.AddHours(24));
    }

    [Fact]
    public void ConfirmByPt_Transitions_Pending_To_Confirmed()
    {
        var slot = DateTime.UtcNow.AddHours(2);
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), slot, slot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");

        appt.ConfirmByPt();

        appt.Status.Should().Be(AppointmentStatus.Confirmed);
        appt.ConfirmedAtUtc.Should().NotBeNull();
        appt.ConfirmedAtUtc!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ConfirmByPt_Throws_When_AcceptanceDeadline_Expired()
    {
        var slot = DateTime.UtcNow.AddMinutes(-10);
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), slot, slot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");

        var act = () => appt.ConfirmByPt();
        act.Should().Throw<ConflictException>()
            .Where(e => e.Code == "ACCEPTANCE_DEADLINE_EXPIRED");
    }

    [Fact]
    public void StartByPt_Transitions_Confirmed_To_InProgress()
    {
        var slot = DateTime.UtcNow.AddMinutes(-2); // within 5 minutes before slot or after slot start
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), slot, slot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ForceStatusForTesting(AppointmentStatus.Confirmed);

        appt.StartByPt();

        appt.Status.Should().Be(AppointmentStatus.InProgress);
        appt.StartedAtUtc.Should().NotBeNull();
        appt.StartedAtUtc!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void StartByPt_Throws_When_Called_Earlier_Than_5_Minutes_Before_Slot()
    {
        var slot = DateTime.UtcNow.AddMinutes(30); // 30 minutes in future (> 5 min before start)
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), slot, slot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ConfirmByPt();

        var act = () => appt.StartByPt();
        act.Should().Throw<ConflictException>()
            .Where(e => e.Code == ErrorCodes.Booking.CannotTransition);
    }

    [Fact]
    public void CompleteByPt_Transitions_To_AwaitingMemberConfirmation_When_Slot_Has_Ended()
    {
        var slot = DateTime.UtcNow.AddMinutes(-70); // 60 min session ended 10 min ago
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), slot, slot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ForceStatusForTesting(AppointmentStatus.InProgress);

        var completedTime = DateTime.UtcNow;
        appt.CompleteByPt(completedTime);

        appt.Status.Should().Be(AppointmentStatus.AwaitingMemberConfirmation);
        appt.CompletedAtUtc.Should().BeNull();
        appt.CompletionSubmittedAtUtc.Should().Be(completedTime);
        appt.MemberConfirmationDeadlineUtc.Should().Be(completedTime.AddHours(24));
        appt.LegacyCompletionTimeUnknown.Should().BeFalse();
    }

    [Fact]
    public void CompleteByPt_Throws_When_Called_Before_Slot_End()
    {
        var slot = DateTime.UtcNow.AddMinutes(-30); // 60 min session ends in 30 minutes
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), slot, slot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ForceStatusForTesting(AppointmentStatus.InProgress);

        var act = () => appt.CompleteByPt();
        act.Should().Throw<ConflictException>()
            .Where(e => e.Code == ErrorCodes.Booking.CannotTransition);
    }

    [Fact]
    public void Cancel_Transitions_To_Cancelled_And_Records_Reason()
    {
        var slot = DateTime.UtcNow.AddHours(2);
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), slot, slot, 60, 100_000);

        appt.Cancel("Member emergency cancellation");

        appt.Status.Should().Be(AppointmentStatus.Cancelled);
        appt.CancelledAtUtc.Should().NotBeNull();
        appt.CancelledAtUtc!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        appt.Note.Should().Be("Member emergency cancellation");
    }

    [Fact]
    public void Cancel_Throws_When_Already_Completed()
    {
        var slot = DateTime.UtcNow.AddMinutes(-70);
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), slot, slot, 60, 100_000);
        appt.ForceStatusForTesting(AppointmentStatus.Completed);

        var act = () => appt.Cancel("Try cancel completed");
        act.Should().Throw<ConflictException>()
            .Where(e => e.Code == ErrorCodes.Booking.CannotTransition);
    }

    [Fact]
    public void MemberConfirmation_Completes_AwaitingBooking()
    {
        var slot = DateTime.UtcNow.AddMinutes(-70);
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), slot, slot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ForceStatusForTesting(AppointmentStatus.InProgress);
        appt.CompleteByPt();

        appt.ConfirmCompletionByMember();

        appt.Status.Should().Be(AppointmentStatus.Completed);
        appt.CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void MemberDispute_BlocksCompletion()
    {
        var slot = DateTime.UtcNow.AddMinutes(-70);
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), slot, slot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ForceStatusForTesting(AppointmentStatus.InProgress);
        appt.CompleteByPt();

        appt.DisputeCompletion("PT kết thúc buổi tập nhưng không tham gia.");

        appt.Status.Should().Be(AppointmentStatus.Disputed);
        appt.DisputedAtUtc.Should().NotBeNull();
        appt.CompletedAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData(BookingStatus.Held, AppointmentStatus.Held)]
    [InlineData(BookingStatus.AwaitingPayment, AppointmentStatus.Held)]
    [InlineData(BookingStatus.Paid, AppointmentStatus.Pending)]
    [InlineData(BookingStatus.Completed, AppointmentStatus.Completed)]
    [InlineData(BookingStatus.Cancelled, AppointmentStatus.Cancelled)]
    [InlineData(BookingStatus.Refunded, AppointmentStatus.Cancelled)]
    [InlineData(BookingStatus.Expired, AppointmentStatus.Expired)]
    [InlineData(BookingStatus.Confirmed, AppointmentStatus.Confirmed)]
    [InlineData(BookingStatus.InProgress, AppointmentStatus.InProgress)]
    public void MapLegacyToAppointmentStatus_Maps_Accurately(BookingStatus legacy, AppointmentStatus expected)
    {
        Appointment.MapLegacyToAppointmentStatus(legacy).Should().Be(expected);
    }

    [Fact]
    public void CompleteByPt_Allows_Confirmed_Booking_After_Slot_End()
    {
        var slot = DateTime.UtcNow.AddMinutes(-90);
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), slot, slot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ForceStatusForTesting(AppointmentStatus.Confirmed);

        appt.CompleteByPt();

        appt.Status.Should().Be(AppointmentStatus.AwaitingMemberConfirmation);
        appt.CompletionSubmittedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void ConfirmCompletionByMember_Allows_Confirmed_Booking_After_Slot_End_Even_Without_Pt_Start()
    {
        var slot = DateTime.UtcNow.AddHours(-3);
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), slot, slot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ForceStatusForTesting(AppointmentStatus.Confirmed);

        appt.ConfirmCompletionByMember();

        appt.Status.Should().Be(AppointmentStatus.Completed);
        appt.CompletedAtUtc.Should().NotBeNull();
        appt.CompletionSubmittedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void ConfirmCompletionByMember_Throws_For_Confirmed_Booking_Before_Slot_End()
    {
        var slot = DateTime.UtcNow.AddHours(2);
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), slot, slot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ForceStatusForTesting(AppointmentStatus.Confirmed);

        var act = () => appt.ConfirmCompletionByMember();

        act.Should().Throw<ConflictException>().Where(e => e.Code == ErrorCodes.Booking.CannotTransition);
    }
}
