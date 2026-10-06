using Fitmate.Application.Auth;
using Fitmate.Application.Workout;
using Fitmate.Domain.Common;
using Fitmate.Domain.Workout;
using FluentAssertions;

namespace Fitmate.UnitTests;

public class WeightServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class InMemoryWeightStore : IWeightStore
    {
        public List<WeightLog> Logs { get; } = new();

        public Task<IReadOnlyList<WeightLog>> ListAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WeightLog>>(Logs.Where(w => w.UserId == userId && !w.IsDeleted).OrderByDescending(w => w.RecordedAt).ToList());

        public Task<WeightLog?> FindByIdAsync(Guid weightLogId, CancellationToken ct) =>
            Task.FromResult(Logs.FirstOrDefault(w => w.Id == weightLogId && !w.IsDeleted));

        public Task AddAsync(WeightLog log, CancellationToken ct)
        {
            Logs.Add(log);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(WeightLog log, CancellationToken ct)
        {
            log.SoftDelete();
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static WeightService SUT(InMemoryWeightStore? store = null)
    {
        store ??= new InMemoryWeightStore();
        return new WeightService(store, AlwaysAllowAuthz.Instance);
    }

    [Fact]
    public async Task ListAsync_returns_empty_when_no_logs()
    {
        var result = await SUT().ListAsync(UserId);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_returns_user_logs_sorted()
    {
        var store = new InMemoryWeightStore();
        var l1 = WeightLog.Create(UserId, 70.5m, DateTime.UtcNow.AddDays(-2));
        var l2 = WeightLog.Create(UserId, 71.0m, DateTime.UtcNow.AddDays(-1));
        var otherUserLog = WeightLog.Create(Guid.NewGuid(), 80.0m, DateTime.UtcNow);
        store.Logs.Add(l1);
        store.Logs.Add(l2);
        store.Logs.Add(otherUserLog);

        var result = await SUT(store).ListAsync(UserId);

        result.Should().HaveCount(2);
        result[0].WeightKg.Should().Be(71.0m);
        result[1].WeightKg.Should().Be(70.5m);
    }

    [Fact]
    public async Task LogAsync_creates_weight_entry()
    {
        var store = new InMemoryWeightStore();
        var request = new LogWeightRequest
        {
            WeightKg = 72.5m,
            RecordedAt = DateTime.UtcNow
        };

        var result = await SUT(store).LogAsync(UserId, request);

        result.Should().NotBeNull();
        result.WeightKg.Should().Be(72.5m);
        store.Logs.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(500)]
    public async Task LogAsync_throws_validation_when_weight_out_of_range(decimal weight)
    {
        var request = new LogWeightRequest
        {
            WeightKg = weight
        };

        var act = () => SUT().LogAsync(UserId, request);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task LogAsync_throws_validation_when_recorded_at_far_future()
    {
        var request = new LogWeightRequest
        {
            WeightKg = 70.0m,
            RecordedAt = DateTime.UtcNow.AddDays(5)
        };

        var act = () => SUT().LogAsync(UserId, request);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task DeleteAsync_soft_deletes_owned_weight_entry()
    {
        var store = new InMemoryWeightStore();
        var log = WeightLog.Create(UserId, 70m, DateTime.UtcNow);
        store.Logs.Add(log);

        await SUT(store).DeleteAsync(UserId, log.Id);

        log.IsDeleted.Should().BeTrue();
        (await SUT(store).ListAsync(UserId)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_hides_foreign_weight_entry_as_not_found()
    {
        var store = new InMemoryWeightStore();
        var log = WeightLog.Create(Guid.NewGuid(), 70m, DateTime.UtcNow);
        store.Logs.Add(log);

        var act = () => SUT(store).DeleteAsync(UserId, log.Id);

        await act.Should().ThrowAsync<NotFoundException>();
        log.IsDeleted.Should().BeFalse();
    }
}

