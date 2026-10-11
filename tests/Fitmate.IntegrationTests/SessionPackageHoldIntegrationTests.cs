using Fitmate.Application.Booking;
using Fitmate.Application.Payments;
using Fitmate.Application.Trainers;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using Fitmate.Domain.Trainers;
using Fitmate.Infrastructure.Persistence;
using Fitmate.Infrastructure.Booking;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fitmate.IntegrationTests;

public sealed class SessionPackageHoldIntegrationTests : IClassFixture<FitmateTestAppFactory>
{
    private sealed class OnlineLookup(Guid serviceId, Guid ptId) : IServiceBookingLookup
    {
        public Task<ServiceSnapshot?> FindBookableAsync(Guid requestedId, DateTime startUtc, CancellationToken ct) =>
            Task.FromResult<ServiceSnapshot?>(requestedId == serviceId
                ? new ServiceSnapshot(serviceId, ptId, 100_000, 60, true, ServiceDeliveryMode.Online)
                : null);
    }

    private sealed class PackageMeetStub(int failOnCall = 0, Func<Task>? beforeFirstSpace = null) : IGoogleMeetConnectionService
    {
        public int Calls { get; private set; }
        public List<Guid> Removed { get; } = [];
        public async Task<GoogleMeetSpace> CreateForAppointmentAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct)
        {
            Calls++;
            if (Calls == 1 && beforeFirstSpace is not null) await beforeFirstSpace();
            if (Calls == failOnCall)
                throw new DomainException("GOOGLE_MEET_CREATE_FAILED", "Test Meet failure", 503);
            return new GoogleMeetSpace("spaces/" + appointmentId,
                "https://meet.google.com/abc-defg-hij");
        }
        public Task RemoveForAppointmentAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct)
        { Removed.Add(appointmentId); return Task.CompletedTask; }
        public Task<GoogleMeetConnectionStatus> GetStatusAsync(Guid ptUserId, CancellationToken ct) =>
            Task.FromResult(new GoogleMeetConnectionStatus(true));
        public Task<string> CreateAuthorizationUrlAsync(Guid ptUserId, CancellationToken ct) => throw new NotSupportedException();
        public Task CompleteAuthorizationAsync(string code, string state, CancellationToken ct) => throw new NotSupportedException();
        public Task DisconnectAsync(Guid ptUserId, CancellationToken ct) => throw new NotSupportedException();
        public Task<GoogleMeetSpace> CreateSpaceAsync(Guid ptUserId, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetMeetingUriAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetMeetingUriForParticipantAsync(Guid actorUserId, Guid appointmentId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class TestPayosProvider(FitmateDbContext db) : IPaymentProvider, IPaymentReservation
    {
        public PaymentMethod Method => PaymentMethod.Payos;
        public int CreateCalls { get; private set; }
        public void Reserve(Guid paymentId, DateTime expiresAt) =>
            db.Set<PayosOrder>().Add(PayosOrder.Create(paymentId, expiresAt));
        public Task<ProviderCreatePaymentResult> CreatePaymentAsync(
            ProviderCreatePaymentRequest request, CancellationToken ct)
        {
            CreateCalls++;
            return Task.FromResult(new ProviderCreatePaymentResult(
                "test-order", "https://pay.payos.vn/web/test-link"));
        }
        public Task<ProviderQueryResult> QueryTransactionAsync(
            ProviderQueryRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<ProviderRefundResult> RefundAsync(
            ProviderRefundRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private readonly FitmateTestAppFactory _factory;

    public SessionPackageHoldIntegrationTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Theory]
    [InlineData(0, SessionPackageStatus.Held)]
    [InlineData(3, SessionPackageStatus.Expired)]
    public async Task OnlinePackage_PreparesAllMeetLinksOrReleasesEveryReservation(
        int failOnCall, SessionPackageStatus expectedStatus)
    {
        var ptId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        _factory.CreateAuthenticatedClient("PT", ptId).Dispose();
        _factory.CreateAuthenticatedClient("Member", memberId).Dispose();
        var service = Service.Create(ptId, "Tập online", "Tập 1–1 qua Google Meet.", 100_000, 60);
        service.ConfigureDetails(ServiceKind.OneToOne, ServiceDeliveryMode.Online,
            "Người mới tập", "Thảm tập", "Hướng dẫn trong buổi", null, null);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        db.Add(service);
        await db.SaveChangesAsync();
        var meet = new PackageMeetStub(failOnCall, async () =>
        {
            var preparing = await db.SessionPackages.AsNoTracking()
                .SingleAsync(p => p.MemberId == memberId && p.ServiceId == service.Id);
            preparing.Status.Should().Be(SessionPackageStatus.OnlinePreparing);
            var checkout = new PackagePaymentCheckoutService(
                scope.ServiceProvider.GetRequiredService<IPackagePaymentStore>(),
                scope.ServiceProvider.GetRequiredService<IPaymentStore>(),
                scope.ServiceProvider.GetRequiredService<IAppointmentStore>(),
                new TestPayosProvider(db));
            Func<Task> prematureCheckout = () => checkout.CreateAsync(memberId,
                preparing.Id, CancellationToken.None);
            await prematureCheckout.Should().ThrowAsync<ConflictException>()
                .Where(e => e.Code == "PACKAGE_PAYMENT_ALREADY_PROCESSED");
            (await db.Payments.CountAsync(p => p.PackageId == preparing.Id)).Should().Be(0);
        });
        var holdService = new SessionPackageHoldService(
            scope.ServiceProvider.GetRequiredService<IAppointmentStore>(),
            scope.ServiceProvider.GetRequiredService<IBookingHoldStore>(),
            scope.ServiceProvider.GetRequiredService<ISessionPackageStore>(),
            new OnlineLookup(service.Id, ptId),
            scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>(), meet);
        var slots = Enumerable.Range(0, 4)
            .Select(i => DateTime.UtcNow.AddDays(3 + i)).ToArray();
        if (failOnCall == 0)
        {
            var held = await holdService.CreateOnlineHoldAsync(memberId, ptId, service.Id,
                slots, CancellationToken.None);
            held.Status.Should().Be(SessionPackageStatus.Held);
            meet.Calls.Should().Be(4);
            meet.Removed.Should().BeEmpty();
        }
        else
        {
            Func<Task> attempt = () => holdService.CreateOnlineHoldAsync(memberId, ptId,
                service.Id, slots, CancellationToken.None);
            await attempt.Should().ThrowAsync<DomainException>()
                .Where(e => e.Code == "GOOGLE_MEET_CREATE_FAILED");
            meet.Calls.Should().Be(failOnCall);
            meet.Removed.Should().HaveCount(4);
        }
        db.ChangeTracker.Clear();
        var package = await db.SessionPackages.AsNoTracking().Include(p => p.Sessions)
            .SingleAsync(p => p.MemberId == memberId && p.ServiceId == service.Id);
        package.Status.Should().Be(expectedStatus);
        var appointmentIds = package.Sessions.Select(s => s.AppointmentId).ToArray();
        var holdIds = package.Sessions.Select(s => s.HoldId).ToArray();
        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => appointmentIds.Contains(a.Id)).ToArrayAsync();
        var holds = await db.BookingHolds.AsNoTracking()
            .Where(h => holdIds.Contains(h.Id)).ToArrayAsync();
        appointments.Should().HaveCount(4);
        holds.Should().HaveCount(4);
        appointments.Should().OnlyContain(a => a.Status ==
            (failOnCall == 0 ? AppointmentStatus.Held : AppointmentStatus.Expired));
        holds.Should().OnlyContain(h => h.Status == (failOnCall == 0 ? "active" : "released"));
        var heldEvents = await db.OutboxMessages.AsNoTracking()
            .Where(o => o.Type == "booking.package_held").Select(o => o.PayloadJson).ToArrayAsync();
        heldEvents.Count(payload => payload.Contains(package.Id.ToString(), StringComparison.OrdinalIgnoreCase))
            .Should().Be(failOnCall == 0 ? 1 : 0);
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

        using (var memberClient = _factory.CreateAuthenticatedClient("Member", memberId))
        using (var ptClient = _factory.CreateAuthenticatedClient("PT", ptId))
        using (var adminClient = _factory.CreateAuthenticatedClient("Admin", adminId))
        using (var strangerClient = _factory.CreateAuthenticatedClient("Member", Guid.NewGuid()))
        using (var publicClient = _factory.CreateClient())
        {
            (await publicClient.GetAsync($"/api/v1/me/session-packages/{first.Id}"))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await strangerClient.GetAsync($"/api/v1/me/session-packages/{first.Id}"))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await memberClient.GetAsync("/api/v1/me/session-packages/assigned"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await memberClient.GetAsync("/api/v1/me/session-packages?page=0"))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var listResponse = await memberClient.GetAsync("/api/v1/me/session-packages?page=1&pageSize=10");
            listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
            var listed = list.RootElement.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == first.Id);
            listed.GetProperty("sessions").GetArrayLength().Should().Be(4);
            listed.GetProperty("total_price_vnd").GetInt64().Should().Be(800_004);
            listed.GetProperty("payment_status").ValueKind.Should().Be(JsonValueKind.Null);

            var ptResponse = await ptClient.GetAsync($"/api/v1/me/session-packages/{first.Id}");
            ptResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            using var detail = JsonDocument.Parse(await ptResponse.Content.ReadAsStringAsync());
            detail.RootElement.GetProperty("pt_user_id").GetGuid().Should().Be(ptId);
            detail.RootElement.GetProperty("sessions").GetArrayLength().Should().Be(4);
            (await ptClient.GetAsync("/api/v1/me/session-packages/assigned"))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            (await publicClient.GetAsync("/api/v1/admin/session-packages"))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await memberClient.GetAsync("/api/v1/admin/session-packages"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await adminClient.GetAsync("/api/v1/admin/session-packages?status=invalid"))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await adminClient.GetAsync("/api/v1/admin/session-packages?status=1"))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await adminClient.GetAsync("/api/v1/admin/session-packages?pageSize=101"))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await adminClient.GetAsync($"/api/v1/admin/session-packages/{Guid.NewGuid()}"))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
            var adminListResponse = await adminClient.GetAsync(
                $"/api/v1/admin/session-packages?status=Held&memberId={memberId}&ptUserId={ptId}");
            adminListResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            using var adminList = JsonDocument.Parse(await adminListResponse.Content.ReadAsStringAsync());
            adminList.RootElement.GetProperty("items").EnumerateArray()
                .Should().ContainSingle(item => item.GetProperty("id").GetGuid() == first.Id);
            using var filteredOut = JsonDocument.Parse(await (
                await adminClient.GetAsync($"/api/v1/admin/session-packages?status=Active&memberId={memberId}&ptUserId={ptId}"))
                .Content.ReadAsStringAsync());
            filteredOut.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
            var adminDetailResponse = await adminClient.GetAsync(
                $"/api/v1/admin/session-packages/{first.Id}");
            adminDetailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            using var adminDetail = JsonDocument.Parse(await adminDetailResponse.Content.ReadAsStringAsync());
            adminDetail.RootElement.GetProperty("sessions").GetArrayLength().Should().Be(4);
        }

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

        var second = await db.SessionPackages.SingleAsync(p => p.PtUserId == ptId && p.Id != first.Id);
        var testProvider = new TestPayosProvider(db);
        var checkout = new PackagePaymentCheckoutService(
            scope.ServiceProvider.GetRequiredService<IPackagePaymentStore>(),
            scope.ServiceProvider.GetRequiredService<IPaymentStore>(),
            scope.ServiceProvider.GetRequiredService<IAppointmentStore>(),
            testProvider);
        var firstCheckout = await checkout.CreateAsync(second.MemberId, second.Id, CancellationToken.None);
        var retriedCheckout = await checkout.CreateAsync(second.MemberId, second.Id, CancellationToken.None);
        firstCheckout.PaymentId.Should().Be(retriedCheckout.PaymentId);
        firstCheckout.AmountVnd.Should().Be(800_004);
        firstCheckout.RedirectUrl.Should().Be("https://pay.payos.vn/web/test-link");
        testProvider.CreateCalls.Should().Be(1);
        (await db.Payments.CountAsync(p => p.PackageId == second.Id)).Should().Be(1);
        (await db.Set<PayosOrder>().CountAsync(o => o.PaymentId == firstCheckout.PaymentId)).Should().Be(1);
        second.Status.Should().Be(SessionPackageStatus.PaymentPending);

        var expiration = scope.ServiceProvider.GetRequiredService<SessionPackageExpirationService>();
        (await expiration.ExpireUnpaidAsync(DateTime.UtcNow.AddMinutes(11), CancellationToken.None))
            .Should().BeGreaterThanOrEqualTo(1);
        db.ChangeTracker.Clear();
        (await db.SessionPackages.SingleAsync(p => p.Id == first.Id)).Status
            .Should().Be(SessionPackageStatus.Expired);
        (await db.SessionPackages.SingleAsync(p => p.Id == second.Id)).Status
            .Should().Be(SessionPackageStatus.PaymentPending);
        var firstAppointmentIds = first.Sessions.Select(s => s.AppointmentId).ToArray();
        (await db.Appointments.Where(a => firstAppointmentIds.Contains(a.Id)).ToListAsync())
            .Should().OnlyContain(a => a.Status == AppointmentStatus.Expired);
        var firstHoldIds = first.Sessions.Select(s => s.HoldId).ToArray();
        (await db.BookingHolds.Where(h => firstHoldIds.Contains(h.Id)).ToListAsync())
            .Should().OnlyContain(h => h.Status == "released");
    }
}
