using Fitmate.Application.Admin;
using Fitmate.Application.Auth;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-220: AdminMetricsService — action-required counts + recent bookings order + AuthorizationGuard.
/// </summary>
public class AdminMetricsServiceTests
{
    private sealed class RoleRequireAuthz : IAuthorizationGuard
    {
        public string? RequiredRole { get; set; }
        public Task EnsureRoleAsync(Guid userId, string role, CancellationToken ct)
        {
            if (RequiredRole != null && !string.Equals(role, RequiredRole, StringComparison.OrdinalIgnoreCase))
                throw new ForbiddenException(ErrorCodes.Identity.Forbidden, $"Requires role {role}.");
            return Task.CompletedTask;
        }
        public Task EnsureAnyRoleAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureOwnerAsync(string r, Guid u, Guid o, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct) => Task.FromResult<string?>(RequiredRole);
        public Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken ct) => Task.FromResult(RequiredRole != null);
    }

    private sealed class InMemoryStore : IAdminMetricsStore
    {
        public int PendingCerts { get; set; }
        public int PendingReports { get; set; }
        public int PendingPayments { get; set; }
        public List<RecentAppointmentRow> Recent { get; } = new();

        public Task<int> CountPendingCertificatesAsync(CancellationToken ct) => Task.FromResult(PendingCerts);
        public Task<int> CountPendingReportsAsync(CancellationToken ct) => Task.FromResult(PendingReports);
        public Task<int> CountPendingPaymentsAsync(CancellationToken ct) => Task.FromResult(PendingPayments);
        public Task<IReadOnlyList<RecentAppointmentRow>> ListRecentAppointmentsAsync(int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<RecentAppointmentRow>>(Recent.Take(limit).ToList());
    }

    private static AdminMetricsService NewService(IAdminMetricsStore store, string? requiredRole = "Admin") => new(store, new RoleRequireAuthz { RequiredRole = requiredRole });

    private static RecentAppointmentRow Row(BookingStatus status, DateTime createdAt, long amount = 500_000)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), amount, status, createdAt);

    [Fact]
    public async Task ActionRequired_AllCountsAggregated()
    {
        var store = new InMemoryStore { PendingCerts = 3, PendingReports = 5, PendingPayments = 2 };
        store.Recent.Add(Row(BookingStatus.Paid, DateTime.UtcNow.AddMinutes(-5)));
        var svc = NewService(store);

        var ar = await svc.GetActionRequiredAsync(Guid.NewGuid(), recentLimit: 10, CancellationToken.None);
        ar.PendingCertificates.Should().Be(3);
        ar.PendingReports.Should().Be(5);
        ar.PendingPayments.Should().Be(2);
        ar.RecentBookings.Should().HaveCount(1);
        ar.RecentBookings[0].Status.Should().Be("Paid");
    }

    [Fact]
    public async Task ActionRequired_RecentLimitClampedToMax()
    {
        var store = new InMemoryStore();
        for (var i = 0; i < 100; i++)
            store.Recent.Add(Row(BookingStatus.Paid, DateTime.UtcNow.AddMinutes(-i)));
        var svc = NewService(store);

        var ar = await svc.GetActionRequiredAsync(Guid.NewGuid(), recentLimit: 1000, CancellationToken.None);
        ar.RecentBookings.Should().HaveCount(50); // MaxRecentLimit
    }

    [Fact]
    public async Task ActionRequired_RecentLimitZeroDefaultsTo10()
    {
        var store = new InMemoryStore();
        for (var i = 0; i < 20; i++)
            store.Recent.Add(Row(BookingStatus.Completed, DateTime.UtcNow.AddMinutes(-i)));
        var svc = NewService(store);

        var ar = await svc.GetActionRequiredAsync(Guid.NewGuid(), recentLimit: 0, CancellationToken.None);
        ar.RecentBookings.Should().HaveCount(10);
    }

    [Fact]
    public async Task ActionRequired_NonAdmin_Forbidden()
    {
        var svc = NewService(new InMemoryStore(), requiredRole: "Pt");
        var act = () => svc.GetActionRequiredAsync(Guid.NewGuid(), 10, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task ActionRequired_EmptyStore_ZerosAndEmptyList()
    {
        var svc = NewService(new InMemoryStore());
        var ar = await svc.GetActionRequiredAsync(Guid.NewGuid(), 10, CancellationToken.None);
        ar.PendingCertificates.Should().Be(0);
        ar.PendingReports.Should().Be(0);
        ar.PendingPayments.Should().Be(0);
        ar.RecentBookings.Should().BeEmpty();
    }

    [Fact]
    public async Task ActionRequired_StatusString_MirrorsEnum()
    {
        var store = new InMemoryStore();
        store.Recent.Add(Row(BookingStatus.Refunded, DateTime.UtcNow.AddMinutes(-1)));
        store.Recent.Add(Row(BookingStatus.Cancelled, DateTime.UtcNow.AddMinutes(-2)));
        var svc = NewService(store);

        var ar = await svc.GetActionRequiredAsync(Guid.NewGuid(), 10, CancellationToken.None);
        ar.RecentBookings[0].Status.Should().Be("Refunded");
        ar.RecentBookings[1].Status.Should().Be("Cancelled");
    }
}
