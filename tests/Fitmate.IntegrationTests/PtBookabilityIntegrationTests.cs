using Fitmate.Application.Booking;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Trainers;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Fitmate.IntegrationTests;

public sealed class PtBookabilityIntegrationTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;

    public PtBookabilityIntegrationTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public async Task DiscoveryAndHold_RequireVerifiedAccountCompleteServiceAndPublishedSlot()
    {
        var ptId = Guid.NewGuid();
        _factory.CreateAuthenticatedClient("PT", ptId).Dispose();
        var adminId = Guid.NewGuid();
        var localStart = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(2).AddHours(7), DateTimeKind.Unspecified);
        var slot = AvailabilitySlot.Create(ptId, localStart, "Asia/Ho_Chi_Minh", 90);
        var profile = PtProfile.CreateDraft(ptId, "Sức mạnh", 3, 300_000, "Huấn luyện cá nhân");
        profile.Approve(adminId);
        var cert = Certificate.Submit(ptId, "PT Certificate", "Training Institute", "CERT-01",
            new DateOnly(2025, 1, 1), "test-certificate", ptId);
        cert.Approve(adminId);
        var service = Service.Create(ptId, "Buổi tập cá nhân", "Hướng dẫn bài tập và sửa kỹ thuật trong một buổi.", 300_000, 60);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            db.AddRange(profile, cert, service, slot);
            await db.SaveChangesAsync();
        }

        await AssertBookability(ptId, service.Id, slot.SlotStartUtc, expected: false);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var user = await db.Users.FindAsync(ptId);
            user!.PhoneNumber = "+84901234567";
            user.PhoneNumberConfirmed = true;
            await db.SaveChangesAsync();
        }

        await AssertBookability(ptId, service.Id, slot.SlotStartUtc, expected: false);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var stored = await db.Services.FindAsync(service.Id);
            stored!.ConfigureDetails(ServiceKind.OneToOne, ServiceDeliveryMode.InPerson,
                "Người mới tập", "Giày thể thao", "Hướng dẫn kỹ thuật và kế hoạch tập", "Fit Gym", "1 Nguyễn Huệ, TP.HCM");
            await db.SaveChangesAsync();
        }

        await AssertBookability(ptId, service.Id, slot.SlotStartUtc, expected: true);
        await AssertSlotBookability(service.Id, slot.SlotStartUtc.AddHours(1), expected: false);

        using var client = _factory.CreateClient();
        var detailResponse = await client.GetAsync($"/api/v1/trainers/{ptId}");
        detailResponse.EnsureSuccessStatusCode();
        using var detailJson = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        detailJson.RootElement.GetProperty("id").GetGuid().Should().Be(ptId);
        detailJson.RootElement.GetProperty("full_name").GetString().Should().Be("Test PT");
        detailJson.RootElement.GetProperty("services")[0].GetProperty("location_name").GetString().Should().Be("Fit Gym");

        var listResponse = await client.GetAsync("/api/v1/trainers?limit=100");
        listResponse.EnsureSuccessStatusCode();
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        listJson.RootElement.GetProperty("items").EnumerateArray()
            .Any(item => item.GetProperty("id").GetGuid() == ptId &&
                item.GetProperty("price_per_session_vnd").GetInt64() == 300_000).Should().BeTrue();
    }

    private async Task AssertBookability(Guid ptId, Guid serviceId, DateTime slotStartUtc, bool expected)
    {
        using var scope = _factory.Services.CreateScope();
        var marketplace = scope.ServiceProvider.GetRequiredService<ITrainerMarketplaceStore>();
        var booking = scope.ServiceProvider.GetRequiredService<IServiceBookingLookup>();
        var detail = await marketplace.FindDetailAsync(ptId, default);
        (detail is not null).Should().Be(expected);
        if (expected) detail!.Profile.PricePerSessionVnd.Should().Be(300_000);
        (await booking.FindBookableAsync(serviceId, slotStartUtc, default) is not null).Should().Be(expected);
    }

    private async Task AssertSlotBookability(Guid serviceId, DateTime slotStartUtc, bool expected)
    {
        using var scope = _factory.Services.CreateScope();
        var booking = scope.ServiceProvider.GetRequiredService<IServiceBookingLookup>();
        (await booking.FindBookableAsync(serviceId, slotStartUtc, default) is not null).Should().Be(expected);
    }
}
