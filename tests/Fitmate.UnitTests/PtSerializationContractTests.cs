using System.Text.Json;
using Fitmate.Api.Trainers;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// Verifies JSON contract and serialization matching OpenAPI specs for PT endpoints (BE-801):
/// - ServiceResponse, CreateServiceRequest, UpdateServiceRequest
/// - AvailabilitySlotResponse, SetAvailabilityRequest
/// - CertificateResponse, CertificatePageResponse
/// </summary>
public class PtSerializationContractTests
{
    [Fact]
    public void ServiceResponse_SerializesToSnakeCase()
    {
        var id = Guid.NewGuid();
        var response = new ServiceResponse(
            Id: id,
            Name: "1-on-1 Personal Training",
            Description: "Intensive workout session",
            PriceVnd: 500000,
            Package4PriceVnd: 1_800_000,
            Package8PriceVnd: 3_400_000,
            DurationMinutes: 60,
            IsActive: true,
            ServiceKind: "one_to_one",
            DeliveryMode: "in_person",
            TargetAudience: "People new to exercise",
            RequiredEquipment: "Training shoes",
            Deliverable: "Technique coaching",
            LocationName: "Fit Gym",
            LocationAddress: "1 Nguyen Hue, HCMC");

        var json = JsonSerializer.Serialize(response);

        json.Should().Contain("\"id\":");
        json.Should().Contain("\"name\":\"1-on-1 Personal Training\"");
        json.Should().Contain("\"description\":\"Intensive workout session\"");
        json.Should().Contain("\"price_vnd\":500000");
        json.Should().Contain("\"package_4_price_vnd\":1800000");
        json.Should().Contain("\"package_8_price_vnd\":3400000");
        json.Should().Contain("\"duration_minutes\":60");
        json.Should().Contain("\"is_active\":true");
        json.Should().Contain("\"service_kind\":\"one_to_one\"");
        json.Should().Contain("\"location_address\":\"1 Nguyen Hue, HCMC\"");

        // Must not contain camelCase variants
        json.Should().NotContain("\"priceVnd\":");
        json.Should().NotContain("\"durationMinutes\":");
        json.Should().NotContain("\"isActive\":");
    }

    [Fact]
    public void CreateServiceRequest_DeserializesFromSnakeCase()
    {
        var json = """
        {
            "name": "Boxing Basics",
            "description": "Intro to boxing",
            "price_vnd": 350000,
            "duration_minutes": 45
        }
        """;

        var req = JsonSerializer.Deserialize<CreateServiceRequest>(json);

        req.Should().NotBeNull();
        req!.Name.Should().Be("Boxing Basics");
        req.Description.Should().Be("Intro to boxing");
        req.PriceVnd.Should().Be(350000);
        req.DurationMinutes.Should().Be(45);
    }

    [Fact]
    public void AvailabilitySlotResponse_SerializesToSnakeCaseAndLowercaseEnum()
    {
        var id = Guid.NewGuid();
        var startUtc = new DateTime(2026, 12, 1, 2, 0, 0, DateTimeKind.Utc);
        var startLocal = new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Unspecified);

        var slot = new AvailabilitySlotResponse(
            Id: id,
            SlotStartUtc: startUtc,
            SlotStartLocal: startLocal,
            DurationMinutes: 60,
            Status: "open");

        var json = JsonSerializer.Serialize(slot);

        json.Should().Contain("\"id\":");
        json.Should().Contain("\"slot_start_utc\":");
        json.Should().Contain("\"slot_start_local\":");
        json.Should().Contain("\"duration_minutes\":60");
        json.Should().Contain("\"status\":\"open\"");

        // Must not contain camelCase variants
        json.Should().NotContain("\"slotStartUtc\":");
        json.Should().NotContain("\"slotStartLocal\":");
        json.Should().NotContain("\"durationMinutes\":");
    }

    [Fact]
    public void SetAvailabilityRequest_DeserializesFromSnakeCase()
    {
        var json = """
        {
            "from": "2026-12-01T00:00:00Z",
            "to": "2026-12-08T00:00:00Z",
            "slots": [
                {
                    "slot_start_local": "2026-12-01T09:00:00",
                    "duration_minutes": 60,
                    "trainer_timezone": "Asia/Ho_Chi_Minh"
                }
            ]
        }
        """;

        var req = JsonSerializer.Deserialize<SetAvailabilityRequest>(json);

        req.Should().NotBeNull();
        req!.From.Should().NotBeNull();
        req.To.Should().NotBeNull();
        req.Slots.Should().HaveCount(1);
        req.Slots[0].SlotStartLocal.Should().Be(new DateTime(2026, 12, 1, 9, 0, 0));
        req.Slots[0].DurationMinutes.Should().Be(60);
        req.Slots[0].TrainerTimezone.Should().Be("Asia/Ho_Chi_Minh");
    }

    [Fact]
    public void CertificateResponse_SerializesToSnakeCaseAndLowercaseStringStatus()
    {
        var id = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var verifiedAt = new DateTime(2026, 12, 1, 10, 0, 0, DateTimeKind.Utc);

        var cert = new CertificateResponse(
            Id: id,
            PtId: ptId,
            Title: "ACE Certified Personal Trainer",
            IssuedBy: "American Council on Exercise",
            Code: "ACE-123456",
            IssueDate: "2024-05-15",
            Status: "approved",
            ImageUrl: "https://cdn.fitmate.vn/certificates/cert.jpg",
            RejectReason: null,
            VerifiedAt: verifiedAt);

        var json = JsonSerializer.Serialize(cert);

        json.Should().Contain("\"id\":");
        json.Should().Contain("\"pt_id\":");
        json.Should().Contain("\"title\":\"ACE Certified Personal Trainer\"");
        json.Should().Contain("\"issued_by\":\"American Council on Exercise\"");
        json.Should().Contain("\"code\":\"ACE-123456\"");
        json.Should().Contain("\"issue_date\":\"2024-05-15\"");
        json.Should().Contain("\"status\":\"approved\"");
        json.Should().Contain("\"image_url\":\"https://cdn.fitmate.vn/certificates/cert.jpg\"");
        json.Should().Contain("\"reject_reason\":null");
        json.Should().Contain("\"verified_at\":");

        // Must not contain camelCase variants
        json.Should().NotContain("\"ptId\":");
        json.Should().NotContain("\"issuedBy\":");
        json.Should().NotContain("\"issueDate\":");
        json.Should().NotContain("\"imageUrl\":");
        json.Should().NotContain("\"rejectReason\":");
        json.Should().NotContain("\"verifiedAt\":");
    }

    [Fact]
    public void CertificatePageResponse_SerializesToItemsStructure()
    {
        var page = new CertificatePageResponse(
            Items: new List<CertificateResponse>
            {
                new(Guid.NewGuid(), Guid.NewGuid(), "Cert 1", "Org", "C1", "2024-01-01", "pending", null, null, null)
            },
            Total: 1,
            Page: 1,
            PageSize: 25);

        var json = JsonSerializer.Serialize(page);

        json.Should().Contain("\"items\":");
        json.Should().Contain("\"total\":1");
        json.Should().Contain("\"page\":1");
        json.Should().Contain("\"pageSize\":25");
    }
}
