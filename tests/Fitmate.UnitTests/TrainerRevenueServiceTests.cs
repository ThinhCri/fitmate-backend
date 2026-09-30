using Fitmate.Application.Auth;
using Fitmate.Application.Finance;
using Fitmate.Domain.Common;
using FluentAssertions;

namespace Fitmate.UnitTests;

public class TrainerRevenueServiceTests
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

    private sealed class CapturingOutbox : Fitmate.Application.Outbox.IOutboxDispatcher
    {
        public List<(string Type, object Payload)> Messages { get; } = new();
        public void Enqueue(string type, object payload) => Messages.Add((type, payload));
    }

    private sealed class InMemoryTrainerRevenueStore : ITrainerRevenueStore
    {
        public List<TrainerBookingRevenueRow> Bookings { get; } = new();

        public Task<IReadOnlyList<TrainerBookingRevenueRow>> ListBookingsInRangeAsync(
            Guid ptUserId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
        {
            var items = Bookings
                .Where(b => b.CreatedAt >= fromUtc && b.CreatedAt < toUtc)
                .ToList();
            return Task.FromResult<IReadOnlyList<TrainerBookingRevenueRow>>(items);
        }

        public Task<(IReadOnlyList<TrainerBookingRevenueRow> Items, int Total)> ListHistoryPagedAsync(
            Guid ptUserId, int skip, int take, CancellationToken ct)
        {
            var paged = Bookings.Skip(skip).Take(take).ToList();
            return Task.FromResult(((IReadOnlyList<TrainerBookingRevenueRow>)paged, Bookings.Count));
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static TrainerRevenueService NewService(
        ITrainerRevenueStore store,
        string? requiredRole = "PT",
        CapturingOutbox? outbox = null)
        => new(store, new RoleRequireAuthz { RequiredRole = requiredRole }, outbox ?? new CapturingOutbox());

    private static TrainerBookingRevenueRow BookingRow(
        long actualPaid, long fee, long share, DateTime utc, AppointmentStatus status = AppointmentStatus.Completed, long refunded = 0)
        => new(
            AppointmentId: Guid.NewGuid(),
            SlotStartUtc: utc,
            CreatedAt: utc,
            PriceListedVnd: actualPaid,
            ActualPaidVnd: actualPaid,
            PlatformFeeVnd: fee,
            TrainerShareVnd: share,
            RefundedVnd: refunded,
            Status: status);

    [Fact]
    public async Task GetOverview_Aggregates_Gross_Fee_NetEarnings_AndChart()
    {
        var store = new InMemoryTrainerTrainerStoreWrapper();
        var ptId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddDays(-2);

        // Booking 1: 100k gross -> 10k fee, 90k share
        store.Bookings.Add(BookingRow(100_000, 10_000, 90_000, t0, AppointmentStatus.Completed));
        // Booking 2: 200k gross -> 20k fee, 180k share
        store.Bookings.Add(BookingRow(200_000, 20_000, 180_000, t0.AddHours(2), AppointmentStatus.Completed));

        var svc = NewService(store, "PT");
        var ov = await svc.GetOverviewAsync(ptId, FinanceRange._30d, CancellationToken.None);

        ov.TotalGrossVnd.Should().Be(300_000);
        ov.PlatformFeeVnd.Should().Be(30_000);
        ov.NetEarningsVnd.Should().Be(270_000);
        ov.CompletedSessionsCount.Should().Be(2);
        ov.Chart.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetOverview_WithRefundedBooking_DeductsFromNetEarnings()
    {
        var store = new InMemoryTrainerTrainerStoreWrapper();
        var ptId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddDays(-1);

        // Booking 1: 500k -> completed (50k fee, 450k share)
        store.Bookings.Add(BookingRow(500_000, 50_000, 450_000, t0, AppointmentStatus.Completed));
        // Booking 2: 200k -> refunded (20k fee, 180k share, 200k refunded)
        store.Bookings.Add(BookingRow(200_000, 20_000, 180_000, t0.AddHours(1), AppointmentStatus.Cancelled, refunded: 200_000));

        var svc = NewService(store, "PT");
        var ov = await svc.GetOverviewAsync(ptId, FinanceRange._30d, CancellationToken.None);

        // 450k + 180k = 630k share. 200k refunded * 0.9 = 180k deducted -> Net = 450k
        ov.NetEarningsVnd.Should().Be(450_000);
    }

    [Fact]
    public async Task GetOverview_NonPt_Forbidden()
    {
        var store = new InMemoryTrainerTrainerStoreWrapper();
        var svc = NewService(store, "Member"); // Role is Member, not PT
        var act = () => svc.GetOverviewAsync(Guid.NewGuid(), FinanceRange._30d, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task GetHistory_Paged_ReturnsItems()
    {
        var store = new InMemoryTrainerTrainerStoreWrapper();
        var ptId = Guid.NewGuid();
        for (int i = 0; i < 5; i++)
        {
            store.Bookings.Add(BookingRow(100_000, 10_000, 90_000, DateTime.UtcNow.AddDays(-i)));
        }

        var svc = NewService(store, "PT");
        var page = await svc.GetHistoryAsync(ptId, 1, 2, CancellationToken.None);

        page.Items.Should().HaveCount(2);
        page.Total.Should().Be(5);
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task SettleTrainer_LegacyRoute_RequiresReview_WithoutOutbox()
    {
        var store = new InMemoryTrainerTrainerStoreWrapper();
        var outbox = new CapturingOutbox();
        var ptId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var svc = NewService(store, "Admin", outbox);
        var req = new TrainerSettlementRequest(ptId, 500_000, "VNP_SETTLE_123456", "Weekly settlement");
        var act = () => svc.SettleTrainerAsync(adminId, req, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>().Where(e => e.Code == "MANUAL_SETTLEMENT_REVIEW_REQUIRED");
        outbox.Messages.Should().BeEmpty();
    }
    [Fact]
    public async Task SettleTrainer_NonAdmin_Forbidden()
    {
        var store = new InMemoryTrainerTrainerStoreWrapper();
        var svc = NewService(store, "PT"); // Role is PT, not Admin
        var req = new TrainerSettlementRequest(Guid.NewGuid(), 500_000, "VNP_123", null);
        var act = () => svc.SettleTrainerAsync(Guid.NewGuid(), req, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task SettleTrainer_ZeroOrNegativeAmount_ThrowsValidationException()
    {
        var store = new InMemoryTrainerTrainerStoreWrapper();
        var svc = NewService(store, "Admin");
        var req = new TrainerSettlementRequest(Guid.NewGuid(), 0, "VNP_123", null);
        var act = () => svc.SettleTrainerAsync(Guid.NewGuid(), req, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task SettleTrainer_MissingReferenceNumber_ThrowsValidationException()
    {
        var store = new InMemoryTrainerTrainerStoreWrapper();
        var svc = NewService(store, "Admin");
        var req = new TrainerSettlementRequest(Guid.NewGuid(), 500_000, "", null);
        var act = () => svc.SettleTrainerAsync(Guid.NewGuid(), req, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    private sealed class InMemoryTrainerTrainerStoreWrapper : ITrainerRevenueStore
    {
        public List<TrainerBookingRevenueRow> Bookings { get; } = new();

        public Task<IReadOnlyList<TrainerBookingRevenueRow>> ListBookingsInRangeAsync(
            Guid ptUserId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
        {
            var items = Bookings
                .Where(b => b.CreatedAt >= fromUtc && b.CreatedAt < toUtc)
                .ToList();
            return Task.FromResult<IReadOnlyList<TrainerBookingRevenueRow>>(items);
        }

        public Task<(IReadOnlyList<TrainerBookingRevenueRow> Items, int Total)> ListHistoryPagedAsync(
            Guid ptUserId, int skip, int take, CancellationToken ct)
        {
            var paged = Bookings.Skip(skip).Take(take).ToList();
            return Task.FromResult(((IReadOnlyList<TrainerBookingRevenueRow>)paged, Bookings.Count));
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }
}

