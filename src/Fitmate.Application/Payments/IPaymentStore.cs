using Fitmate.Domain.Booking;
using Fitmate.Domain.Payment;

namespace Fitmate.Application.Payments;

public interface IExecutionTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
}

public interface IPaymentStore
{
    Task<Payment?> FindByIdAsync(Guid paymentId, CancellationToken ct);
    Task<Payment?> FindByAppointmentIdAsync(Guid appointmentId, CancellationToken ct);
    Task<BookingHold?> FindHoldAsync(Guid holdId, CancellationToken ct);
    Task<bool> IsPackageHoldAsync(Guid holdId, CancellationToken ct);
    Task<Appointment?> FindAppointmentByHoldAsync(Guid holdId, CancellationToken ct);
    void Add(Payment payment);
    Task SaveChangesAsync(CancellationToken ct);

    // Concurrency locking & transaction methods
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct);
    Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken ct);
    Task<BookingHold?> FindHoldForUpdateAsync(Guid holdId, CancellationToken ct);
    Task<Appointment?> FindAppointmentForUpdateAsync(Guid appointmentId, CancellationToken ct);
    Task<Payment?> FindByIdForUpdateAsync(Guid paymentId, CancellationToken ct);

    // Refund support
    void AddRefund(Refund refund);
    Task<Refund?> FindRefundByIdAsync(Guid refundId, CancellationToken ct);
    Task<long> GetPendingRefundsTotalAsync(Guid paymentId, CancellationToken ct);
}
