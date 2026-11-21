using Fitmate.Application.Payments;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Fitmate.Infrastructure.Payments;

public class EfPaymentStore : IPaymentStore
{
    private readonly FitmateDbContext _db;

    public EfPaymentStore(FitmateDbContext db) => _db = db;

    public Task<Payment?> FindByIdAsync(Guid id, CancellationToken ct)
        => _db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Payment?> FindByAppointmentIdAsync(Guid appointmentId, CancellationToken ct)
        => _db.Payments.FirstOrDefaultAsync(p => p.AppointmentId == appointmentId, ct);

    public async Task<BookingHold?> FindHoldAsync(Guid holdId, CancellationToken ct)
    {
        var hold = await _db.BookingHolds.FirstOrDefaultAsync(h => h.Id == holdId, ct);
        if (hold != null) return hold;

        var appt = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == holdId, ct);
        if (appt != null)
        {
            return await _db.BookingHolds
                .Where(h => h.MemberId == appt.MemberId && h.PtUserId == appt.PtUserId && h.SlotStartUtc == appt.SlotStartUtc && h.Status == "active")
                .OrderByDescending(h => h.CreatedAt)
                .FirstOrDefaultAsync(ct);
        }

        return null;
    }

    public Task<bool> IsPackageHoldAsync(Guid holdId, CancellationToken ct)
        => _db.SessionPackageSessions.AnyAsync(s => s.HoldId == holdId, ct);

    public async Task<Appointment?> FindAppointmentByHoldAsync(Guid holdId, CancellationToken ct)
    {
        var appt = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == holdId, ct);
        if (appt != null) return appt;

        var hold = await _db.BookingHolds.FirstOrDefaultAsync(h => h.Id == holdId, ct);
        if (hold == null) return null;

        return await _db.Appointments.FirstOrDefaultAsync(a =>
            a.PtUserId == hold.PtUserId &&
            a.MemberId == hold.MemberId &&
            a.SlotStartUtc == hold.SlotStartUtc &&
            (a.Status == AppointmentStatus.Held || a.Status == AppointmentStatus.Pending), ct);
    }

    public void Add(Payment payment) => _db.Payments.Add(payment);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            if (_db.Database.CurrentTransaction != null)
            {
                return await operation();
            }

            if (!_db.Database.IsRelational())
                return await operation();
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                var result = await operation();
                await tx.CommitAsync(ct);
                return result;
            }
        });
    }

    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken ct)
    {
        await ExecuteInTransactionAsync(async () =>
        {
            await operation();
            return true;
        }, ct);
    }

    public async Task<IExecutionTransaction> BeginTransactionAsync(CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction != null)
        {
            return new NullExecutionTransaction();
        }

        try
        {
            var tx = await _db.Database.BeginTransactionAsync(ct);
            return new EfExecutionTransaction(tx);
        }
        catch (InvalidOperationException)
        {
            // InMemory provider doesn't support transactions
            return new NullExecutionTransaction();
        }
    }

    public async Task<BookingHold?> FindHoldForUpdateAsync(Guid holdId, CancellationToken ct)
    {
        if (_db.Database.IsNpgsql())
        {
            var hold = await _db.BookingHolds
                .FromSqlInterpolated($"SELECT * FROM booking_holds WHERE id = {holdId} FOR UPDATE")
                .FirstOrDefaultAsync(ct);
            if (hold != null) return hold;

            var appt = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == holdId, ct);
            if (appt != null)
            {
                return await _db.BookingHolds
                    .FromSqlInterpolated($"SELECT * FROM booking_holds WHERE \"PtUserId\" = {appt.PtUserId} AND \"MemberId\" = {appt.MemberId} AND \"SlotStartUtc\" = {appt.SlotStartUtc} ORDER BY \"CreatedAt\" DESC LIMIT 1 FOR UPDATE")
                    .FirstOrDefaultAsync(ct);
            }

            return null;
        }

        var directHold = await _db.BookingHolds.FirstOrDefaultAsync(h => h.Id == holdId, ct);
        if (directHold != null) return directHold;

        var a = await _db.Appointments.FirstOrDefaultAsync(x => x.Id == holdId, ct);
        if (a != null)
        {
            return await _db.BookingHolds
                .OrderByDescending(h => h.CreatedAt)
                .FirstOrDefaultAsync(h =>
                    h.PtUserId == a.PtUserId &&
                    h.MemberId == a.MemberId &&
                    h.SlotStartUtc == a.SlotStartUtc, ct);
        }

        return null;
    }

    public Task<Appointment?> FindAppointmentForUpdateAsync(Guid appointmentId, CancellationToken ct)
    {
        if (_db.Database.IsNpgsql())
        {
            return _db.Appointments
                .FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {appointmentId} FOR UPDATE")
                .FirstOrDefaultAsync(ct);
        }

        return _db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId, ct);
    }

    public Task<Payment?> FindByIdForUpdateAsync(Guid paymentId, CancellationToken ct)
    {
        if (_db.Database.IsNpgsql())
        {
            return _db.Payments
                .FromSqlInterpolated($"SELECT * FROM payments WHERE id = {paymentId} FOR UPDATE")
                .FirstOrDefaultAsync(ct);
        }

        return _db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);
    }

    public void AddRefund(Refund refund) => _db.Refunds.Add(refund);

    public Task<Refund?> FindRefundByIdAsync(Guid refundId, CancellationToken ct)
        => _db.Refunds.FirstOrDefaultAsync(r => r.Id == refundId, ct);

    public async Task<long> GetPendingRefundsTotalAsync(Guid paymentId, CancellationToken ct)
    {
        return await _db.Refunds
            .Where(r => r.PaymentId == paymentId && r.Status == RefundStatus.Pending)
            .SumAsync(r => (long?)r.AmountVnd, ct) ?? 0L;
    }

    private sealed class EfExecutionTransaction : IExecutionTransaction
    {
        private readonly IDbContextTransaction _tx;

        public EfExecutionTransaction(IDbContextTransaction tx) => _tx = tx;

        public Task CommitAsync(CancellationToken ct) => _tx.CommitAsync(ct);
        public Task RollbackAsync(CancellationToken ct) => _tx.RollbackAsync(ct);
        public ValueTask DisposeAsync() => _tx.DisposeAsync();
    }

    private sealed class NullExecutionTransaction : IExecutionTransaction
    {
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
