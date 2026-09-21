using Fitmate.Domain.Booking;
using Fitmate.Domain.Payments;

namespace Fitmate.Application.Payments;

public interface IPaymentStore
{
    Task<Payment?> FindByIdAsync(Guid paymentId, CancellationToken ct);
    Task<Payment?> FindByHoldIdAsync(Guid holdId, CancellationToken ct);
    Task<BookingHold?> FindHoldAsync(Guid holdId, CancellationToken ct);
    Task AddAsync(Payment payment, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
