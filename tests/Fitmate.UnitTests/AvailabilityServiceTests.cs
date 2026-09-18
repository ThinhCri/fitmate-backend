using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-050: AvailabilityService — ReplaceMine + ListOpenForTrainer.
/// </summary>
public class AvailabilityServiceTests
{
    private sealed class InMemoryStore : IAvailabilitySlotStore
    {
        public List<AvailabilitySlot> Items { get; } = new();
        public int SaveCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public Task<IReadOnlyList<AvailabilitySlot>> ListByPtAsync(Guid ptUserId, AvailabilityStatus[] statuses, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AvailabilitySlot>>(Items
                .Where(s => s.PtUserId == ptUserId && statuses.Contains(s.Status) && s.SlotStartUtc >= fromUtc && s.SlotStartUtc < toUtc)
                .OrderBy(s => s.SlotStartUtc)
                .ToList());

        public Task<IReadOnlyList<AvailabilitySlot>> ListOpenByPtAsync(Guid ptUserId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
            => ListByPtAsync(ptUserId, new[] { AvailabilityStatus.Open }, fromUtc, toUtc, ct);

        public Task DeleteByPtAndStatusAsync(Guid ptUserId, AvailabilityStatus status, CancellationToken ct)
        {
            DeleteCalls++;
            var removed = Items.Where(s => s.PtUserId == ptUserId && s.Status == status).ToList();
            foreach (var s in removed) Items.Remove(s);
            return Task.CompletedTask;
        }

        public Task AddAsync(AvailabilitySlot slot, CancellationToken ct)
        {
            Items.Add(slot);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private static AvailabilityService NewService(InMemoryStore store) => new(store);

    private static SlotInput MakeSlot(DateTime local, string tz = "Asia/Ho_Chi_Minh", int duration = 60)
        => new(local, tz, duration);

    [Fact]
    public async Task ReplaceMine_NewBatch_DeletesOldOpenAndInsertsNew()
    {
        var store = new InMemoryStore();
        var pt = Guid.NewGuid();
        var oldSlot = AvailabilitySlot.Create(pt, new DateTime(2026, 12, 1, 9, 0, 0), "Asia/Ho_Chi_Minh", 60);
        store.Items.Add(oldSlot);

        var newSlots = new[]
        {
            MakeSlot(new DateTime(2026, 12, 5, 10, 0, 0)),
            MakeSlot(new DateTime(2026, 12, 5, 14, 0, 0)),
        };
        var result = await NewService(store).ReplaceMineAsync(pt, newSlots, CancellationToken.None);

        result.Should().HaveCount(2);
        store.Items.Should().HaveCount(2); // old Open đã xóa, 2 mới thêm vào
        store.DeleteCalls.Should().Be(1);
        store.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task ReplaceMine_DuplicateUtcInBatch_ThrowsConflict()
    {
        var store = new InMemoryStore();
        var pt = Guid.NewGuid();
        var slots = new[]
        {
            MakeSlot(new DateTime(2026, 12, 5, 10, 0, 0)),
            MakeSlot(new DateTime(2026, 12, 5, 10, 0, 0)),
        };
        var svc = NewService(store);
        var act = () => svc.ReplaceMineAsync(pt, slots, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>()
            .Where(ex => ex.Code == ErrorCodes.Booking.SlotTaken);
    }

    [Fact]
    public async Task ReplaceMine_Empty_Throws()
    {
        var svc = NewService(new InMemoryStore());
        var act = () => svc.ReplaceMineAsync(Guid.NewGuid(), Array.Empty<SlotInput>(), CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Theory]
    [InlineData(14)]
    [InlineData(241)]
    public async Task ReplaceMine_BadDuration_Throws(int duration)
    {
        var svc = NewService(new InMemoryStore());
        var slots = new[] { MakeSlot(new DateTime(2026, 12, 5, 10, 0, 0), duration: duration) };
        var act = () => svc.ReplaceMineAsync(Guid.NewGuid(), slots, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ReplaceMine_BadTimezone_Throws()
    {
        var svc = NewService(new InMemoryStore());
        var slots = new[] { new SlotInput(new DateTime(2026, 12, 5, 10, 0, 0), "", 60) };
        var act = () => svc.ReplaceMineAsync(Guid.NewGuid(), slots, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ListOpenForTrainer_RangeQuery()
    {
        var store = new InMemoryStore();
        var pt = Guid.NewGuid();
        var s1 = AvailabilitySlot.Create(pt, new DateTime(2026, 12, 5, 9, 0, 0), "Asia/Ho_Chi_Minh", 60);  // 02:00 UTC
        var s2 = AvailabilitySlot.Create(pt, new DateTime(2026, 12, 6, 9, 0, 0), "Asia/Ho_Chi_Minh", 60);  // 02:00 UTC next day
        store.Items.AddRange(new[] { s1, s2 });

        var slots = await NewService(store).ListOpenForTrainerAsync(
            pt,
            fromUtc: new DateTime(2026, 12, 5, 0, 0, 0, DateTimeKind.Utc),
            toUtc: new DateTime(2026, 12, 5, 23, 59, 59, DateTimeKind.Utc),
            ct: CancellationToken.None);

        slots.Should().HaveCount(1);
        slots[0].Id.Should().Be(s1.Id);
    }

    [Fact]
    public async Task ListOpenForTrainer_ToNotAfterFrom_Throws()
    {
        var svc = NewService(new InMemoryStore());
        var act = () => svc.ListOpenForTrainerAsync(
            Guid.NewGuid(),
            fromUtc: DateTime.UtcNow,
            toUtc: DateTime.UtcNow.AddMinutes(-1),
            ct: CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }
}
