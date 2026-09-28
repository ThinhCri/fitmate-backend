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

        public Task ReplaceScopedAsync(Guid ptUserId, DateTime fromUtc, DateTime toUtc, IReadOnlyList<AvailabilitySlot> newSlots, CancellationToken ct)
        {
            var toRemove = Items.Where(s => s.PtUserId == ptUserId && s.Status == AvailabilityStatus.Open && s.SlotStartUtc >= fromUtc && s.SlotStartUtc < toUtc).ToList();
            foreach (var r in toRemove) Items.Remove(r);
            Items.AddRange(newSlots);
            SaveCalls++;
            return Task.CompletedTask;
        }

        public Task<string?> GetPtTimezoneAsync(Guid ptUserId, CancellationToken ct)
            => Task.FromResult<string?>("Asia/Ho_Chi_Minh");

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

    private static AvailabilityService NewService(InMemoryStore store) => new(store, AlwaysAllowAuthz.Instance);

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

    [Fact]
    public async Task ReplaceScoped_ReplacesOnlyOpenInRange_PreservesOutsideAndHeld()
    {
        var store = new InMemoryStore();
        var pt = Guid.NewGuid();
        var svc = NewService(store);

        // Outside range (yesterday): Open
        var outsideOpen = AvailabilitySlot.Create(pt, new DateTime(2026, 12, 1, 9, 0, 0), "Asia/Ho_Chi_Minh", 60);
        // Inside range: Open (should be replaced)
        var insideOpen = AvailabilitySlot.Create(pt, new DateTime(2026, 12, 5, 9, 0, 0), "Asia/Ho_Chi_Minh", 60);
        // Inside range: Booked (should NEVER be replaced or deleted)
        var insideBooked = AvailabilitySlot.Create(pt, new DateTime(2026, 12, 5, 14, 0, 0), "Asia/Ho_Chi_Minh", 60);
        insideBooked.MarkBooked();

        store.Items.AddRange(new[] { outsideOpen, insideOpen, insideBooked });

        var fromUtc = new DateTime(2026, 12, 5, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 12, 6, 0, 0, 0, DateTimeKind.Utc);

        // New slot inside range (at 10:00 local, doesn't overlap 14:00 booked)
        var newSlot = MakeSlot(new DateTime(2026, 12, 5, 10, 0, 0), "Asia/Ho_Chi_Minh", 60);

        var result = await svc.ReplaceScopedAsync(pt, fromUtc, toUtc, new[] { newSlot }, CancellationToken.None);

        result.Should().HaveCount(1);
        store.Items.Should().Contain(outsideOpen);
        store.Items.Should().NotContain(insideOpen);
        store.Items.Should().Contain(insideBooked);
        store.Items.Should().Contain(s => s.SlotStartLocal == new DateTime(2026, 12, 5, 10, 0, 0));
    }

    [Fact]
    public async Task ReplaceScoped_SlotOutsideRange_ThrowsValidationException()
    {
        var store = new InMemoryStore();
        var pt = Guid.NewGuid();
        var svc = NewService(store);

        var fromUtc = new DateTime(2026, 12, 5, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 12, 6, 0, 0, 0, DateTimeKind.Utc);

        // Slot on Dec 4 (outside range)
        var slotOutside = MakeSlot(new DateTime(2026, 12, 4, 10, 0, 0), "Asia/Ho_Chi_Minh", 60);

        var act = () => svc.ReplaceScopedAsync(pt, fromUtc, toUtc, new[] { slotOutside }, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ReplaceScoped_IntervalOverlapInBatch_ThrowsConflictException()
    {
        var store = new InMemoryStore();
        var pt = Guid.NewGuid();
        var svc = NewService(store);

        var fromUtc = new DateTime(2026, 12, 5, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 12, 6, 0, 0, 0, DateTimeKind.Utc);

        // Slot 1: 09:00 - 10:00 (60m)
        var slot1 = MakeSlot(new DateTime(2026, 12, 5, 9, 0, 0), "Asia/Ho_Chi_Minh", 60);
        // Slot 2: 09:30 - 10:30 (overlaps with Slot 1)
        var slot2 = MakeSlot(new DateTime(2026, 12, 5, 9, 30, 0), "Asia/Ho_Chi_Minh", 60);

        var act = () => svc.ReplaceScopedAsync(pt, fromUtc, toUtc, new[] { slot1, slot2 }, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task ReplaceScoped_OverlapWithHeldOrBooked_ThrowsConflictException()
    {
        var store = new InMemoryStore();
        var pt = Guid.NewGuid();
        var svc = NewService(store);

        var fromUtc = new DateTime(2026, 12, 5, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 12, 6, 0, 0, 0, DateTimeKind.Utc);

        // Existing held slot at 09:00 for 60m
        var existingHeld = AvailabilitySlot.Create(pt, new DateTime(2026, 12, 5, 9, 0, 0), "Asia/Ho_Chi_Minh", 60);
        existingHeld.MarkHeld();
        store.Items.Add(existingHeld);

        // New slot at 09:30 for 60m (overlaps with 09:00-10:00 held slot)
        var newOverlapping = MakeSlot(new DateTime(2026, 12, 5, 9, 30, 0), "Asia/Ho_Chi_Minh", 60);

        var act = () => svc.ReplaceScopedAsync(pt, fromUtc, toUtc, new[] { newOverlapping }, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task ListAuthoritativeForPt_ReturnsAllSlotStatuses()
    {
        var store = new InMemoryStore();
        var pt = Guid.NewGuid();
        var svc = NewService(store);

        var sOpen = AvailabilitySlot.Create(pt, new DateTime(2026, 12, 5, 9, 0, 0), "Asia/Ho_Chi_Minh", 60);
        var sHeld = AvailabilitySlot.Create(pt, new DateTime(2026, 12, 5, 11, 0, 0), "Asia/Ho_Chi_Minh", 60);
        sHeld.MarkHeld();
        var sBooked = AvailabilitySlot.Create(pt, new DateTime(2026, 12, 5, 14, 0, 0), "Asia/Ho_Chi_Minh", 60);
        sBooked.MarkBooked();

        store.Items.AddRange(new[] { sOpen, sHeld, sBooked });

        var fromUtc = new DateTime(2026, 12, 5, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 12, 6, 0, 0, 0, DateTimeKind.Utc);

        var slots = await svc.ListAuthoritativeForPtAsync(pt, fromUtc, toUtc, CancellationToken.None);

        slots.Should().HaveCount(3);
        slots.Select(s => s.Status).Should().Contain(new[] { AvailabilityStatus.Open, AvailabilityStatus.Held, AvailabilityStatus.Booked });
    }
}
