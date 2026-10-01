using System.Text.Json;
using Fitmate.Api.Auth;
using Fitmate.Api.Booking;
using Fitmate.Application.Booking;
using Fitmate.Domain.Booking;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Fitmate.UnitTests.Contract;

/// <summary>
/// Phase 4 — Booking Hold Wire Contract Tests.
/// Validates request and response wire serialization, OpenAPI schema parity,
/// owner-scoped authorization, and hold active lifecycle invariants.
/// </summary>
public class BookingHoldContractTests
{
    private static readonly JsonSerializerOptions DefaultOptions = new();

    private static List<string> GetPropertyNames(string json)
    {
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
    }

    [Fact]
    public void Serialize_CreateHoldRequest_Produces_Exact_SnakeCase_Keys()
    {
        var request = new CreateHoldRequest(
            PtId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ServiceId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            SlotStartUtc: new DateTime(2026, 10, 2, 2, 0, 0, DateTimeKind.Utc));

        var json = JsonSerializer.Serialize(request, DefaultOptions);
        var keys = GetPropertyNames(json);

        keys.Should().Equal(new[] { "pt_id", "service_id", "slot_start_utc" });
    }

    [Fact]
    public void Deserialize_CreateHoldRequest_Binds_From_SnakeCase_Json()
    {
        var json = """
        {
            "pt_id": "11111111-1111-1111-1111-111111111111",
            "service_id": "22222222-2222-2222-2222-222222222222",
            "slot_start_utc": "2026-10-02T02:00:00Z"
        }
        """;

        var request = JsonSerializer.Deserialize<CreateHoldRequest>(json, DefaultOptions);

        request.Should().NotBeNull();
        request!.PtId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        request.ServiceId.Should().Be(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        request.SlotStartUtc.Should().Be(new DateTime(2026, 10, 2, 2, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Serialize_CreateBookingHoldResponse_Produces_Exact_SnakeCase_Keys()
    {
        var response = new CreateBookingHoldResponse(
            HoldId: Guid.NewGuid(),
            HoldExpiresAt: DateTime.UtcNow.AddMinutes(5),
            AppointmentId: Guid.NewGuid(),
            AppointmentStatus: "Held");

        var json = JsonSerializer.Serialize(response, DefaultOptions);
        var keys = GetPropertyNames(json);

        keys.Should().Equal(new[] { "hold_id", "hold_expires_at", "appointment_id", "appointment_status" });
    }

    [Fact]
    public void Serialize_BookingHoldStatusResponse_Produces_Exact_SnakeCase_Keys()
    {
        var response = new BookingHoldStatusResponse(
            HoldId: Guid.NewGuid(),
            ExpiresAt: DateTime.UtcNow.AddMinutes(5),
            IsActive: true);

        var json = JsonSerializer.Serialize(response, DefaultOptions);
        var keys = GetPropertyNames(json);

        keys.Should().Equal(new[] { "hold_id", "expires_at", "is_active" });
    }

    [Fact]
    public void OpenApi_Schema_CreateBookingHoldResponse_Matches_Exact_Keys()
    {
        var specPath = Path.Combine(AppContext.BaseDirectory, "docs/backend-release/openapi/fitmate-v1.yaml");
        if (!File.Exists(specPath))
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 6 && !File.Exists(specPath); i++)
            {
                dir = Directory.GetParent(dir)?.FullName ?? dir;
                specPath = Path.Combine(dir, "docs/backend-release/openapi/fitmate-v1.yaml");
            }
        }

        var lines = File.ReadAllLines(specPath);
        var inCreateSchema = false;
        var inGetSchema = false;
        var createProps = new List<string>();
        var getProps = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("    CreateBookingHoldResponse:"))
            {
                inCreateSchema = true;
                inGetSchema = false;
                continue;
            }
            if (line.StartsWith("    BookingHoldStatusResponse:"))
            {
                inGetSchema = true;
                inCreateSchema = false;
                continue;
            }
            if (line.StartsWith("    ") && !line.StartsWith("      ") && !line.StartsWith("    CreateBookingHoldResponse") && !line.StartsWith("    BookingHoldStatusResponse"))
            {
                inCreateSchema = false;
                inGetSchema = false;
            }

            if (inCreateSchema && line.StartsWith("        ") && line.EndsWith(":") && !line.Contains("properties:") && !line.Contains("required:"))
            {
                createProps.Add(line.Trim().TrimEnd(':'));
            }
            if (inGetSchema && line.StartsWith("        ") && line.EndsWith(":") && !line.Contains("properties:") && !line.Contains("required:"))
            {
                getProps.Add(line.Trim().TrimEnd(':'));
            }
        }

        createProps.Should().Equal(new[] { "hold_id", "hold_expires_at", "appointment_id", "appointment_status" });
        getProps.Should().Equal(new[] { "hold_id", "expires_at", "is_active" });
    }

    [Fact]
    public async Task Controller_CreateHold_Returns_CreatedAtAction_With_Exact_Wire_Shape()
    {
        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var slot = DateTime.UtcNow.AddDays(1);

        var hold = BookingHold.Create(memberId, ptId, serviceId, slot, TimeSpan.FromMinutes(5));
        var appt = Appointment.CreateHold(memberId, ptId, serviceId, slot, slot, 60, 150_000);

        var bookingMock = new Mock<IBookingService>();
        bookingMock.Setup(b => b.CreateHoldAsync(memberId, ptId, serviceId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((hold, appt));

        var currentMock = new Mock<ICurrentUserAccessor>();
        currentMock.Setup(c => c.GetUserIdOrThrow()).Returns(memberId);

        var controller = new BookingsController(bookingMock.Object);
        var request = new CreateHoldRequest(ptId, serviceId, slot);

        var actionResult = await controller.CreateHold(currentMock.Object, request, CancellationToken.None);

        var created = actionResult.Should().BeOfType<CreatedAtActionResult>().Subject;
        var value = created.Value.Should().BeOfType<CreateBookingHoldResponse>().Subject;

        value.HoldId.Should().Be(hold.Id);
        value.HoldExpiresAt.Should().Be(hold.ExpiresAt);
        value.AppointmentId.Should().Be(appt.Id);
        value.AppointmentStatus.Should().Be("Held");

        var json = JsonSerializer.Serialize(value, DefaultOptions);
        GetPropertyNames(json).Should().Equal(new[] { "hold_id", "hold_expires_at", "appointment_id", "appointment_status" });
    }

    [Fact]
    public async Task Controller_GetHold_OwnerSuccess_Returns_Exact_Wire_Shape()
    {
        var memberId = Guid.NewGuid();
        var hold = BookingHold.Create(memberId, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), TimeSpan.FromMinutes(5));

        var bookingMock = new Mock<IBookingService>();
        var holdStoreMock = new Mock<IBookingHoldStore>();
        holdStoreMock.Setup(s => s.FindByIdAsync(hold.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hold);

        var currentMock = new Mock<ICurrentUserAccessor>();
        currentMock.Setup(c => c.GetUserIdOrThrow()).Returns(memberId);

        var controller = new BookingsController(bookingMock.Object);

        var actionResult = await controller.GetHold(currentMock.Object, hold.Id, holdStoreMock.Object, CancellationToken.None);

        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        var value = okResult.Value.Should().BeOfType<BookingHoldStatusResponse>().Subject;

        value.HoldId.Should().Be(hold.Id);
        value.ExpiresAt.Should().Be(hold.ExpiresAt);
        value.IsActive.Should().BeTrue();

        var json = JsonSerializer.Serialize(value, DefaultOptions);
        GetPropertyNames(json).Should().Equal(new[] { "hold_id", "expires_at", "is_active" });
    }

    [Fact]
    public async Task Controller_GetHold_NonOwner_Returns_Forbid()
    {
        var ownerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var hold = BookingHold.Create(ownerId, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), TimeSpan.FromMinutes(5));

        var bookingMock = new Mock<IBookingService>();
        var holdStoreMock = new Mock<IBookingHoldStore>();
        holdStoreMock.Setup(s => s.FindByIdAsync(hold.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hold);

        var currentMock = new Mock<ICurrentUserAccessor>();
        currentMock.Setup(c => c.GetUserIdOrThrow()).Returns(callerId);

        var controller = new BookingsController(bookingMock.Object);

        var actionResult = await controller.GetHold(currentMock.Object, hold.Id, holdStoreMock.Object, CancellationToken.None);

        actionResult.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Controller_GetHold_ExpiredHold_Reflects_IsActiveFalse()
    {
        var memberId = Guid.NewGuid();
        // Expired hold: TTL negative
        var hold = BookingHold.Create(memberId, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), TimeSpan.FromMinutes(-10));

        var bookingMock = new Mock<IBookingService>();
        var holdStoreMock = new Mock<IBookingHoldStore>();
        holdStoreMock.Setup(s => s.FindByIdAsync(hold.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hold);

        var currentMock = new Mock<ICurrentUserAccessor>();
        currentMock.Setup(c => c.GetUserIdOrThrow()).Returns(memberId);

        var controller = new BookingsController(bookingMock.Object);

        var actionResult = await controller.GetHold(currentMock.Object, hold.Id, holdStoreMock.Object, CancellationToken.None);

        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        var value = okResult.Value.Should().BeOfType<BookingHoldStatusResponse>().Subject;

        value.IsActive.Should().BeFalse();
    }
}
