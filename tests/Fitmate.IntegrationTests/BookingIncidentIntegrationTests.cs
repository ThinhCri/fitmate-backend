using Fitmate.Application.Finance;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Finance;
using Fitmate.Domain.Payment;
using Fitmate.Domain.Support;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fitmate.IntegrationTests;

public sealed class BookingIncidentIntegrationTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;

    public BookingIncidentIntegrationTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        factory.EnsureDatabaseInitialized();
    }

    private async Task<(Guid TicketId, Guid AppointmentId, Guid PaymentId)> SeedAsync(AppointmentStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var start = DateTime.UtcNow.AddHours(-3);
        var appointment = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            start, start, 60, 100_000);
        var payment = Payment.CreatePending(appointment.Id, PaymentMethod.Payos, 100_000, null);
        payment.MarkPaid("incident-test-" + Guid.NewGuid());
        appointment.AttachPayment(payment.Id, 100_000, 0, .10m, 10_000, 90_000, "v1");
        appointment.ForceStatusForTesting(status);
        var ticket = SupportTicket.Create(appointment.MemberId, "Sự cố buổi tập",
            "booking_dispute", SupportTicketPriority.High, "Tôi cần kiểm tra buổi tập này.");
        ticket.LinkBooking(appointment.Id);
        db.AddRange(appointment, payment, ticket);
        await db.SaveChangesAsync();
        return (ticket.Id, appointment.Id, payment.Id);
    }

    [Fact]
    public async Task PtNoShow_CancelsOnlyAffectedSessionAndCreatesPendingManualRefund()
    {
        var seed = await SeedAsync(AppointmentStatus.Confirmed);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IManualSettlementStore>();
        var admin = Guid.NewGuid();

        var decision = await store.ResolveIncidentAsync(admin, seed.TicketId,
            BookingIncidentOutcome.PtNoShow, "PT không xuất hiện, đã xác minh với hai bên.",
            "private:test-evidence", CancellationToken.None);

        decision.RefundId.Should().NotBeNull();
        (await db.Appointments.SingleAsync(a => a.Id == seed.AppointmentId)).Status
            .Should().Be(AppointmentStatus.Cancelled);
        var refund = await db.Refunds.SingleAsync(r => r.Id == decision.RefundId);
        refund.Status.Should().Be(RefundStatus.Pending);
        refund.AmountVnd.Should().Be(100_000);
        (await db.Payments.SingleAsync(p => p.Id == seed.PaymentId)).Status.Should().Be(PaymentStatus.Paid);
        var ticket = await db.SupportTickets.SingleAsync(t => t.Id == seed.TicketId);
        ticket.BookingResolution.Should().Be(BookingIncidentOutcome.PtNoShow);
        var retry = () => store.ResolveIncidentAsync(admin, seed.TicketId,
            BookingIncidentOutcome.PtNoShow, "PT không xuất hiện, đã xác minh với hai bên.",
            "private:test-evidence", CancellationToken.None);
        await retry.Should().ThrowAsync<ConflictException>();
        (await db.Refunds.CountAsync(r => r.PaymentId == seed.PaymentId)).Should().Be(1);
    }

    [Fact]
    public async Task MemberNoShow_RecognizesSessionRevenueOnce()
    {
        var seed = await SeedAsync(AppointmentStatus.Confirmed);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IManualSettlementStore>();

        var decision = await store.ResolveIncidentAsync(Guid.NewGuid(), seed.TicketId,
            BookingIncidentOutcome.MemberNoShow,
            "Hội viên không đến theo chứng cứ hai bên cung cấp.",
            "private:test-evidence", CancellationToken.None);

        decision.RefundId.Should().BeNull();
        (await db.Appointments.SingleAsync(a => a.Id == seed.AppointmentId)).Status
            .Should().Be(AppointmentStatus.Completed);
        var entries = await db.FinancialLedgerEntries.Where(e => e.AppointmentId == seed.AppointmentId)
            .ToListAsync();
        entries.Should().ContainSingle(e => e.EntryType == FinancialLedgerEntryTypes.GrossBookingFee && e.AmountVnd == 100_000);
        entries.Should().ContainSingle(e => e.EntryType == FinancialLedgerEntryTypes.PlatformCommission && e.AmountVnd == 10_000);
        entries.Should().ContainSingle(e => e.EntryType == FinancialLedgerEntryTypes.TrainerPayable && e.AmountVnd == 90_000);
    }

    [Fact]
    public async Task DisputedSession_AdminCanVerifyItWasDelivered()
    {
        var seed = await SeedAsync(AppointmentStatus.Disputed);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IManualSettlementStore>();

        var decision = await store.ResolveIncidentAsync(Guid.NewGuid(), seed.TicketId,
            BookingIncidentOutcome.SessionDelivered,
            "Buổi tập đã diễn ra, đã đối chiếu thông tin của hai bên.",
            "private:delivered-evidence", CancellationToken.None);

        decision.RefundId.Should().BeNull();
        (await db.Appointments.SingleAsync(a => a.Id == seed.AppointmentId)).Status
            .Should().Be(AppointmentStatus.Completed);
        (await db.FinancialLedgerEntries.CountAsync(e => e.AppointmentId == seed.AppointmentId))
            .Should().Be(3);
    }

    [Fact]
    public async Task GenericClose_CannotReleaseUnresolvedBookingIncident()
    {
        var seed = await SeedAsync(AppointmentStatus.Confirmed);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var ticket = await db.SupportTickets.SingleAsync(t => t.Id == seed.TicketId);
        var close = () => ticket.ChangeStatus(SupportTicketStatus.Closed);
        close.Should().Throw<ConflictException>();
        ticket.BookingResolution.Should().BeNull();
    }

    [Fact]
    public async Task PackagePtNoShow_RefundsOnlyOneAllocatedSession()
    {
        using var seedScope = _factory.Services.CreateScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var member = Guid.NewGuid();
        var pt = Guid.NewGuid();
        var service = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var appointments = Enumerable.Range(0, 4).Select(index =>
        {
            var start = now.AddHours(index == 0 ? -3 : 24 * index);
            return Appointment.CreateHold(member, pt, service, start, start, 60, 100_000);
        }).ToArray();
        var holds = appointments.Select(a => BookingHold.Create(member, pt, service,
            a.SlotStartUtc, TimeSpan.FromMinutes(10))).ToArray();
        var package = SessionPackage.CreateHold(member, pt, service, 100_000,
            appointments.Select((a, index) => (holds[index].Id, a.Id, a.SlotStartUtc)).ToArray(),
            now.AddDays(-7), TimeSpan.FromMinutes(10));
        seedDb.BookingHolds.AddRange(holds);
        seedDb.SessionPackages.Add(package);
        seedDb.Appointments.AddRange(appointments);
        await seedDb.SaveChangesAsync();
        var payment = Payment.CreatePendingForPackage(package.Id, PaymentMethod.Payos, package.TotalPriceVnd);
        package.AttachPayment(payment.Id, now.AddDays(-7).AddMinutes(1));
        payment.MarkPaid("incident-package-" + Guid.NewGuid());
        package.MarkPaid(payment.Id, package.TotalPriceVnd, now.AddDays(-7).AddMinutes(2));
        foreach (var session in package.Sessions)
        {
            var appointment = appointments.Single(a => a.Id == session.AppointmentId);
            appointment.AttachPayment(payment.Id, session.PaidAllocationVnd, 0, .10m,
                session.PlatformFeeAllocationVnd, session.TrainerShareAllocationVnd, "v1");
            appointment.ForceStatusForTesting(AppointmentStatus.Confirmed);
        }
        var ticket = SupportTicket.Create(member, "PT không có mặt", "booking_dispute",
            SupportTicketPriority.High, "PT không đến buổi đầu tiên.");
        ticket.LinkBooking(appointments[0].Id);
        seedDb.AddRange(payment, ticket);
        await seedDb.SaveChangesAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IManualSettlementStore>();
        var decision = await store.ResolveIncidentAsync(Guid.NewGuid(), ticket.Id,
            BookingIncidentOutcome.PtNoShow,
            "PT vắng mặt, đã kiểm tra chứng cứ của hội viên và PT.",
            "private:package-incident", CancellationToken.None);

        var firstAllocation = package.Sessions.Single(s => s.AppointmentId == appointments[0].Id).PaidAllocationVnd;
        var refund = await db.Refunds.SingleAsync(r => r.Id == decision.RefundId);
        refund.AmountVnd.Should().Be(firstAllocation);
        refund.AppointmentId.Should().Be(appointments[0].Id);
        (await db.Appointments.SingleAsync(a => a.Id == appointments[0].Id)).Status.Should().Be(AppointmentStatus.Cancelled);
        (await db.Appointments.Where(a => appointments.Skip(1).Select(x => x.Id).Contains(a.Id)).ToListAsync())
            .Should().OnlyContain(a => a.Status == AppointmentStatus.Confirmed);
    }
}
