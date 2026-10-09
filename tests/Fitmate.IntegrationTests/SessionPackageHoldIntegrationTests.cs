using Fitmate.Application.Booking;
using Fitmate.Application.Payments;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using Fitmate.Domain.Trainers;
using Fitmate.Infrastructure.Persistence;
using Fitmate.Infrastructure.Booking;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fitmate.IntegrationTests;

public sealed class SessionPackageHoldIntegrationTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;

    public SessionPackageHoldIntegrationTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public async Task FourSlotsCommitTogether_AndCompetingPackagesCannotDoubleBook()
    {
        var ptId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        _factory.CreateAuthenticatedClient("PT", ptId).Dispose();
        _factory.CreateAuthenticatedClient("Member", memberId).Dispose();
        var adminId = Guid.NewGuid();
        var profile = PtProfile.CreateDraft(ptId, "Sức mạnh", 3, 200_001, "Hướng dẫn tập luyện");
        profile.Approve(adminId);
        var cert = Certificate.Submit(ptId, "Chứng chỉ PT", "Trung tâm đào tạo", "PKG-01",
            new DateOnly(2025, 1, 1), "test-certificate", ptId);
        cert.Approve(adminId);
        var service = Service.Create(ptId, "Tập 1–1", "Hướng dẫn kỹ thuật và theo dõi từng buổi.",
            200_001, 60);
        service.ConfigureDetails(ServiceKind.OneToOne, ServiceDeliveryMode.InPerson,
            "Người mới tập", "Giày thể thao", "Hướng dẫn trong buổi", "Fit Gym", "1 Nguyễn Huệ, TP.HCM");
        var localStart = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(3).Date.AddHours(8),
            DateTimeKind.Unspecified);
        var slots = Enumerable.Range(0, 8)
            .Select(i => AvailabilitySlot.Create(ptId, localStart.AddDays(i),
                "Asia/Ho_Chi_Minh", 60))
            .ToArray();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var pt = await db.Users.FindAsync(ptId);
        pt!.PhoneNumber = "+849" + Random.Shared.Next(10_000_000, 100_000_000);
        pt.PhoneNumberConfirmed = true;
        db.AddRange(profile, cert, service);
        db.AddRange(slots);
        await db.SaveChangesAsync();

        var holdService = scope.ServiceProvider.GetRequiredService<SessionPackageHoldService>();
        var first = await holdService.CreateInPersonHoldAsync(memberId, ptId, service.Id,
            slots.Take(4).Select(s => s.SlotStartUtc).ToArray(), CancellationToken.None);
        first.SessionCount.Should().Be(4);
        first.Sessions.Sum(s => s.PaidAllocationVnd).Should().Be(800_004);
        first.Sessions.Select(s => s.HoldId).Distinct().Should().HaveCount(4);
        (await db.SessionPackages.CountAsync(p => p.PtUserId == ptId)).Should().Be(1);
        (await db.SessionPackageSessions.CountAsync(s => s.PackageId == first.Id)).Should().Be(4);

        var firstSession = first.Sessions.Single(s => s.Sequence == 1);
        var paymentService = new PaymentService(
            scope.ServiceProvider.GetRequiredService<IPaymentStore>(), new StubPaymentProvider());
        Func<Task> singleCheckout = () => paymentService.CreateAsync(memberId,
            new CreatePaymentRequest { HoldId = firstSession.HoldId, Method = PaymentMethod.Vnpay },
            CancellationToken.None);
        await singleCheckout.Should().ThrowAsync<ConflictException>()
            .Where(e => e.Code == "PACKAGE_CHECKOUT_REQUIRED");
        (await db.Payments.CountAsync(p => p.AppointmentId == firstSession.AppointmentId)).Should().Be(0);

        var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
        Func<Task> singleCancel = () => bookingService.CancelAsync(memberId,
            firstSession.AppointmentId, "Changed plans", CancellationToken.None);
        await singleCancel.Should().ThrowAsync<ConflictException>()
            .Where(e => e.Code == "PACKAGE_ACTION_REQUIRED");

        var secondMember = Guid.NewGuid();
        _factory.CreateAuthenticatedClient("Member", secondMember).Dispose();
        var conflictingSlots = slots.Skip(3).Take(4).Select(s => s.SlotStartUtc).ToArray();
        Func<Task> conflict = () => holdService.CreateInPersonHoldAsync(secondMember, ptId,
            service.Id, conflictingSlots, CancellationToken.None);
        await conflict.Should().ThrowAsync<ConflictException>();

        (await db.SessionPackages.CountAsync(p => p.PtUserId == ptId)).Should().Be(1);
        (await db.Appointments.CountAsync(a => a.PtUserId == ptId)).Should().Be(4);
        (await db.BookingHolds.CountAsync(h => h.PtUserId == ptId)).Should().Be(4);

        var concurrentMembers = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var id in concurrentMembers)
            _factory.CreateAuthenticatedClient("Member", id).Dispose();
        var remainingSlots = slots.Skip(4).Take(4).Select(s => s.SlotStartUtc).ToArray();
        async Task<Exception?> AttemptAsync(Guid id)
        {
            using var attemptScope = _factory.Services.CreateScope();
            var attemptService = attemptScope.ServiceProvider.GetRequiredService<SessionPackageHoldService>();
            try
            {
                await attemptService.CreateInPersonHoldAsync(id, ptId, service.Id,
                    remainingSlots, CancellationToken.None);
                return null;
            }
            catch (Exception ex) { return ex; }
        }

        var results = await Task.WhenAll(concurrentMembers.Select(AttemptAsync));
        results.Count(x => x is null).Should().Be(1);
        results.Count(x => x is ConflictException).Should().Be(1);
        (await db.SessionPackages.CountAsync(p => p.PtUserId == ptId)).Should().Be(2);
        (await db.Appointments.CountAsync(a => a.PtUserId == ptId)).Should().Be(8);
        (await db.BookingHolds.CountAsync(h => h.PtUserId == ptId)).Should().Be(8);

        var expiration = scope.ServiceProvider.GetRequiredService<SessionPackageExpirationService>();
        (await expiration.ExpireUnpaidAsync(DateTime.UtcNow.AddMinutes(11), CancellationToken.None))
            .Should().BeGreaterThanOrEqualTo(2);
        db.ChangeTracker.Clear();
        (await db.SessionPackages.Where(p => p.PtUserId == ptId).ToListAsync())
            .Should().OnlyContain(p => p.Status == SessionPackageStatus.Expired);
        (await db.Appointments.Where(a => a.PtUserId == ptId).ToListAsync())
            .Should().OnlyContain(a => a.Status == AppointmentStatus.Expired);
        (await db.BookingHolds.Where(h => h.PtUserId == ptId).ToListAsync())
            .Should().OnlyContain(h => h.Status == "released");
    }
}
