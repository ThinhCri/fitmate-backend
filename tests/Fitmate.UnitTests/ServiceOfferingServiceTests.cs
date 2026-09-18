using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-050: ServiceOfferingService — CRUD + ownership.
/// </summary>
public class ServiceOfferingServiceTests
{
    private sealed class InMemoryStore : IServiceOfferingStore
    {
        public Dictionary<Guid, Domain.Trainers.Service> Items { get; } = new();
        public int SaveCalls { get; private set; }

        public Task<Domain.Trainers.Service?> FindAsync(Guid serviceId, CancellationToken ct)
            => Task.FromResult(Items.GetValueOrDefault(serviceId));

        public Task<IReadOnlyList<Domain.Trainers.Service>> ListByPtAsync(Guid ptUserId, bool includeInactive, CancellationToken ct)
        {
            var q = Items.Values.Where(s => s.PtUserId == ptUserId);
            if (!includeInactive) q = q.Where(s => s.IsActive);
            return Task.FromResult<IReadOnlyList<Domain.Trainers.Service>>(q.OrderBy(s => s.CreatedAt).ToList());
        }

        public Task AddAsync(Domain.Trainers.Service service, CancellationToken ct)
        {
            Items[service.Id] = service;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private static ServiceOfferingService NewService(InMemoryStore store)
        => new(store, AlwaysAllowAuthz.Instance);

    [Fact]
    public async Task Create_ValidArgs_PersistsActive()
    {
        var store = new InMemoryStore();
        var svc = NewService(store);

        var s = await svc.CreateAsync(
            ptUserId: Guid.NewGuid(),
            name: "Personal Training 60min",
            description: "1-1 tập cường độ cao",
            priceVnd: 500_000,
            durationMinutes: 60,
            ct: CancellationToken.None);

        s.IsActive.Should().BeTrue();
        s.Name.Should().Be("Personal Training 60min");
        s.PriceVnd.Should().Be(500_000);
        s.DurationMinutes.Should().Be(60);
        store.Items.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(14)]
    [InlineData(241)]
    public async Task Create_BadDuration_Throws(int duration)
    {
        var store = new InMemoryStore();
        var svc = NewService(store);
        var act = () => svc.CreateAsync(Guid.NewGuid(), "S", null, 100_000, duration, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Create_NegativePrice_Throws()
    {
        var store = new InMemoryStore();
        var svc = NewService(store);
        var act = () => svc.CreateAsync(Guid.NewGuid(), "S", null, -100, 60, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Create_EmptyName_Throws()
    {
        var store = new InMemoryStore();
        var svc = NewService(store);
        var act = () => svc.CreateAsync(Guid.NewGuid(), "", null, 100_000, 60, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Update_Owner_UpdatesFields()
    {
        var store = new InMemoryStore();
        var pt = Guid.NewGuid();
        var s = Domain.Trainers.Service.Create(pt, "Old", null, 100_000, 60);
        store.Items[s.Id] = s;
        var svc = NewService(store);

        var updated = await svc.UpdateAsync(pt, s.Id, "New", "Desc mới", 200_000, 90, isActive: false, CancellationToken.None);

        updated.Name.Should().Be("New");
        updated.Description.Should().Be("Desc mới");
        updated.PriceVnd.Should().Be(200_000);
        updated.DurationMinutes.Should().Be(90);
        updated.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Update_NotOwner_ThrowsForbidden()
    {
        var store = new InMemoryStore();
        var owner = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        var s = Domain.Trainers.Service.Create(owner, "S", null, 100_000, 60);
        store.Items[s.Id] = s;

        var svc = NewService(store);
        var act = () => svc.UpdateAsync(attacker, s.Id, "X", null, null, null, null, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Update_NotFound_ThrowsNotFound()
    {
        var svc = NewService(new InMemoryStore());
        var act = () => svc.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), null, null, null, null, null, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Deactivate_SetsInactive()
    {
        var store = new InMemoryStore();
        var pt = Guid.NewGuid();
        var s = Domain.Trainers.Service.Create(pt, "S", null, 100_000, 60);
        store.Items[s.Id] = s;

        await NewService(store).DeactivateAsync(pt, s.Id, CancellationToken.None);

        store.Items[s.Id].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Get_NonOwner_ReturnsNull_DoesNotLeakExistence()
    {
        var store = new InMemoryStore();
        var owner = Guid.NewGuid();
        var s = Domain.Trainers.Service.Create(owner, "S", null, 100_000, 60);
        store.Items[s.Id] = s;

        var result = await NewService(store).GetAsync(Guid.NewGuid(), s.Id, CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListMine_FilterInactive()
    {
        var store = new InMemoryStore();
        var pt = Guid.NewGuid();
        var active = Domain.Trainers.Service.Create(pt, "Active", null, 100_000, 60);
        var inactive = Domain.Trainers.Service.Create(pt, "Inactive", null, 200_000, 60);
        inactive.Deactivate();
        store.Items[active.Id] = active;
        store.Items[inactive.Id] = inactive;

        var onlyActive = await NewService(store).ListMineAsync(pt, includeInactive: false, ct: CancellationToken.None);
        onlyActive.Should().HaveCount(1).And.Subject.First().Name.Should().Be("Active");

        var all = await NewService(store).ListMineAsync(pt, includeInactive: true, ct: CancellationToken.None);
        all.Should().HaveCount(2);
    }
}
