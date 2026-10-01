using System.Text.Json;
using Fitmate.Api.Auth;
using Fitmate.Api.Booking;
using Fitmate.Application.Booking;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Readers;
using Moq;
using Xunit;

namespace Fitmate.UnitTests.Contract;

public class BookingRescheduleContractTests
{
    private static readonly string OpenApiPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../docs/backend-release/openapi/fitmate-v1.yaml"));

    [Fact]
    public void BookingRescheduleResponse_Serializes_WithExactExpectedKeys_AndNoCamelCase()
    {
        var apptId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var oldSlot = new DateTime(2026, 10, 2, 2, 0, 0, DateTimeKind.Utc);
        var newSlot = new DateTime(2026, 10, 3, 2, 0, 0, DateTimeKind.Utc);
        var deadline = new DateTime(2026, 10, 2, 14, 0, 0, DateTimeKind.Utc);

        var req = BookingRescheduleRequest.Create(apptId, requesterId, oldSlot, newSlot, 60, "Work schedule conflict", deadline);
        var response = BookingRescheduleResponse.FromDomain(req);

        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string>
        {
            "id",
            "appointment_id",
            "requested_by_user_id",
            "old_slot_start_utc",
            "new_slot_start_utc",
            "duration_minutes",
            "status",
            "reason",
            "deadline_utc",
            "created_at_utc",
            "responded_at_utc",
            "rejection_reason"
        };

        var actualKeys = root.EnumerateObject().Select(p => p.Name).ToHashSet();
        actualKeys.Should().BeEquivalentTo(expectedKeys);

        // Explicitly assert absence of camelCase and internal audit keys
        actualKeys.Should().NotContain("appointmentId");
        actualKeys.Should().NotContain("requestedByUserId");
        actualKeys.Should().NotContain("oldSlotStartUtc");
        actualKeys.Should().NotContain("newSlotStartUtc");
        actualKeys.Should().NotContain("durationMinutes");
        actualKeys.Should().NotContain("deadlineUtc");
        actualKeys.Should().NotContain("createdAtUtc");
        actualKeys.Should().NotContain("respondedAtUtc");
        actualKeys.Should().NotContain("rejectionReason");
        actualKeys.Should().NotContain("IsDeleted");
        actualKeys.Should().NotContain("RowVersion");

        root.GetProperty("status").GetString().Should().Be("Pending");
        root.GetProperty("appointment_id").GetString().Should().Be(apptId.ToString());
    }

    [Fact]
    public void CreateRescheduleRequestBody_Deserializes_FromSnakeCase()
    {
        var json = """
        {
            "new_slot_start_utc": "2026-10-03T02:00:00Z",
            "reason": "Need later session"
        }
        """;

        var body = JsonSerializer.Deserialize<CreateRescheduleRequestBody>(json);
        body.Should().NotBeNull();
        body!.NewSlotStartUtc.Should().Be(DateTime.Parse("2026-10-03T02:00:00Z").ToUniversalTime());
        body.Reason.Should().Be("Need later session");
    }

    [Fact]
    public void RejectRescheduleRequestBody_Deserializes_FromSnakeCase()
    {
        var json = """
        {
            "rejection_reason": "Not available at this time"
        }
        """;

        var body = JsonSerializer.Deserialize<RejectRescheduleRequestBody>(json);
        body.Should().NotBeNull();
        body!.RejectionReason.Should().Be("Not available at this time");
    }

    [Fact]
    public void OpenApi_RescheduleSchemas_MatchWirePropertiesExactly()
    {
        File.Exists(OpenApiPath).Should().BeTrue();
        using var stream = File.OpenRead(OpenApiPath);
        var reader = new OpenApiStreamReader();
        var doc = reader.Read(stream, out var diagnostic);
        diagnostic.Errors.Should().BeEmpty();

        doc.Components.Schemas.Should().ContainKey("CreateRescheduleRequestBody");
        var createSchema = doc.Components.Schemas["CreateRescheduleRequestBody"];
        createSchema.Properties.Keys.Should().BeEquivalentTo(new[] { "new_slot_start_utc", "reason" });

        doc.Components.Schemas.Should().ContainKey("RejectRescheduleRequestBody");
        var rejectSchema = doc.Components.Schemas["RejectRescheduleRequestBody"];
        rejectSchema.Properties.Keys.Should().BeEquivalentTo(new[] { "rejection_reason" });

        doc.Components.Schemas.Should().ContainKey("BookingRescheduleResponse");
        var resSchema = doc.Components.Schemas["BookingRescheduleResponse"];
        var expectedResProperties = new HashSet<string>
        {
            "id",
            "appointment_id",
            "requested_by_user_id",
            "old_slot_start_utc",
            "new_slot_start_utc",
            "duration_minutes",
            "status",
            "reason",
            "deadline_utc",
            "created_at_utc",
            "responded_at_utc",
            "rejection_reason"
        };
        resSchema.Properties.Keys.Should().BeEquivalentTo(expectedResProperties);
    }

    [Fact]
    public async Task CreateRescheduleRequest_Returns200_WithBookingRescheduleResponse()
    {
        var bookingMock = new Mock<IBookingService>();
        var rescheduleMock = new Mock<IBookingRescheduleService>();
        var userMock = new Mock<ICurrentUserAccessor>();
        var userId = Guid.NewGuid();
        var apptId = Guid.NewGuid();
        userMock.Setup(u => u.GetUserIdOrThrow()).Returns(userId);

        var req = BookingRescheduleRequest.Create(
            apptId, userId, DateTime.UtcNow, DateTime.UtcNow.AddDays(1), 60, "Schedule conflict", DateTime.UtcNow.AddHours(24));
        rescheduleMock.Setup(s => s.CreateRequestAsync(userId, apptId, It.IsAny<DateTime>(), "Schedule conflict", It.IsAny<CancellationToken>()))
            .ReturnsAsync(req);

        var controller = new BookingsController(bookingMock.Object);
        var body = new CreateRescheduleRequestBody(DateTime.UtcNow.AddDays(1), "Schedule conflict");

        var result = await controller.CreateRescheduleRequest(userMock.Object, rescheduleMock.Object, apptId, body, CancellationToken.None);

        var ok = result as OkObjectResult;
        ok.Should().NotBeNull();
        ok!.StatusCode.Should().Be(StatusCodes.Status200OK);
        var wireRes = ok.Value as BookingRescheduleResponse;
        wireRes.Should().NotBeNull();
        wireRes!.AppointmentId.Should().Be(apptId);
        wireRes.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task AcceptRescheduleRequest_Returns200_WithBookingRescheduleResponse()
    {
        var bookingMock = new Mock<IBookingService>();
        var rescheduleMock = new Mock<IBookingRescheduleService>();
        var userMock = new Mock<ICurrentUserAccessor>();
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        userMock.Setup(u => u.GetUserIdOrThrow()).Returns(userId);

        var req = BookingRescheduleRequest.Create(
            Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddDays(1), 60, "Schedule conflict", DateTime.UtcNow.AddHours(24));
        req.Accept(DateTime.UtcNow);
        rescheduleMock.Setup(s => s.AcceptRequestAsync(userId, requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(req);

        var controller = new BookingsController(bookingMock.Object);
        var result = await controller.AcceptRescheduleRequest(userMock.Object, rescheduleMock.Object, requestId, CancellationToken.None);

        var ok = result as OkObjectResult;
        ok.Should().NotBeNull();
        var wireRes = ok!.Value as BookingRescheduleResponse;
        wireRes.Should().NotBeNull();
        wireRes!.Status.Should().Be("Accepted");
    }

    [Fact]
    public async Task RejectRescheduleRequest_Returns200_WithBookingRescheduleResponse()
    {
        var bookingMock = new Mock<IBookingService>();
        var rescheduleMock = new Mock<IBookingRescheduleService>();
        var userMock = new Mock<ICurrentUserAccessor>();
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        userMock.Setup(u => u.GetUserIdOrThrow()).Returns(userId);

        var req = BookingRescheduleRequest.Create(
            Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddDays(1), 60, "Schedule conflict", DateTime.UtcNow.AddHours(24));
        req.Reject("Conflict", DateTime.UtcNow);
        rescheduleMock.Setup(s => s.RejectRequestAsync(userId, requestId, "Conflict", It.IsAny<CancellationToken>()))
            .ReturnsAsync(req);

        var controller = new BookingsController(bookingMock.Object);
        var body = new RejectRescheduleRequestBody("Conflict");
        var result = await controller.RejectRescheduleRequest(userMock.Object, rescheduleMock.Object, requestId, body, CancellationToken.None);

        var ok = result as OkObjectResult;
        ok.Should().NotBeNull();
        var wireRes = ok!.Value as BookingRescheduleResponse;
        wireRes.Should().NotBeNull();
        wireRes!.Status.Should().Be("Rejected");
        wireRes.RejectionReason.Should().Be("Conflict");
    }

    [Fact]
    public async Task CancelRescheduleRequest_Returns200_WithBookingRescheduleResponse()
    {
        var bookingMock = new Mock<IBookingService>();
        var rescheduleMock = new Mock<IBookingRescheduleService>();
        var userMock = new Mock<ICurrentUserAccessor>();
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        userMock.Setup(u => u.GetUserIdOrThrow()).Returns(userId);

        var req = BookingRescheduleRequest.Create(
            Guid.NewGuid(), userId, DateTime.UtcNow, DateTime.UtcNow.AddDays(1), 60, "Schedule conflict", DateTime.UtcNow.AddHours(24));
        req.Cancel(DateTime.UtcNow);
        rescheduleMock.Setup(s => s.CancelRequestAsync(userId, requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(req);

        var controller = new BookingsController(bookingMock.Object);
        var result = await controller.CancelRescheduleRequest(userMock.Object, rescheduleMock.Object, requestId, CancellationToken.None);

        var ok = result as OkObjectResult;
        ok.Should().NotBeNull();
        var wireRes = ok!.Value as BookingRescheduleResponse;
        wireRes.Should().NotBeNull();
        wireRes!.Status.Should().Be("Cancelled");
    }

    [Fact]
    public void RescheduleLegacy_Returns410Gone_WithAccurateReplacementRoute()
    {
        var bookingMock = new Mock<IBookingService>();
        var controller = new BookingsController(bookingMock.Object);

        var result = controller.RescheduleLegacy(Guid.NewGuid());

        var objectResult = result as ObjectResult;
        objectResult.Should().NotBeNull();
        objectResult!.StatusCode.Should().Be(StatusCodes.Status410Gone);

        var problem = objectResult.Value as ProblemDetails;
        problem.Should().NotBeNull();
        problem!.Detail.Should().Contain("POST /api/v1/bookings/{id}/reschedule-requests");
    }
}
