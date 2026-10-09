using Fitmate.Domain.Booking;
using Fitmate.Domain.Payment;

namespace Fitmate.Application.Payments;

public interface IPackagePaymentStore
{
    Task<SessionPackage?> FindWithSessionsAsync(Guid packageId, CancellationToken ct);
    Task<Payment?> FindPaymentAsync(Guid packageId, CancellationToken ct);
    Task<bool> AllReservationsActiveAsync(SessionPackage package, DateTime nowUtc, CancellationToken ct);
}
