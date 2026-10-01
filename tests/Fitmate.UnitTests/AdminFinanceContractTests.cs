using System.Text.Json;
using Fitmate.Application.Finance;
using FluentAssertions;
using Xunit;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-818: Contract tests for Admin Finance endpoints matching OpenAPI fitmate-v1.yaml
/// </summary>
public class AdminFinanceContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void VerifyLegacyAppointmentRequest_DeserializesFromCamelCase()
    {
        var json = """
        {
            "verifiedCompletedAtUtc": "2026-09-15T10:00:00Z",
            "reason": "Xác minh qua biên bản tập luyện",
            "evidenceReference": "EVID-ATTENDANCE-DOC-001"
        }
        """;

        var req = JsonSerializer.Deserialize<VerifyLegacyAppointmentRequest>(json, JsonOptions);

        req.Should().NotBeNull();
        req!.VerifiedCompletedAtUtc.Should().Be(DateTime.Parse("2026-09-15T10:00:00Z").ToUniversalTime());
        req.Reason.Should().Be("Xác minh qua biên bản tập luyện");
        req.EvidenceReference.Should().Be("EVID-ATTENDANCE-DOC-001");
    }

    [Fact]
    public void LegacyAppointmentReconciliationView_SerializesToCamelCase()
    {
        var id = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var ptUserId = Guid.NewGuid();
        var slotStart = DateTime.UtcNow.AddDays(-5);
        var completedAt = slotStart.AddHours(1);

        var view = new LegacyAppointmentReconciliationView(
            Id: id,
            MemberId: memberId,
            PtUserId: ptUserId,
            SlotStartUtc: slotStart,
            ActualPaidVnd: 500_000,
            PlatformFeeVnd: 50_000,
            TrainerShareVnd: 450_000,
            Status: Fitmate.Domain.Common.AppointmentStatus.Completed,
            LegacyCompletionTimeUnknown: false,
            CompletedAtUtc: completedAt);

        var json = JsonSerializer.Serialize(view, JsonOptions);

        json.Should().Contain("\"id\":");
        json.Should().Contain("\"memberId\":");
        json.Should().Contain("\"ptUserId\":");
        json.Should().Contain("\"slotStartUtc\":");
        json.Should().Contain("\"actualPaidVnd\":500000");
        json.Should().Contain("\"platformFeeVnd\":50000");
        json.Should().Contain("\"trainerShareVnd\":450000");
        json.Should().Contain("\"legacyCompletionTimeUnknown\":false");
        json.Should().Contain("\"completedAtUtc\":");
    }
}
