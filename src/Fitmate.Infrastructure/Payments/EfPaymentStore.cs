using Fitmate.Application.Payments;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Payments;

public class EfPaymentStore : IPaymentStore
{
    private readonly FitmateDbContext _db;

    public EfPaymentStore(FitmateDbContext db) => _db = db;

    public Task<Payment?> FindByIdAsync(Guid id, CancellationToken ct)
        => _db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Payment?> FindByAppointmentIdAsync(Guid appointmentId, CancellationToken ct)
        => _db.Payments.FirstOrDefaultAsync(p => p.AppointmentId == appointmentId, ct);

    public Task<BookingHold?> FindHoldAsync(Guid holdId, CancellationToken ct)
        => _db.BookingHolds.FirstOrDefaultAsync(h => h.Id == holdId, ct);

    public Task<Appointment?> FindAppointmentByHoldAsync(Guid holdId, CancellationToken ct)
        => _db.Appointments.FirstOrDefaultAsync(a => a.Id == holdId, ct);

    public void Add(Payment payment) => _db.Payments.Add(payment);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
