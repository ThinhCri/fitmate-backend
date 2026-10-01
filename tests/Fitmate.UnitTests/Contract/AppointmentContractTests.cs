using System.Text.Json;
using Fitmate.Application.Booking;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using FluentAssertions;
using Microsoft.OpenApi.Readers;
using Xunit;

namespace Fitmate.UnitTests.Contract;

public class AppointmentContractTests
{
    private static readonly string OpenApiPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../docs/backend-release/openapi/fitmate-v1.yaml"));

    [Fact]
    public void AppointmentResponse_Serializes_WithExactExpectedKeys_AndNoDomainInternals()
    {
        var apptId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var slotUtc = new DateTime(2026, 10, 2, 2, 0, 0, DateTimeKind.Utc);
        var slotLocal = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Unspecified);

        var appt = Appointment.CreateHold(memberId, ptId, serviceId, slotUtc, slotLocal, 60, 100_000);
        appt.AttachPayment(paymentId, 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ForceStatusForTesting(AppointmentStatus.Confirmed);

        var response = AppointmentResponse.FromDomain(appt);
        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string>
        {
            "id",
            "member_id",
            "pt_user_id",
            "service_id",
            "slot_start_utc",
            "slot_start_local",
            "duration_minutes",
            "status",
            "hold_expires_at_utc",
            "acceptance_deadline_utc",
            "confirmed_at_utc",
            "started_at_utc",
            "completed_at_utc",
            "cancelled_at_utc",
            "rescheduled_count",
            "price_listed_vnd",
            "discount_vnd",
            "actual_paid_vnd",
            "platform_fee_rate_bps",
            "platform_fee_vnd",
            "trainer_share_vnd",
            "policy_version",
            "payment_id"
        };

        var actualKeys = root.EnumerateObject().Select(p => p.Name).ToHashSet();
        actualKeys.Should().BeEquivalentTo(expectedKeys);

        // Explicitly assert absence of domain/audit/note keys
        actualKeys.Should().NotContain("note");
        actualKeys.Should().NotContain("Note");
        actualKeys.Should().NotContain("legacy_status");
        actualKeys.Should().NotContain("LegacyStatus");
        actualKeys.Should().NotContain("created_at");
        actualKeys.Should().NotContain("CreatedAt");
        actualKeys.Should().NotContain("updated_at");
        actualKeys.Should().NotContain("UpdatedAt");
        actualKeys.Should().NotContain("payment_status");
        actualKeys.Should().NotContain("is_late_attendee");
        actualKeys.Should().NotContain("previous_booking_id");
        actualKeys.Should().NotContain("pt_id"); // canonical is pt_user_id

        // Values check
        root.GetProperty("status").GetString().Should().Be("Confirmed");
        root.GetProperty("platform_fee_rate_bps").GetInt32().Should().Be(1000);
        root.GetProperty("payment_id").GetString().Should().Be(paymentId.ToString());
    }

    [Theory]
    [InlineData(0.10, 1000)]
    [InlineData(0.155, 1550)]
    [InlineData(0.00, 0)]
    [InlineData(1.00, 10000)]
    public void AppointmentResponse_ConvertsPlatformFeeRate_ToBasisPoints_Accurately(decimal rate, int expectedBps)
    {
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, rate, (long)(100_000 * rate), (long)(100_000 * (1 - rate)), "v1");

        var response = AppointmentResponse.FromDomain(appt);
        response.PlatformFeeRateBps.Should().Be(expectedBps);
    }

    [Fact]
    public void AppointmentPageResponse_Serializes_WithExactExpectedKeys()
    {
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);
        var page = new AppointmentPageResponse(
            Items: new[] { AppointmentResponse.FromDomain(appt) },
            Total: 1,
            Page: 1,
            PageSize: 25
        );

        var json = JsonSerializer.Serialize(page);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string> { "items", "total", "page", "pageSize" };
        var actualKeys = root.EnumerateObject().Select(p => p.Name).ToHashSet();
        actualKeys.Should().BeEquivalentTo(expectedKeys);
        root.GetProperty("pageSize").GetInt32().Should().Be(25);
    }

    [Fact]
    public void OpenApi_AppointmentSchema_MatchesWirePropertiesExactly()
    {
        File.Exists(OpenApiPath).Should().BeTrue();
        using var stream = File.OpenRead(OpenApiPath);
        var reader = new OpenApiStreamReader();
        var doc = reader.Read(stream, out var diagnostic);
        diagnostic.Errors.Should().BeEmpty();

        doc.Components.Schemas.Should().ContainKey("Appointment");
        var schema = doc.Components.Schemas["Appointment"];

        var expectedSchemaProperties = new HashSet<string>
        {
            "id",
            "member_id",
            "pt_user_id",
            "service_id",
            "slot_start_utc",
            "slot_start_local",
            "duration_minutes",
            "status",
            "hold_expires_at_utc",
            "acceptance_deadline_utc",
            "confirmed_at_utc",
            "started_at_utc",
            "completed_at_utc",
            "cancelled_at_utc",
            "rescheduled_count",
            "price_listed_vnd",
            "discount_vnd",
            "actual_paid_vnd",
            "platform_fee_rate_bps",
            "platform_fee_vnd",
            "trainer_share_vnd",
            "policy_version",
            "payment_id"
        };

        var actualSchemaProperties = schema.Properties.Keys.ToHashSet();
        actualSchemaProperties.Should().BeEquivalentTo(expectedSchemaProperties);

        // Ensure removed phantom fields are not present
        actualSchemaProperties.Should().NotContain("note");
        actualSchemaProperties.Should().NotContain("payment_status");
        actualSchemaProperties.Should().NotContain("is_late_attendee");
        actualSchemaProperties.Should().NotContain("previous_booking_id");
        actualSchemaProperties.Should().NotContain("pt_id");
    }
}
