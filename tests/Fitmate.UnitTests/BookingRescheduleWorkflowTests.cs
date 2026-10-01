using System;
using Fitmate.Api.Booking;
using Fitmate.Application.Booking;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Fitmate.UnitTests;

public class BookingRescheduleWorkflowTests
{
    [Fact]
    public void RescheduleLegacy_Returns410Gone_WithRetiredErrorCode()
    {
        var bookingMock = new Mock<IBookingService>();
        var controller = new BookingsController(bookingMock.Object);

        var result = controller.RescheduleLegacy(Guid.NewGuid());

        var objectResult = result as ObjectResult;
        objectResult.Should().NotBeNull();
        objectResult!.StatusCode.Should().Be(StatusCodes.Status410Gone);

        var problem = objectResult.Value as ProblemDetails;
        problem.Should().NotBeNull();
        problem!.Extensions.Should().ContainKey("code");
        problem.Extensions["code"].Should().Be("BOOKING_RESCHEDULE_ENDPOINT_RETIRED");
    }

    [Fact]
    public void TwoPartyConsent_PreservesOriginalAppointmentIdAndSlot_UntilExplicitAcceptance()
    {
        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var originalSlot = DateTime.UtcNow.AddDays(2);

        var appt = Appointment.CreateHold(memberId, ptId, serviceId, originalSlot, originalSlot, 60, 200_000);
        appt.AttachPayment(Guid.NewGuid(), 200_000, 0, 0.10m, 20_000, 180_000, "v1");
        appt.ForceStatusForTesting(AppointmentStatus.Confirmed);

        var originalApptId = appt.Id;
        var originalSlotStart = appt.SlotStartUtc;

        // When a reschedule is requested under Branch A:
        var newSlot = originalSlot.AddHours(2);
        var deadline = DateTime.UtcNow.AddHours(24);
        var request = BookingRescheduleRequest.Create(
            appt.Id, memberId, appt.SlotStartUtc, newSlot, appt.DurationMinutes, "Need later slot", deadline);

        // The request is Pending and appointment is unchanged
        request.Status.Should().Be(RescheduleRequestStatus.Pending);
        appt.Id.Should().Be(originalApptId, "Appointment ID must never be changed during reschedule flow");
        appt.SlotStartUtc.Should().Be(originalSlotStart, "Slot must not change until PT accepts");
        appt.Status.Should().Be(AppointmentStatus.Confirmed, "Appointment must remain confirmed until accepted");
    }
}
