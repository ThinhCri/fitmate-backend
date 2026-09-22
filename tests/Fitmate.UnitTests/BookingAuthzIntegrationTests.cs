using Fitmate.Application.Auth;
using Fitmate.Application.Booking;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-090: BookingService phải enforce role PT ở Application cho Complete.
/// </summary>
public class BookingAuthzIntegrationTests
{
    private sealed class InMemoryAppointmentStore : IAppointmentStore
    {
        public Dictionary<Guid, Appointment> Items { get; } = new();
        public Task<Appointment?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.GetValueOrDefault(id));
        public Task<bool> ExistsActiveForSlotAsync(Guid ptUserId, DateTime slotStartUtc, CancellationToken ct) => Task.FromResult(false);
        public Task<(IReadOnlyList<Appointment> Items, int Total)> ListByMemberPagedAsync(Guid m, BookingStatus? s, int p, int ps, CancellationToken ct)
            => Task.FromResult(((IReadOnlyList<Appointment>)Array.Empty<Appointment>(), 0));
        public Task<(IReadOnlyList<Appointment> Items, int Total)> ListByPtPagedAsync(Guid p, BookingStatus? s, int page, int pageSize, CancellationToken ct)
            => Task.FromResult(((IReadOnlyList<Appointment>)Array.Empty<Appointment>(), 0));
        public Task<(IReadOnlyList<Appointment> Items, int Total)> ListAdminPagedAsync(BookingStatus? s, Guid? p, Guid? m, int page, int pageSize, CancellationToken ct)
            => Task.FromResult(((IReadOnlyList<Appointment>)Array.Empty<Appointment>(), 0));
        public Task AddAsync(Appointment a, CancellationToken ct) { Items[a.Id] = a; return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class InMemoryHoldStore : IBookingHoldStore
    {
        public Task<BookingHold?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<BookingHold?>(null);
        public Task AddAsync(BookingHold h, CancellationToken ct) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoServiceLookup : IServiceBookingLookup
    {
        public Task<ServiceSnapshot?> FindActiveAsync(Guid id, CancellationToken ct) => Task.FromResult<ServiceSnapshot?>(null);
    }

    private sealed class OutboxStub : IOutboxDispatcher
    {
        public void Enqueue(string type, object payload) { }
    }

    private sealed class RoleGuard : IAuthorizationGuard
    {
        public string? Role { get; set; }
        public Task EnsureRoleAsync(Guid userId, string role, CancellationToken ct)
        {
            if (!string.Equals(Role, role, StringComparison.OrdinalIgnoreCase))
                throw new ForbiddenException("forbidden", $"Requires role {role}.");
            return Task.CompletedTask;
        }
        public Task EnsureAnyRoleAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureOwnerAsync(string r, Guid u, Guid o, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct) => Task.FromResult(Role);
        public Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken ct) => Task.FromResult(true);
    }

    [Fact]
    public async Task Complete_NonPtUser_ThrowsForbidden_EvenIfOwner()
    {
        // User có role Member nhưng ID trùng PT user — Role vẫn fail trước ownership check.
        var pt = Guid.NewGuid();
        var appt = Appointment.CreateHold(Guid.NewGuid(), pt, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        var store = new InMemoryAppointmentStore { Items = { [appt.Id] = appt } };
        var guard = new RoleGuard { Role = "Member" };
        var svc = new BookingService(store, new InMemoryHoldStore(), new NoServiceLookup(), new OutboxStub(), guard);

        var act = () => svc.CompleteAsync(pt, appt.Id, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Complete_AsPt_Passes()
    {
        var pt = Guid.NewGuid();
        var appt = Appointment.CreateHold(Guid.NewGuid(), pt, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        var store = new InMemoryAppointmentStore { Items = { [appt.Id] = appt } };
        var guard = new RoleGuard { Role = "PT" };
        var svc = new BookingService(store, new InMemoryHoldStore(), new NoServiceLookup(), new OutboxStub(), guard);

        var done = await svc.CompleteAsync(pt, appt.Id, CancellationToken.None);
        done.Status.Should().Be(BookingStatus.Completed);
    }
}
