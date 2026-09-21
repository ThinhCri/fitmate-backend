using Fitmate.Application.Booking;
using Fitmate.Application.Common;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-060: BookingService — createHold + cancel + complete + reschedule + ownership + concurrency.
/// </summary>
public class BookingServiceTests
{
    private sealed class InMemoryAppointmentStore : IAppointmentStore
    {
        public Dictionary<Guid, Appointment> Items { get; } = new();
        public int SaveCalls { get; private set; }
        public List<(Guid, DateTime)> ActiveQueries { get; } = new();

        public Task<Appointment?> FindByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Items.GetValueOrDefault(id));

        public Task<bool> ExistsActiveForSlotAsync(Guid ptUserId, DateTime slotStartUtc, CancellationToken ct)
        {
            ActiveQueries.Add((ptUserId, slotStartUtc));
            return Task.FromResult(Items.Values.Any(a =>
                a.PtUserId == ptUserId && a.SlotStartUtc == slotStartUtc &&
                (a.Status == BookingStatus.Held || a.Status == BookingStatus.AwaitingPayment ||
                 a.Status == BookingStatus.Paid || a.Status == BookingStatus.Completed)));
        }

        public Task<(IReadOnlyList<Appointment> Items, int Total)> ListByMemberPagedAsync(Guid memberId, BookingStatus? status, int page, int pageSize, CancellationToken ct)
        {
            var filtered = Items.Values.Where(a => a.MemberId == memberId && (!status.HasValue || a.Status == status.Value)).ToList();
            return Task.FromResult(((IReadOnlyList<Appointment>)filtered, filtered.Count));
        }

        public Task<(IReadOnlyList<Appointment> Items, int Total)> ListByPtPagedAsync(Guid ptUserId, BookingStatus? status, int page, int pageSize, CancellationToken ct)
        {
            var filtered = Items.Values.Where(a => a.PtUserId == ptUserId && (!status.HasValue || a.Status == status.Value)).ToList();
            return Task.FromResult(((IReadOnlyList<Appointment>)filtered, filtered.Count));
        }

        public Task AddAsync(Appointment a, CancellationToken ct)
        {
            Items[a.Id] = a;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryHoldStore : IBookingHoldStore
    {
        public Dictionary<Guid, BookingHold> Items { get; } = new();
        public Task<BookingHold?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.GetValueOrDefault(id));
        public Task AddAsync(BookingHold h, CancellationToken ct) { Items[h.Id] = h; return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubServiceLookup : IServiceBookingLookup
    {
        public Dictionary<Guid, ServiceSnapshot> Items { get; } = new();
        public Task<ServiceSnapshot?> FindActiveAsync(Guid serviceId, CancellationToken ct)
            => Task.FromResult(Items.GetValueOrDefault(serviceId));
    }

    private sealed class CapturingOutbox : Fitmate.Application.Outbox.IOutboxDispatcher
    {
        public List<(string Type, object Payload)> Messages { get; } = new();
        public void Enqueue(string type, object payload) => Messages.Add((type, payload));
    }

    private static BookingService NewService(InMemoryAppointmentStore apptStore, InMemoryHoldStore holdStore, StubServiceLookup lookup, CapturingOutbox outbox)
        => new(apptStore, holdStore, lookup, outbox, AlwaysAllowAuthz.Instance);

    private static ServiceSnapshot Snapshot(Guid ptUserId, long price = 500_000, int duration = 60) =>
        new(Guid.NewGuid(), ptUserId, price, duration, IsActive: true);

    [Fact]
    public async Task CreateHold_HappyPath_InsertsHoldAndAppointment_AndOutbox()
    {
        var apptStore = new InMemoryAppointmentStore();
        var holdStore = new InMemoryHoldStore();
        var lookup = new StubServiceLookup();
        var outbox = new CapturingOutbox();
        var pt = Guid.NewGuid();
        lookup.Items[Guid.NewGuid()] = Snapshot(pt);

        var svc = NewService(apptStore, holdStore, lookup, outbox);
        var serviceId = lookup.Items.First().Key;
        var memberId = Guid.NewGuid();
        var slot = new DateTime(2026, 12, 5, 9, 0, 0, DateTimeKind.Utc);

        var (hold, appt) = await svc.CreateHoldAsync(memberId, pt, serviceId, slot, CancellationToken.None);

        hold.MemberId.Should().Be(memberId);
        appt.Status.Should().Be(BookingStatus.Held);
        appt.PtUserId.Should().Be(pt);
        appt.PriceListedVnd.Should().Be(500_000);
        appt.DurationMinutes.Should().Be(60);
        apptStore.SaveCalls.Should().Be(1);
        outbox.Messages.Should().Contain(m => m.Type == "booking.held");
    }

    [Fact]
    public async Task CreateHold_ServiceNotFound_ThrowsNotFound()
    {
        var svc = NewService(new InMemoryAppointmentStore(), new InMemoryHoldStore(), new StubServiceLookup(), new CapturingOutbox());
        var act = () => svc.CreateHoldAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateHold_ServiceBelongsToOtherPt_ThrowsValidation()
    {
        var apptStore = new InMemoryAppointmentStore();
        var lookup = new StubServiceLookup();
        var ownerPt = Guid.NewGuid();
        var otherPt = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        lookup.Items[serviceId] = Snapshot(ownerPt);

        var svc = NewService(apptStore, new InMemoryHoldStore(), lookup, new CapturingOutbox());
        var act = () => svc.CreateHoldAsync(Guid.NewGuid(), otherPt, serviceId, DateTime.UtcNow, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task CreateHold_SlotAlreadyActive_ThrowsConflict()
    {
        var apptStore = new InMemoryAppointmentStore();
        var holdStore = new InMemoryHoldStore();
        var lookup = new StubServiceLookup();
        var pt = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        lookup.Items[serviceId] = Snapshot(pt);
        // Pre-existing active appointment at slot.
        var slot = new DateTime(2026, 12, 5, 9, 0, 0, DateTimeKind.Utc);
        var busy = Appointment.CreateHold(Guid.NewGuid(), pt, serviceId, slot, slot, 60, 500_000);
        apptStore.Items[busy.Id] = busy;

        var svc = NewService(apptStore, holdStore, lookup, new CapturingOutbox());
        var act = () => svc.CreateHoldAsync(Guid.NewGuid(), pt, serviceId, slot, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.Code == ErrorCodes.Booking.SlotTaken);
    }

    [Fact]
    public async Task Cancel_ReasonTooShort_Throws()
    {
        var svc = NewService(new InMemoryAppointmentStore(), new InMemoryHoldStore(), new StubServiceLookup(), new CapturingOutbox());
        var apptId = Guid.NewGuid();
        var act = () => svc.CancelAsync(Guid.NewGuid(), apptId, "abc", CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Cancel_NotActor_ThrowsForbidden()
    {
        var apptStore = new InMemoryAppointmentStore();
        var member = Guid.NewGuid();
        var pt = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        var appt = Appointment.CreateHold(member, pt, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);
        apptStore.Items[appt.Id] = appt;

        var svc = NewService(apptStore, new InMemoryHoldStore(), new StubServiceLookup(), new CapturingOutbox());
        var act = () => svc.CancelAsync(attacker, appt.Id, "member muốn hủy vì bận", CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Cancel_HappyPath_MarksCancelled_AndOutboxEvent()
    {
        var apptStore = new InMemoryAppointmentStore();
        var outbox = new CapturingOutbox();
        var member = Guid.NewGuid();
        var appt = Appointment.CreateHold(member, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);
        apptStore.Items[appt.Id] = appt;

        var svc = NewService(apptStore, new InMemoryHoldStore(), new StubServiceLookup(), outbox);
        var cancelled = await svc.CancelAsync(member, appt.Id, "Bận đột xuất", CancellationToken.None);

        cancelled.Status.Should().Be(BookingStatus.Cancelled);
        cancelled.Note.Should().Be("Bận đột xuất");
        outbox.Messages.Should().Contain(m => m.Type == "booking.cancelled");
    }

    [Fact]
    public async Task Complete_NotPaid_ThrowsConflict()
    {
        var apptStore = new InMemoryAppointmentStore();
        var pt = Guid.NewGuid();
        var appt = Appointment.CreateHold(Guid.NewGuid(), pt, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);
        apptStore.Items[appt.Id] = appt;

        var svc = NewService(apptStore, new InMemoryHoldStore(), new StubServiceLookup(), new CapturingOutbox());
        var act = () => svc.CompleteAsync(pt, appt.Id, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Complete_HappyPath_MarksCompleted_AndOutbox()
    {
        var apptStore = new InMemoryAppointmentStore();
        var outbox = new CapturingOutbox();
        var pt = Guid.NewGuid();
        var appt = Appointment.CreateHold(Guid.NewGuid(), pt, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 500_000, 0, 0.10m, 50_000, 450_000, "v1");
        apptStore.Items[appt.Id] = appt;

        var svc = NewService(apptStore, new InMemoryHoldStore(), new StubServiceLookup(), outbox);
        var done = await svc.CompleteAsync(pt, appt.Id, CancellationToken.None);

        done.Status.Should().Be(BookingStatus.Completed);
        outbox.Messages.Should().Contain(m => m.Type == "booking.completed");
    }

    [Fact]
    public async Task Complete_OtherPt_ThrowsForbidden()
    {
        var apptStore = new InMemoryAppointmentStore();
        var ownerPt = Guid.NewGuid();
        var appt = Appointment.CreateHold(Guid.NewGuid(), ownerPt, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        apptStore.Items[appt.Id] = appt;

        var svc = NewService(apptStore, new InMemoryHoldStore(), new StubServiceLookup(), new CapturingOutbox());
        var act = () => svc.CompleteAsync(Guid.NewGuid(), appt.Id, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Reschedule_SameSlot_ThrowsValidation()
    {
        var apptStore = new InMemoryAppointmentStore();
        var member = Guid.NewGuid();
        var pt = Guid.NewGuid();
        var slot = new DateTime(2026, 12, 5, 9, 0, 0, DateTimeKind.Utc);
        var appt = Appointment.CreateHold(member, pt, Guid.NewGuid(), slot, slot, 60, 100_000);
        apptStore.Items[appt.Id] = appt;

        var svc = NewService(apptStore, new InMemoryHoldStore(), new StubServiceLookup(), new CapturingOutbox());
        var act = () => svc.RescheduleAsync(member, appt.Id, slot, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Reschedule_NewSlotBusy_ThrowsConflict_DoesNotMutateOldAppointment()
    {
        var apptStore = new InMemoryAppointmentStore();
        var member = Guid.NewGuid();
        var pt = Guid.NewGuid();
        var oldSlot = new DateTime(2026, 12, 5, 9, 0, 0, DateTimeKind.Utc);
        var newSlot = new DateTime(2026, 12, 6, 9, 0, 0, DateTimeKind.Utc);
        var appt = Appointment.CreateHold(member, pt, Guid.NewGuid(), oldSlot, oldSlot, 60, 100_000);
        apptStore.Items[appt.Id] = appt;
        // Đã có appointment khác ở newSlot.
        var blocker = Appointment.CreateHold(Guid.NewGuid(), pt, Guid.NewGuid(), newSlot, newSlot, 60, 100_000);
        apptStore.Items[blocker.Id] = blocker;

        var svc = NewService(apptStore, new InMemoryHoldStore(), new StubServiceLookup(), new CapturingOutbox());
        var act = () => svc.RescheduleAsync(member, appt.Id, newSlot, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.Code == ErrorCodes.Booking.SlotTaken);

        // Old appointment vẫn Held (domain-rules.md §3.1.1: reschedule fail → slot cũ không bị mất).
        apptStore.Items[appt.Id].Status.Should().Be(BookingStatus.Held);
    }

    [Fact]
    public async Task Reschedule_CompletedAppointment_ThrowsConflict()
    {
        var apptStore = new InMemoryAppointmentStore();
        var member = Guid.NewGuid();
        var pt = Guid.NewGuid();
        var oldSlot = new DateTime(2026, 12, 5, 9, 0, 0, DateTimeKind.Utc);
        var newSlot = new DateTime(2026, 12, 6, 9, 0, 0, DateTimeKind.Utc);
        var appt = Appointment.CreateHold(member, pt, Guid.NewGuid(), oldSlot, oldSlot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.CompleteByPt();
        apptStore.Items[appt.Id] = appt;

        var svc = NewService(apptStore, new InMemoryHoldStore(), new StubServiceLookup(), new CapturingOutbox());
        var act = () => svc.RescheduleAsync(member, appt.Id, newSlot, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Reschedule_HappyPath_CreatesNewAppointment_OldCancelled()
    {
        var apptStore = new InMemoryAppointmentStore();
        var outbox = new CapturingOutbox();
        var member = Guid.NewGuid();
        var pt = Guid.NewGuid();
        var oldSlot = new DateTime(2026, 12, 5, 9, 0, 0, DateTimeKind.Utc);
        var newSlot = new DateTime(2026, 12, 6, 9, 0, 0, DateTimeKind.Utc);
        var appt = Appointment.CreateHold(member, pt, Guid.NewGuid(), oldSlot, oldSlot, 60, 100_000);
        apptStore.Items[appt.Id] = appt;

        var svc = NewService(apptStore, new InMemoryHoldStore(), new StubServiceLookup(), outbox);
        var newAppt = await svc.RescheduleAsync(member, appt.Id, newSlot, CancellationToken.None);

        newAppt.SlotStartUtc.Should().Be(newSlot);
        newAppt.MemberId.Should().Be(member);
        apptStore.Items[appt.Id].Status.Should().Be(BookingStatus.Cancelled); // old cancelled
        apptStore.Items.Should().HaveCount(2);
        outbox.Messages.Should().Contain(m => m.Type == "booking.rescheduled");
    }

    [Fact]
    public async Task ListByMember_Filter()
    {
        var apptStore = new InMemoryAppointmentStore();
        var member = Guid.NewGuid();
        var pt = Guid.NewGuid();
        apptStore.Items[Guid.NewGuid()] = Appointment.CreateHold(member, pt, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);
        apptStore.Items[Guid.NewGuid()] = Appointment.CreateHold(member, pt, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);

        var svc = NewService(apptStore, new InMemoryHoldStore(), new StubServiceLookup(), new CapturingOutbox());
        var (items, total) = await svc.ListByMemberAsync(member, null, 1, 10, CancellationToken.None);
        total.Should().Be(2);
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListByPt_ReturnsOnlyPtAppointments()
    {
        var apptStore = new InMemoryAppointmentStore();
        var pt = Guid.NewGuid();
        var memberA = Guid.NewGuid();
        var memberB = Guid.NewGuid();
        var otherPt = Guid.NewGuid();
        apptStore.Items[Guid.NewGuid()] = Appointment.CreateHold(memberA, pt, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);
        apptStore.Items[Guid.NewGuid()] = Appointment.CreateHold(memberB, pt, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);
        apptStore.Items[Guid.NewGuid()] = Appointment.CreateHold(memberA, otherPt, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);

        var svc = NewService(apptStore, new InMemoryHoldStore(), new StubServiceLookup(), new CapturingOutbox());
        var (items, total) = await svc.ListByPtAsync(pt, null, 1, 10, CancellationToken.None);

        total.Should().Be(2);
        items.Should().HaveCount(2);
        items.Should().OnlyContain(a => a.PtUserId == pt);
    }

    [Fact]
    public async Task ListByMember_StatusFilter_NarrowsResults()
    {
        var apptStore = new InMemoryAppointmentStore();
        var member = Guid.NewGuid();
        var pt = Guid.NewGuid();
        var a1 = Appointment.CreateHold(member, pt, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);
        var a2 = Appointment.CreateHold(member, pt, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);
        apptStore.Items[a1.Id] = a1;
        apptStore.Items[a2.Id] = a2;

        var svc = NewService(apptStore, new InMemoryHoldStore(), new StubServiceLookup(), new CapturingOutbox());

        // All held.
        var (allItems, allTotal) = await svc.ListByMemberAsync(member, null, 1, 10, CancellationToken.None);
        allTotal.Should().Be(2);

        // Only completed — none yet.
        var (completedItems, completedTotal) = await svc.ListByMemberAsync(member, BookingStatus.Completed, 1, 10, CancellationToken.None);
        completedTotal.Should().Be(0);
        completedItems.Should().BeEmpty();
    }

    [Fact]
    public async Task ListByMember_Pagination_TotalReflectsAllItems()
    {
        // Pagination semantics (skip/take) live in EfAppointmentStore and are
        // verified by integration tests against a real Postgres. Here we verify
        // the service contract: total reflects the full filtered set regardless
        // of page/pageSize (caller is responsible for skip/take at store layer).
        var apptStore = new InMemoryAppointmentStore();
        var member = Guid.NewGuid();
        var pt = Guid.NewGuid();
        for (int i = 0; i < 5; i++)
        {
            apptStore.Items[Guid.NewGuid()] = Appointment.CreateHold(member, pt, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 100_000);
        }

        var svc = NewService(apptStore, new InMemoryHoldStore(), new StubServiceLookup(), new CapturingOutbox());

        var (page1Items, page1Total) = await svc.ListByMemberAsync(member, null, 1, 2, CancellationToken.None);
        page1Total.Should().Be(5);

        var (page3Items, page3Total) = await svc.ListByMemberAsync(member, null, 3, 2, CancellationToken.None);
        page3Total.Should().Be(5);
        // Service returns whatever the store returns. InMemory stub doesn't paginate,
        // so all pages see all 5. Real Ef store will skip/take per page.
        page3Items.Count.Should().BeLessThanOrEqualTo(5);
    }
}
