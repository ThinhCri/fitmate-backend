using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fitmate.Application.Booking;
using Fitmate.Application.Common;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fitmate.UnitTests;

public class BookingRescheduleServiceTests
{
    private sealed class InMemoryAppointmentStore : IAppointmentStore
    {
        public Dictionary<Guid, Appointment> Items { get; } = new();

        public Task<Appointment?> FindByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Items.GetValueOrDefault(id));

        public Task<bool> ExistsActiveForSlotAsync(Guid ptUserId, DateTime slotStartUtc, CancellationToken ct)
            => Task.FromResult(false);

        public Task<bool> HasOverlappingSlotAsync(Guid ptUserId, DateTime requestedStartUtc, int durationMinutes, Guid? excludeAppointmentId, CancellationToken ct)
        {
            var requestedEndUtc = requestedStartUtc.AddMinutes(durationMinutes);
            var nowUtc = DateTime.UtcNow;
            var overlaps = Items.Values.Any(a =>
                a.PtUserId == ptUserId &&
                (!excludeAppointmentId.HasValue || a.Id != excludeAppointmentId.Value) &&
                (a.Status == AppointmentStatus.Pending ||
                 a.Status == AppointmentStatus.Confirmed ||
                 a.Status == AppointmentStatus.InProgress ||
                 (a.Status == AppointmentStatus.Held && a.HoldExpiresAtUtc.HasValue && a.HoldExpiresAtUtc.Value > nowUtc)) &&
                requestedStartUtc < a.SlotStartUtc.AddMinutes(a.DurationMinutes) &&
                a.SlotStartUtc < requestedEndUtc);
            return Task.FromResult(overlaps);
        }

        public async Task<T> ExecuteWithScheduleLockAsync<T>(Guid ptUserId, Func<Task<T>> operation, CancellationToken ct)
            => await operation();

        public Task<(IReadOnlyList<Appointment> Items, int Total)> ListByMemberPagedAsync(Guid memberId, AppointmentStatus? status, int page, int pageSize, CancellationToken ct)
            => Task.FromResult(((IReadOnlyList<Appointment>)Array.Empty<Appointment>(), 0));

        public Task<(IReadOnlyList<Appointment> Items, int Total)> ListByPtPagedAsync(Guid ptUserId, AppointmentStatus? status, int page, int pageSize, CancellationToken ct)
            => Task.FromResult(((IReadOnlyList<Appointment>)Array.Empty<Appointment>(), 0));

        public Task<(IReadOnlyList<Appointment> Items, int Total)> ListAdminPagedAsync(AppointmentStatus? status, Guid? ptUserId, Guid? memberId, int page, int pageSize, CancellationToken ct)
            => Task.FromResult(((IReadOnlyList<Appointment>)Array.Empty<Appointment>(), 0));

        public Task<IReadOnlyList<Appointment>> GetExpiredHeldAppointmentsAsync(DateTime nowUtc, int limit, CancellationToken ct)
            => Task.FromResult((IReadOnlyList<Appointment>)Array.Empty<Appointment>());

        public Task<IReadOnlyList<Appointment>> GetExpiredPendingAppointmentsAsync(DateTime nowUtc, int limit, CancellationToken ct)
            => Task.FromResult((IReadOnlyList<Appointment>)Array.Empty<Appointment>());

        public Task AddAsync(Appointment appointment, CancellationToken ct)
        {
            Items[appointment.Id] = appointment;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class InMemoryRescheduleStore : IBookingRescheduleStore
    {
        public Dictionary<Guid, BookingRescheduleRequest> Items { get; } = new();

        public Task<BookingRescheduleRequest?> FindByIdAsync(Guid requestId, CancellationToken ct)
            => Task.FromResult(Items.GetValueOrDefault(requestId));

        public Task<BookingRescheduleRequest?> FindPendingByAppointmentIdAsync(Guid appointmentId, CancellationToken ct)
            => Task.FromResult(Items.Values.FirstOrDefault(r => r.AppointmentId == appointmentId && r.Status == RescheduleRequestStatus.Pending));

        public Task AddAsync(BookingRescheduleRequest request, CancellationToken ct)
        {
            Items[request.Id] = request;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CapturingOutbox : IOutboxDispatcher
    {
        public List<(string Type, object Payload)> Messages { get; } = new();
        public void Enqueue(string type, object payload) => Messages.Add((type, payload));
    }

    private static IOptions<RescheduleOptions> DefaultOptions()
        => Options.Create(new RescheduleOptions
        {
            MinLeadTimeBeforeOldSlot = TimeSpan.FromHours(2),
            MinLeadTimeForNewSlot = TimeSpan.FromHours(2),
            MaxAdvanceDays = 30,
            DefaultRequestTimeout = TimeSpan.FromHours(24)
        });

    [Fact]
    public async Task CreateRequest_HappyPath_CreatesPendingRequest_AndEnqueuesOutbox()
    {
        var apptStore = new InMemoryAppointmentStore();
        var reqStore = new InMemoryRescheduleStore();
        var outbox = new CapturingOutbox();

        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var oldSlot = DateTime.UtcNow.AddDays(2);
        var newSlot = DateTime.UtcNow.AddDays(3);

        var appt = Appointment.CreateHold(memberId, ptId, Guid.NewGuid(), oldSlot, oldSlot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ConfirmByPt();
        apptStore.Items[appt.Id] = appt;

        var svc = new BookingRescheduleService(apptStore, reqStore, outbox, DefaultOptions());

        var req = await svc.CreateRequestAsync(memberId, appt.Id, newSlot, "Need to change schedule for work trip", CancellationToken.None);

        req.Should().NotBeNull();
        req.Status.Should().Be(RescheduleRequestStatus.Pending);
        req.OldSlotStartUtc.Should().Be(oldSlot);
        req.NewSlotStartUtc.Should().Be(newSlot);
        req.RequestedByUserId.Should().Be(memberId);
        outbox.Messages.Should().Contain(m => m.Type == "booking.reschedule_requested");
    }

    [Fact]
    public async Task CreateRequest_OldSlotTooSoon_ThrowsConflict()
    {
        var apptStore = new InMemoryAppointmentStore();
        var reqStore = new InMemoryRescheduleStore();
        var outbox = new CapturingOutbox();

        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var oldSlot = DateTime.UtcNow.AddMinutes(90); // Only 1.5h away (< 2h)
        var newSlot = DateTime.UtcNow.AddDays(3);

        var appt = Appointment.CreateHold(memberId, ptId, Guid.NewGuid(), oldSlot, oldSlot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ConfirmByPt();
        apptStore.Items[appt.Id] = appt;

        var svc = new BookingRescheduleService(apptStore, reqStore, outbox, DefaultOptions());

        var act = () => svc.CreateRequestAsync(memberId, appt.Id, newSlot, "Reason with > 5 chars", CancellationToken.None);
        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("MIN_LEAD_TIME_OLD_SLOT_VIOLATED");
    }

    [Fact]
    public async Task CreateRequest_NewSlotTooSoon_ThrowsConflict()
    {
        var apptStore = new InMemoryAppointmentStore();
        var reqStore = new InMemoryRescheduleStore();
        var outbox = new CapturingOutbox();

        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var oldSlot = DateTime.UtcNow.AddDays(2);
        var newSlot = DateTime.UtcNow.AddMinutes(90); // Only 1.5h away (< 2h)

        var appt = Appointment.CreateHold(memberId, ptId, Guid.NewGuid(), oldSlot, oldSlot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ConfirmByPt();
        apptStore.Items[appt.Id] = appt;

        var svc = new BookingRescheduleService(apptStore, reqStore, outbox, DefaultOptions());

        var act = () => svc.CreateRequestAsync(memberId, appt.Id, newSlot, "Reason with > 5 chars", CancellationToken.None);
        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("MIN_LEAD_TIME_NEW_SLOT_VIOLATED");
    }

    [Fact]
    public async Task CreateRequest_NewSlotBeyond30Days_ThrowsConflict()
    {
        var apptStore = new InMemoryAppointmentStore();
        var reqStore = new InMemoryRescheduleStore();
        var outbox = new CapturingOutbox();

        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var oldSlot = DateTime.UtcNow.AddDays(2);
        var newSlot = DateTime.UtcNow.AddDays(35); // > 30 days

        var appt = Appointment.CreateHold(memberId, ptId, Guid.NewGuid(), oldSlot, oldSlot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ConfirmByPt();
        apptStore.Items[appt.Id] = appt;

        var svc = new BookingRescheduleService(apptStore, reqStore, outbox, DefaultOptions());

        var act = () => svc.CreateRequestAsync(memberId, appt.Id, newSlot, "Reason with > 5 chars", CancellationToken.None);
        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("MAX_ADVANCE_DAYS_EXCEEDED");
    }

    [Fact]
    public async Task CreateRequest_SlotTaken_ThrowsConflict()
    {
        var apptStore = new InMemoryAppointmentStore();
        var reqStore = new InMemoryRescheduleStore();
        var outbox = new CapturingOutbox();

        var memberId = Guid.NewGuid();
        var otherMember = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var oldSlot = DateTime.UtcNow.AddDays(2);
        var newSlot = DateTime.UtcNow.AddDays(3);

        var appt = Appointment.CreateHold(memberId, ptId, Guid.NewGuid(), oldSlot, oldSlot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ConfirmByPt();
        apptStore.Items[appt.Id] = appt;

        // Existing booking at newSlot
        var blockingAppt = Appointment.CreateHold(otherMember, ptId, Guid.NewGuid(), newSlot, newSlot, 60, 100_000);
        blockingAppt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        blockingAppt.ConfirmByPt();
        apptStore.Items[blockingAppt.Id] = blockingAppt;

        var svc = new BookingRescheduleService(apptStore, reqStore, outbox, DefaultOptions());

        var act = () => svc.CreateRequestAsync(memberId, appt.Id, newSlot, "Reason with > 5 chars", CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task AcceptRequest_HappyPath_AtomicSlotSwap_UpdatesAppointmentAndMarksAccepted()
    {
        var apptStore = new InMemoryAppointmentStore();
        var reqStore = new InMemoryRescheduleStore();
        var outbox = new CapturingOutbox();

        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var oldSlot = DateTime.UtcNow.AddDays(2);
        var newSlot = DateTime.UtcNow.AddDays(3);

        var appt = Appointment.CreateHold(memberId, ptId, Guid.NewGuid(), oldSlot, oldSlot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ConfirmByPt();
        apptStore.Items[appt.Id] = appt;

        var svc = new BookingRescheduleService(apptStore, reqStore, outbox, DefaultOptions());
        var req = await svc.CreateRequestAsync(memberId, appt.Id, newSlot, "Member needs reschedule", CancellationToken.None);

        // PT accepts
        var accepted = await svc.AcceptRequestAsync(ptId, req.Id, CancellationToken.None);

        accepted.Status.Should().Be(RescheduleRequestStatus.Accepted);
        accepted.RespondedAtUtc.Should().NotBeNull();
        appt.SlotStartUtc.Should().Be(newSlot);
        appt.RescheduledCount.Should().Be(1);
        outbox.Messages.Should().Contain(m => m.Type == "booking.rescheduled");
    }

    [Fact]
    public async Task AcceptRequest_SelfApprovalForbidden_ThrowsForbidden()
    {
        var apptStore = new InMemoryAppointmentStore();
        var reqStore = new InMemoryRescheduleStore();
        var outbox = new CapturingOutbox();

        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var oldSlot = DateTime.UtcNow.AddDays(2);
        var newSlot = DateTime.UtcNow.AddDays(3);

        var appt = Appointment.CreateHold(memberId, ptId, Guid.NewGuid(), oldSlot, oldSlot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ConfirmByPt();
        apptStore.Items[appt.Id] = appt;

        var svc = new BookingRescheduleService(apptStore, reqStore, outbox, DefaultOptions());
        var req = await svc.CreateRequestAsync(memberId, appt.Id, newSlot, "Member needs reschedule", CancellationToken.None);

        // Member tries to accept own request
        var act = () => svc.AcceptRequestAsync(memberId, req.Id, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task RejectRequest_ByOtherParty_MarksRejected()
    {
        var apptStore = new InMemoryAppointmentStore();
        var reqStore = new InMemoryRescheduleStore();
        var outbox = new CapturingOutbox();

        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var oldSlot = DateTime.UtcNow.AddDays(2);
        var newSlot = DateTime.UtcNow.AddDays(3);

        var appt = Appointment.CreateHold(memberId, ptId, Guid.NewGuid(), oldSlot, oldSlot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ConfirmByPt();
        apptStore.Items[appt.Id] = appt;

        var svc = new BookingRescheduleService(apptStore, reqStore, outbox, DefaultOptions());
        var req = await svc.CreateRequestAsync(memberId, appt.Id, newSlot, "Member needs reschedule", CancellationToken.None);

        // PT rejects
        var rejected = await svc.RejectRequestAsync(ptId, req.Id, "PT is busy at that time", CancellationToken.None);

        rejected.Status.Should().Be(RescheduleRequestStatus.Rejected);
        rejected.RejectionReason.Should().Be("PT is busy at that time");
        appt.SlotStartUtc.Should().Be(oldSlot); // Preserved
        outbox.Messages.Should().Contain(m => m.Type == "booking.reschedule_rejected");
    }

    [Fact]
    public async Task CancelRequest_ByRequester_MarksCancelled()
    {
        var apptStore = new InMemoryAppointmentStore();
        var reqStore = new InMemoryRescheduleStore();
        var outbox = new CapturingOutbox();

        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var oldSlot = DateTime.UtcNow.AddDays(2);
        var newSlot = DateTime.UtcNow.AddDays(3);

        var appt = Appointment.CreateHold(memberId, ptId, Guid.NewGuid(), oldSlot, oldSlot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ConfirmByPt();
        apptStore.Items[appt.Id] = appt;

        var svc = new BookingRescheduleService(apptStore, reqStore, outbox, DefaultOptions());
        var req = await svc.CreateRequestAsync(memberId, appt.Id, newSlot, "Member needs reschedule", CancellationToken.None);

        // Member cancels own request
        var cancelled = await svc.CancelRequestAsync(memberId, req.Id, CancellationToken.None);

        cancelled.Status.Should().Be(RescheduleRequestStatus.Cancelled);
        appt.SlotStartUtc.Should().Be(oldSlot);
        outbox.Messages.Should().Contain(m => m.Type == "booking.reschedule_cancelled");
    }

    [Fact]
    public async Task CancelRequest_ByNonRequester_ThrowsForbidden()
    {
        var apptStore = new InMemoryAppointmentStore();
        var reqStore = new InMemoryRescheduleStore();
        var outbox = new CapturingOutbox();

        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var oldSlot = DateTime.UtcNow.AddDays(2);
        var newSlot = DateTime.UtcNow.AddDays(3);

        var appt = Appointment.CreateHold(memberId, ptId, Guid.NewGuid(), oldSlot, oldSlot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ConfirmByPt();
        apptStore.Items[appt.Id] = appt;

        var svc = new BookingRescheduleService(apptStore, reqStore, outbox, DefaultOptions());
        var req = await svc.CreateRequestAsync(memberId, appt.Id, newSlot, "Member needs reschedule", CancellationToken.None);

        // PT tries to cancel member's request -> Forbidden
        var act = () => svc.CancelRequestAsync(ptId, req.Id, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task AcceptRequest_DeadlineExpired_MarksExpiredAndEnqueuesOutbox_AndThrowsConflict()
    {
        var apptStore = new InMemoryAppointmentStore();
        var reqStore = new InMemoryRescheduleStore();
        var outbox = new CapturingOutbox();

        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var oldSlot = DateTime.UtcNow.AddDays(2);
        var newSlot = DateTime.UtcNow.AddDays(3);

        var appt = Appointment.CreateHold(memberId, ptId, Guid.NewGuid(), oldSlot, oldSlot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ConfirmByPt();
        apptStore.Items[appt.Id] = appt;

        var svc = new BookingRescheduleService(apptStore, reqStore, outbox, DefaultOptions());
        var pastDeadline = DateTime.UtcNow.AddMinutes(-10);
        var req = BookingRescheduleRequest.Create(
            appt.Id, memberId, appt.SlotStartUtc, newSlot, appt.DurationMinutes, "Member needs reschedule", pastDeadline);
        reqStore.Items[req.Id] = req;

        // Try to accept
        var act = () => svc.AcceptRequestAsync(ptId, req.Id, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("DEADLINE_EXPIRED");

        reqStore.Items[req.Id].Status.Should().Be(RescheduleRequestStatus.Expired, "Status must be Expired in store");
        outbox.Messages.Should().Contain(m => m.Type == "booking.reschedule_expired");
    }

    [Fact]
    public async Task AcceptRequest_SlotConflict_MarksRejectedAndEnqueuesOutbox_AndThrowsConflict()
    {
        var apptStore = new InMemoryAppointmentStore();
        var reqStore = new InMemoryRescheduleStore();
        var outbox = new CapturingOutbox();

        var memberId = Guid.NewGuid();
        var otherMember = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var oldSlot = DateTime.UtcNow.AddDays(2);
        var newSlot = DateTime.UtcNow.AddDays(3);

        var appt = Appointment.CreateHold(memberId, ptId, Guid.NewGuid(), oldSlot, oldSlot, 60, 100_000);
        appt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        appt.ConfirmByPt();
        apptStore.Items[appt.Id] = appt;

        var svc = new BookingRescheduleService(apptStore, reqStore, outbox, DefaultOptions());
        var req = await svc.CreateRequestAsync(memberId, appt.Id, newSlot, "Member needs reschedule", CancellationToken.None);

        // Now another appointment takes the newSlot
        var blockingAppt = Appointment.CreateHold(otherMember, ptId, Guid.NewGuid(), newSlot, newSlot, 60, 100_000);
        blockingAppt.AttachPayment(Guid.NewGuid(), 100_000, 0, 0.10m, 10_000, 90_000, "v1");
        blockingAppt.ConfirmByPt();
        apptStore.Items[blockingAppt.Id] = blockingAppt;

        // PT tries to accept, but slot is taken
        var act = () => svc.AcceptRequestAsync(ptId, req.Id, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("SLOT_NO_LONGER_AVAILABLE");

        reqStore.Items[req.Id].Status.Should().Be(RescheduleRequestStatus.Rejected, "Status must be persisted as Rejected");
        outbox.Messages.Should().Contain(m => m.Type == "booking.reschedule_rejected");
    }
}
