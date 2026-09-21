using Fitmate.Domain.Booking;
using Fitmate.Domain.Payment;

namespace Fitmate.Application.Payments;

public interface IPaymentStore
{
    Task<Payment?> FindByIdAsync(Guid paymentId, CancellationToken ct);
    Task<Payment?> FindByAppointmentIdAsync(Guid appointmentId, CancellationToken ct);
    Task<BookingHold?> FindHoldAsync(Guid holdId, CancellationToken ct);
    Task<Appointment?> FindAppointmentByHoldAsync(Guid holdId, CancellationToken ct);
    void Add(Payment payment);
    Task SaveChangesAsync(CancellationToken ct);
}
